using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vault.Bridge;

/// <summary>
/// Eklenti ↔ native host ↔ masaüstü uygulaması arasındaki mesajlar.
/// <see cref="BridgeRequest.Payload"/> ve <see cref="BridgeResponse.Payload"/> JSON metni olarak taşınır;
/// böylece imza (HMAC) iki tarafta da birebir aynı bayt dizisi üzerinden hesaplanır.
/// </summary>
public sealed class BridgeRequest
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ClientId { get; set; }
    public string? Nonce { get; set; }
    /// <summary>Unix zamanı (saniye).</summary>
    public long? Ts { get; set; }
    public string? Payload { get; set; }
    public string? Mac { get; set; }
}

public sealed class BridgeResponse
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public BridgeError? Error { get; set; }
    public string? Payload { get; set; }
    public string? Mac { get; set; }

    public static BridgeResponse Failure(string id, string code, string message) =>
        new() { Id = id, Ok = false, Error = new BridgeError { Code = code, Message = message } };
}

public sealed class BridgeError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public static class RequestTypes
{
    public const string Ping = "ping";
    public const string Associate = "associate";
    public const string TestAssociate = "test-associate";
    public const string GetLogins = "get-logins";
    public const string GetCredentials = "get-credentials";
    public const string GetTotp = "get-totp";
    public const string CheckLogin = "check-login";
    public const string SaveLogin = "save-login";
    public const string UpdatePassword = "update-password";
    public const string ShowApp = "show-app";
    /// <summary>Native host'un kendisinin yanıtladığı istek: masaüstü uygulamasını başlatır.</summary>
    public const string LaunchApp = "launch-app";
}

public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Locked = "locked";
    public const string NotFound = "not_found";
    public const string Denied = "denied";
    public const string AppNotRunning = "app_not_running";
    public const string Internal = "internal";
}

// ---- İstek/yanıt içerikleri ----

public sealed class PingResult
{
    public int Version { get; set; } = BridgeProtocol.Version;
    public bool Locked { get; set; }
}

public sealed class AssociateRequest
{
    /// <summary>Eklentinin ürettiği 32 baytlık anahtar (Base64).</summary>
    public string Key { get; set; } = "";
    public string Browser { get; set; } = "";
}

public sealed class AssociateResult
{
    public string Name { get; set; } = "";
}

public sealed class UrlRequest
{
    public string Url { get; set; } = "";
    /// <summary>Kullanıcı bir işlem başlattıysa true: kasa kilitliyse pencere öne getirilir.</summary>
    public bool Interactive { get; set; }
}

public sealed class EntryRequest
{
    public string Url { get; set; } = "";
    public string EntryId { get; set; } = "";
}

public sealed class LoginSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public bool HasPassword { get; set; }
    public bool HasTotp { get; set; }
}

public sealed class LoginList
{
    public List<LoginSummary> Logins { get; set; } = [];
}

public sealed class Credentials
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>Tarayıcıda gönderilen giriş formu: kasada var mı, parola değişmiş mi?</summary>
public sealed class LoginSubmission
{
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class LoginCheckStatus
{
    /// <summary>Aynı kullanıcı adı ve parola zaten kayıtlı.</summary>
    public const string Exists = "exists";
    /// <summary>Kullanıcı adı kayıtlı ama parola farklı.</summary>
    public const string Changed = "changed";
    /// <summary>Bu site için böyle bir hesap yok.</summary>
    public const string New = "new";
}

public sealed class LoginCheckResult
{
    public string Status { get; set; } = LoginCheckStatus.New;
    public string? EntryId { get; set; }
    public string? Title { get; set; }
}

public sealed class UpdatePasswordRequest
{
    public string Url { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class SavedEntry
{
    public string EntryId { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class TotpResult
{
    public string Code { get; set; } = "";
    public int Remaining { get; set; }
    public int Period { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(AssociateRequest))]
[JsonSerializable(typeof(AssociateResult))]
[JsonSerializable(typeof(UrlRequest))]
[JsonSerializable(typeof(EntryRequest))]
[JsonSerializable(typeof(LoginList))]
[JsonSerializable(typeof(Credentials))]
[JsonSerializable(typeof(TotpResult))]
[JsonSerializable(typeof(LoginSubmission))]
[JsonSerializable(typeof(LoginCheckResult))]
[JsonSerializable(typeof(UpdatePasswordRequest))]
[JsonSerializable(typeof(SavedEntry))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;

public static class BridgeProtocol
{
    public const int Version = 1;

    /// <summary>Tek mesaj için üst sınır (Chrome, host'tan gelen mesajları 1 MB ile sınırlar).</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    public const string NativeHostName = "com.localvault.bridge";

    /// <summary>Eklenti manifest'indeki "key" alanından türeyen sabit Chrome/Edge kimliği.</summary>
    public const string ChromeExtensionId = "cfhminffkiclobdbhggbnpapfknickdi";

    public const string FirefoxExtensionId = "localvault@local.vault";

    public static string DefaultPipeName =>
        Environment.GetEnvironmentVariable("LOCALVAULT_PIPE_NAME") is { Length: > 0 } name
            ? name
            : $"LocalVault.Bridge.{Environment.UserName}";

    public static byte[] Serialize(BridgeRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, BridgeJsonContext.Default.BridgeRequest);

    public static byte[] Serialize(BridgeResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, BridgeJsonContext.Default.BridgeResponse);

    public static BridgeRequest? TryParseRequest(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static BridgeResponse? TryParseResponse(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
