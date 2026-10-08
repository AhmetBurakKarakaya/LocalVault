using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Vault.Core.Matching;
using Vault.Core.Models;
using Vault.Core.Otp;

namespace Vault.Bridge;

/// <summary>Açık kasaya erişim (masaüstü uygulaması uygular; çağrılar UI iş parçacığında yapılır).</summary>
public interface IVaultAccess
{
    bool IsUnlocked { get; }
    VaultData Data { get; }
    void Save();
}

/// <summary>Tarayıcı isteklerinin kullanıcıya yansıyan kısmı.</summary>
public interface IBridgeUi
{
    /// <summary>Yeni eklenti bağlantısı için kullanıcı onayı ister.</summary>
    Task<bool> ApproveAssociationAsync(string browser, string verificationCode, CancellationToken ct);

    /// <summary>Kasa kilitliyken kullanıcı tarayıcıdan işlem başlattı: pencereyi öne getir.</summary>
    void RequestUnlock();

    /// <summary>Uygulama penceresini göster.</summary>
    void ShowApp();

    /// <summary>Bilgi mesajı (ör. "GitHub parolası Chrome'a gönderildi").</summary>
    void Notify(string message);
}

/// <summary>
/// Tarayıcı isteklerini doğrular ve yanıtlar. Güvenlik kuralları:
/// - ping ve associate dışındaki tüm istekler eşleştirme anahtarıyla HMAC imzalı olmalı,
/// - zaman damgası ±60 sn içinde olmalı ve her nonce yalnızca bir kez kullanılabilir (tekrar oynatma koruması),
/// - parola/OTP yalnızca istenen URL kayıtla gerçekten eşleşiyorsa verilir.
/// </summary>
public sealed class BridgeRequestHandler(IVaultAccess vault, IBridgeUi ui, TimeProvider? time = null)
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, DateTimeOffset> _seenNonces = [];
    private readonly Lock _nonceLock = new();

    public async Task<BridgeResponse> HandleAsync(BridgeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(request.Id) || request.Id.Length > 64)
            return BridgeResponse.Failure(request.Id ?? "", ErrorCodes.BadRequest, "Geçersiz istek kimliği.");

        try
        {
            switch (request.Type)
            {
                case RequestTypes.Ping:
                    return Ok(request, null, new PingResult { Locked = !vault.IsUnlocked }, BridgeJsonContext.Default.PingResult);
                case RequestTypes.Associate:
                    return await AssociateAsync(request, ct);
                case RequestTypes.ShowApp:
                    ui.ShowApp();
                    return new BridgeResponse { Id = request.Id, Ok = true };
            }

            // Buradan sonrası imzalı istekler.
            if (!vault.IsUnlocked)
            {
                // Kasa kilitliyken anahtarlar okunamaz; kullanıcı işlem başlattıysa pencereyi aç.
                if (request.Type != RequestTypes.TestAssociate && IsInteractive(request))
                    ui.RequestUnlock();
                return BridgeResponse.Failure(request.Id, ErrorCodes.Locked, "Kasa kilitli.");
            }

            var (association, key, error) = Authenticate(request);
            if (error is not null)
                return error;

            return request.Type switch
            {
                RequestTypes.TestAssociate => Ok(request, key, new AssociateResult { Name = association!.Name },
                    BridgeJsonContext.Default.AssociateResult),
                RequestTypes.GetLogins => GetLogins(request, key!),
                RequestTypes.GetCredentials => GetCredentials(request, key!, association!),
                RequestTypes.GetTotp => GetTotp(request, key!),
                RequestTypes.CheckLogin => CheckLogin(request, key!),
                RequestTypes.SaveLogin => SaveLogin(request, key!, association!),
                RequestTypes.UpdatePassword => UpdatePassword(request, key!, association!),
                _ => BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, $"Bilinmeyen istek türü: {request.Type}"),
            };
        }
        catch (JsonException)
        {
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "İstek içeriği okunamadı.");
        }
    }

    // ---- Eşleştirme ----

    private async Task<BridgeResponse> AssociateAsync(BridgeRequest request, CancellationToken ct)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.AssociateRequest);
        if (payload is null || string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 64)
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "Eşleştirme isteği eksik.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(payload.Key);
        }
        catch (FormatException)
        {
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "Anahtar geçersiz.");
        }
        if (key.Length != MessageAuth.KeySize)
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "Anahtar 32 bayt olmalı.");

        if (!vault.IsUnlocked)
        {
            ui.RequestUnlock();
            return BridgeResponse.Failure(request.Id, ErrorCodes.Locked, "Eşleştirmek için önce kasanın kilidini açın.");
        }

        var browser = string.IsNullOrWhiteSpace(payload.Browser) ? "Tarayıcı" : payload.Browser.Trim();
        if (browser.Length > 40)
            browser = browser[..40];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ApprovalTimeout);
        bool approved;
        try
        {
            approved = await ui.ApproveAssociationAsync(browser, MessageAuth.VerificationCode(key), timeout.Token);
        }
        catch (OperationCanceledException)
        {
            approved = false;
        }

        if (!approved)
            return BridgeResponse.Failure(request.Id, ErrorCodes.Denied, "Bağlantı isteği reddedildi.");
        if (!vault.IsUnlocked)
            return BridgeResponse.Failure(request.Id, ErrorCodes.Locked, "Kasa onay sırasında kilitlendi.");

        var name = UniqueName(browser);
        vault.Data.BrowserAssociations.RemoveAll(a => a.ClientId == request.ClientId);
        vault.Data.BrowserAssociations.Add(new BrowserAssociation
        {
            ClientId = request.ClientId,
            Key = payload.Key,
            Name = name,
            CreatedAt = _time.GetUtcNow(),
        });
        vault.Save();
        ui.Notify($"{name} eklentisi bağlandı.");

        return Ok(request, key, new AssociateResult { Name = name }, BridgeJsonContext.Default.AssociateResult);
    }

    private string UniqueName(string browser)
    {
        var names = vault.Data.BrowserAssociations.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(browser))
            return browser;
        for (var i = 2; ; i++)
            if (!names.Contains($"{browser} ({i})"))
                return $"{browser} ({i})";
    }

    private (BrowserAssociation? Association, byte[]? Key, BridgeResponse? Error) Authenticate(BridgeRequest request)
    {
        var association = vault.Data.BrowserAssociations.Find(a => a.ClientId == request.ClientId);
        if (association is null)
            return (null, null, BridgeResponse.Failure(request.Id, ErrorCodes.Unauthorized, "Bu eklenti kasayla eşleştirilmemiş."));

        var key = Convert.FromBase64String(association.Key);
        if (!MessageAuth.VerifyRequest(key, request))
            return (null, null, BridgeResponse.Failure(request.Id, ErrorCodes.Unauthorized, "İstek imzası geçersiz."));

        var now = _time.GetUtcNow();
        var sentAt = DateTimeOffset.FromUnixTimeSeconds(request.Ts!.Value);
        if ((now - sentAt).Duration() > MaxClockSkew)
            return (null, null, BridgeResponse.Failure(request.Id, ErrorCodes.Unauthorized, "İstek zaman aşımına uğradı."));

        if (!RegisterNonce($"{request.ClientId}:{request.Nonce}", now))
            return (null, null, BridgeResponse.Failure(request.Id, ErrorCodes.Unauthorized, "İstek tekrar gönderilmiş."));

        return (association, key, null);
    }

    private bool RegisterNonce(string nonce, DateTimeOffset now)
    {
        lock (_nonceLock)
        {
            // Saat sapma penceresinin iki katından eski nonce'lar zaten zaman damgası kontrolünde reddedilir.
            foreach (var old in _seenNonces.Where(p => now - p.Value > MaxClockSkew * 2).Select(p => p.Key).ToList())
                _seenNonces.Remove(old);
            return _seenNonces.TryAdd(nonce, now);
        }
    }

    // ---- Kayıtlar ----

    private BridgeResponse GetLogins(BridgeRequest request, byte[] key)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.UrlRequest);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Url))
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "URL eksik.");

        var list = new LoginList
        {
            Logins = UrlMatcher.FindMatches(vault.Data.Entries, payload.Url)
                .Select(m => new LoginSummary
                {
                    Id = m.Entry.Id.ToString("N"),
                    Title = m.Entry.Title,
                    Username = m.Entry.Username,
                    HasPassword = m.Entry.Password.Length > 0,
                    HasTotp = m.Entry.Totp is not null,
                })
                .ToList(),
        };
        return Ok(request, key, list, BridgeJsonContext.Default.LoginList);
    }

    private BridgeResponse GetCredentials(BridgeRequest request, byte[] key, BrowserAssociation association)
    {
        var (entry, error) = FindMatchingEntry(request);
        if (error is not null)
            return error;

        ui.Notify($"'{entry!.Title}' bilgileri {association.Name} eklentisine gönderildi.");
        return Ok(request, key, new Credentials { Username = entry.Username, Password = entry.Password },
            BridgeJsonContext.Default.Credentials);
    }

    private BridgeResponse GetTotp(BridgeRequest request, byte[] key)
    {
        var (entry, error) = FindMatchingEntry(request);
        if (error is not null)
            return error;
        if (entry!.Totp is null)
            return BridgeResponse.Failure(request.Id, ErrorCodes.NotFound, "Bu kayıtta doğrulama kodu tanımlı değil.");

        var code = OtpGenerator.ComputeTotp(entry.Totp, _time.GetUtcNow());
        return Ok(request, key, new TotpResult { Code = code.Code, Remaining = code.RemainingSeconds, Period = code.Period },
            BridgeJsonContext.Default.TotpResult);
    }

    // ---- Yeni hesap kaydetme / parola güncelleme ----

    private const int MaxFieldLength = 4096;

    private static string? ValidateSubmission(string url, string username, string password)
    {
        if (!UrlMatcher.TryParse(url, out _))
            return "Geçersiz sayfa adresi.";
        if (username.Length > MaxFieldLength || password.Length > MaxFieldLength)
            return "Değer çok uzun.";
        if (password.Length == 0)
            return "Parola boş.";
        return null;
    }

    /// <summary>Gönderilen giriş bilgisinin kasadaki durumunu bildirir; parola eklentiye geri dönmez.</summary>
    private BridgeResponse CheckLogin(BridgeRequest request, byte[] key)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.LoginSubmission);
        var invalid = payload is null ? "İstek eksik." : ValidateSubmission(payload.Url, payload.Username, payload.Password);
        if (invalid is not null)
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, invalid);

        var matches = UrlMatcher.FindMatches(vault.Data.Entries, payload!.Url).Select(m => m.Entry).ToList();
        var sameUser = matches.Where(e => e.Username.Equals(payload.Username, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var result = new LoginCheckResult();
        if (sameUser.FirstOrDefault(e => e.Password == payload.Password) is { } exact)
        {
            result.Status = LoginCheckStatus.Exists;
            result.EntryId = exact.Id.ToString("N");
            result.Title = exact.Title;
        }
        else if (sameUser.FirstOrDefault() is { } changed)
        {
            result.Status = LoginCheckStatus.Changed;
            result.EntryId = changed.Id.ToString("N");
            result.Title = changed.Title;
        }
        return Ok(request, key, result, BridgeJsonContext.Default.LoginCheckResult);
    }

    private BridgeResponse SaveLogin(BridgeRequest request, byte[] key, BrowserAssociation association)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.LoginSubmission);
        var invalid = payload is null ? "İstek eksik." : ValidateSubmission(payload.Url, payload.Username, payload.Password);
        if (invalid is not null)
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, invalid);

        UrlMatcher.TryParse(payload!.Url, out var uri);
        var host = uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost;
        var entry = new VaultEntry
        {
            Title = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host,
            Username = payload.Username.Trim(),
            Password = payload.Password,
            // Kayıt, formun bulunduğu adresin kökü için yapılır (alt alan adlarını da kapsar).
            Urls = [$"{uri.Scheme}://{(uri.IsDefaultPort ? host : $"{host}:{uri.Port}")}"],
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        };
        vault.Data.Entries.Add(entry);
        vault.Save();
        ui.Notify($"'{entry.Title}' {association.Name} eklentisinden kaydedildi.");
        return Ok(request, key, new SavedEntry { EntryId = entry.Id.ToString("N"), Title = entry.Title },
            BridgeJsonContext.Default.SavedEntry);
    }

    private BridgeResponse UpdatePassword(BridgeRequest request, byte[] key, BrowserAssociation association)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.UpdatePasswordRequest);
        if (payload is null || !Guid.TryParse(payload.EntryId, out var id))
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "Kayıt kimliği eksik.");
        if (ValidateSubmission(payload.Url, "", payload.Password) is { } invalid)
            return BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, invalid);

        var entry = vault.Data.FindById(id);
        if (entry is null || UrlMatcher.FindMatches([entry], payload.Url).Count == 0)
            return BridgeResponse.Failure(request.Id, ErrorCodes.NotFound, "Bu sayfa için böyle bir kayıt yok.");

        entry.Password = payload.Password;
        entry.Touch();
        vault.Save();
        ui.Notify($"'{entry.Title}' parolası {association.Name} eklentisinden güncellendi.");
        return Ok(request, key, new SavedEntry { EntryId = entry.Id.ToString("N"), Title = entry.Title },
            BridgeJsonContext.Default.SavedEntry);
    }

    /// <summary>Kaydı bulur ve istenen URL ile eşleştiğini yeniden doğrular (eklentiye güvenmeden).</summary>
    private (VaultEntry? Entry, BridgeResponse? Error) FindMatchingEntry(BridgeRequest request)
    {
        var payload = Parse(request.Payload, BridgeJsonContext.Default.EntryRequest);
        if (payload is null || !Guid.TryParse(payload.EntryId, out var id) || string.IsNullOrWhiteSpace(payload.Url))
            return (null, BridgeResponse.Failure(request.Id, ErrorCodes.BadRequest, "Kayıt kimliği veya URL eksik."));

        var entry = vault.Data.FindById(id);
        if (entry is null || UrlMatcher.FindMatches([entry], payload.Url).Count == 0)
            return (null, BridgeResponse.Failure(request.Id, ErrorCodes.NotFound, "Bu sayfa için böyle bir kayıt yok."));
        return (entry, null);
    }

    // ---- Yardımcılar ----

    private static bool IsInteractive(BridgeRequest request)
    {
        // Kilitliyken imza doğrulanamaz; yalnızca "interactive" bayrağına bakılır (en kötü ihtimalle pencere açılır).
        try
        {
            return Parse(request.Payload, BridgeJsonContext.Default.UrlRequest)?.Interactive == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static T? Parse<T>(string? json, JsonTypeInfo<T> type) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize(json, type);

    private static BridgeResponse Ok<T>(BridgeRequest request, byte[]? key, T payload, JsonTypeInfo<T> type)
    {
        var response = new BridgeResponse
        {
            Id = request.Id,
            Ok = true,
            Payload = JsonSerializer.Serialize(payload, type),
        };
        if (key is not null)
            MessageAuth.SignResponse(key, response);
        return response;
    }
}
