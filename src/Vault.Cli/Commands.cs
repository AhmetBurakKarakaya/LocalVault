using Vault.Core;
using Vault.Core.Generation;
using Vault.Core.Import;
using Vault.Core.Matching;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Core.Storage;

namespace Vault.Cli;

internal static class Commands
{
    private const int MinMasterPasswordLength = 8;
    private static readonly string[] EntryOptions =
        ["title", "url", "username", "password", "generate", "length", "totp", "totp-digits", "totp-period",
         "totp-algorithm", "notes", "tag", "match"];

    public static int Init(CliArgs args)
    {
        args.AllowOnly();
        var path = VaultPath(args);
        if (File.Exists(path))
            throw new VaultException($"Bu konumda zaten bir kasa var: {path}");

        var password = ReadNewMasterPassword(args);
        ConsoleUi.Info("Anahtar türetiliyor (Argon2id)...");
        using var session = VaultSession.Create(path, password);
        ConsoleUi.Success($"Kasa oluşturuldu: {session.FilePath}");
        return 0;
    }

    public static int Info(CliArgs args)
    {
        args.AllowOnly();
        var path = VaultPath(args);
        var file = VaultJson.DeserializeFile(File.ReadAllBytes(path));
        Console.WriteLine($"Dosya       : {path}");
        Console.WriteLine($"Sürüm       : {file.Version}");
        Console.WriteLine($"Şifreleme   : {file.Cipher}");
        Console.WriteLine($"KDF         : {file.Kdf.Algorithm} (bellek {file.Kdf.MemoryKb / 1024} MB, {file.Kdf.Iterations} tur, {file.Kdf.Parallelism} paralel)");
        Console.WriteLine($"Son değişim : {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}");
        return 0;
    }

    public static int List(CliArgs args)
    {
        args.AllowOnly();
        using var session = Open(args);
        var entries = session.Data.Search(args.Positionals.FirstOrDefault()).ToList();
        if (entries.Count == 0)
        {
            ConsoleUi.Info("Kayıt bulunamadı.");
            return 0;
        }

        Console.WriteLine($"{"ID",-8}  {"BAŞLIK",-24}  {"KULLANICI",-26}  {"OTP",-3}  URL");
        foreach (var e in entries)
            Console.WriteLine($"{ShortId(e),-8}  {Trim(e.Title, 24),-24}  {Trim(e.Username, 26),-26}  {(e.Totp is null ? "" : "✓"),-3}  {e.Urls.FirstOrDefault()}");
        ConsoleUi.Info($"{entries.Count} kayıt.");
        return 0;
    }

    public static int Show(CliArgs args)
    {
        args.AllowOnly("reveal");
        using var session = Open(args);
        var e = Resolve(session.Data, args.RequirePositional(0, "Kayıt (ID veya başlık)"));

        Console.WriteLine($"ID          : {e.Id}");
        Console.WriteLine($"Başlık      : {e.Title}");
        Console.WriteLine($"Kullanıcı   : {e.Username}");
        Console.WriteLine($"Parola      : {(args.Has("reveal") ? e.Password : Mask(e.Password))}");
        Console.WriteLine($"URL'ler     : {string.Join(", ", e.Urls)}");
        Console.WriteLine($"Eşleşme     : {e.MatchMode}");
        if (e.Totp is not null)
        {
            var code = OtpGenerator.ComputeTotp(e.Totp);
            Console.WriteLine($"TOTP        : {code.Code} ({code.RemainingSeconds} sn) [{e.Totp.Algorithm}, {e.Totp.Digits} hane, {e.Totp.Period} sn]");
        }
        if (e.Tags.Count > 0)
            Console.WriteLine($"Etiketler   : {string.Join(", ", e.Tags)}");
        if (!string.IsNullOrEmpty(e.Notes))
            Console.WriteLine($"Notlar      : {e.Notes}");
        Console.WriteLine($"Güncelleme  : {e.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        return 0;
    }

    public static int Add(CliArgs args)
    {
        args.AllowOnly(EntryOptions);
        using var session = Open(args);

        var entry = new VaultEntry();
        ApplyEntryOptions(args, entry, isNew: true);
        if (string.IsNullOrWhiteSpace(entry.Title))
            throw new UsageException("--title belirtilmeli (veya başlığı otpauth adresindeki issuer'dan alınabilmeli).");

        session.Data.Entries.Add(entry);
        session.Save();
        ConsoleUi.Success($"Eklendi: {entry.Title} ({ShortId(entry)})");
        return 0;
    }

    public static int Edit(CliArgs args)
    {
        args.AllowOnly([.. EntryOptions, "clear-totp"]);
        using var session = Open(args);
        var entry = Resolve(session.Data, args.RequirePositional(0, "Kayıt (ID veya başlık)"));

        ApplyEntryOptions(args, entry, isNew: false);
        if (args.Has("clear-totp"))
            entry.Totp = null;
        entry.Touch();

        session.Save();
        ConsoleUi.Success($"Güncellendi: {entry.Title} ({ShortId(entry)})");
        return 0;
    }

    public static int Remove(CliArgs args)
    {
        args.AllowOnly("yes");
        using var session = Open(args);
        var entry = Resolve(session.Data, args.RequirePositional(0, "Kayıt (ID veya başlık)"));

        if (!args.Has("yes") && !ConsoleUi.Confirm($"'{entry.Title}' silinsin mi?"))
        {
            ConsoleUi.Info("İptal edildi.");
            return 1;
        }

        session.Data.Remove(entry.Id);
        session.Save();
        ConsoleUi.Success($"Silindi: {entry.Title}");
        return 0;
    }

    public static int Totp(CliArgs args)
    {
        args.AllowOnly("watch");
        using var session = Open(args);
        var entry = Resolve(session.Data, args.RequirePositional(0, "Kayıt (ID veya başlık)"));
        var settings = entry.Totp ?? throw new VaultException($"'{entry.Title}' kaydında TOTP tanımlı değil.");

        if (!args.Has("watch") || Console.IsOutputRedirected)
        {
            var code = OtpGenerator.ComputeTotp(settings);
            Console.WriteLine(code.Code);
            ConsoleUi.Info($"{code.RemainingSeconds} sn geçerli.");
            return 0;
        }

        ConsoleUi.Info($"{entry.Title} — çıkmak için bir tuşa basın.");
        while (Console.IsInputRedirected || !Console.KeyAvailable)
        {
            var code = OtpGenerator.ComputeTotp(settings);
            var bar = new string('█', code.RemainingSeconds * 20 / code.Period).PadRight(20, '░');
            Console.Write($"\r{code.Code}  {bar} {code.RemainingSeconds,2} sn ");
            Thread.Sleep(250);
        }
        Console.ReadKey(intercept: true);
        Console.WriteLine();
        return 0;
    }

    public static int Match(CliArgs args)
    {
        args.AllowOnly();
        var url = args.RequirePositional(0, "Sayfa adresi");
        using var session = Open(args);

        var matches = UrlMatcher.FindMatches(session.Data.Entries, url);
        if (matches.Count == 0)
        {
            ConsoleUi.Info("Bu adres için eşleşen kayıt yok.");
            return 1;
        }

        foreach (var m in matches)
            Console.WriteLine($"{ShortId(m.Entry),-8}  {m.Quality,-10}  {Trim(m.Entry.Title, 24),-24}  {m.Entry.Username}  ({m.MatchedUrl})");
        return 0;
    }

    public static int Generate(CliArgs args)
    {
        args.AllowOnly("length", "no-symbols", "no-digits", "no-upper", "no-lower", "exclude-ambiguous");
        var options = PasswordOptionsFrom(args);
        Console.WriteLine(PasswordGenerator.Generate(options));
        ConsoleUi.Info($"~{PasswordGenerator.EstimateEntropyBits(options):0} bit entropi");
        return 0;
    }

    public static int ChangeMasterPassword(CliArgs args)
    {
        args.AllowOnly();
        using var session = Open(args);
        var newPassword = ReadNewMasterPassword(args);
        ConsoleUi.Info("Yeni anahtar türetiliyor...");
        session.ChangeMasterPassword(newPassword);
        ConsoleUi.Success("Ana parola değiştirildi.");
        return 0;
    }

    public static int Export(CliArgs args)
    {
        args.AllowOnly("yes", "force");
        var target = Path.GetFullPath(args.RequirePositional(0, "Hedef dosya"));
        if (File.Exists(target) && !args.Has("force"))
            throw new UsageException($"Dosya zaten var: {target} (üzerine yazmak için --force).");

        using var session = Open(args);
        ConsoleUi.Warning("UYARI: Dışa aktarılan dosya TÜM parolaları ve OTP anahtarlarını ŞİFRESİZ içerir.");
        if (!args.Has("yes") && !ConsoleUi.Confirm("Devam edilsin mi?"))
        {
            ConsoleUi.Info("İptal edildi.");
            return 1;
        }

        File.WriteAllText(target, VaultJson.ToPlainJson(session.Data));
        ConsoleUi.Success($"{session.Data.Entries.Count} kayıt dışa aktarıldı: {target}");
        ConsoleUi.Warning("İşiniz bitince bu dosyayı güvenli şekilde silin.");
        return 0;
    }

    public static int Import(CliArgs args)
    {
        args.AllowOnly();
        var source = args.RequirePositional(0, "Kaynak dosya");
        var imported = VaultJson.FromPlainJson(File.ReadAllText(source));
        using var session = Open(args);

        int added = 0, updated = 0, skipped = 0;
        foreach (var entry in imported.Entries)
        {
            if (entry.Totp is not null)
            {
                try
                {
                    entry.Totp.Validate();
                }
                catch (ArgumentException ex)
                {
                    ConsoleUi.Warning($"'{entry.Title}': TOTP geçersiz, yine de içe aktarıldı ({ex.Message})");
                }
            }

            var existing = session.Data.FindById(entry.Id);
            if (existing is null)
            {
                session.Data.Entries.Add(entry);
                added++;
            }
            else if (entry.UpdatedAt > existing.UpdatedAt)
            {
                session.Data.Entries[session.Data.Entries.IndexOf(existing)] = entry;
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        session.Save();
        ConsoleUi.Success($"İçe aktarıldı: {added} yeni, {updated} güncellendi, {skipped} atlandı (daha yeni sürüm kasada).");
        return 0;
    }

    public static int ImportCsv(CliArgs args)
    {
        args.AllowOnly("format", "yes");
        var source = args.RequirePositional(0, "CSV dosyası");
        var format = args.Get("format")?.ToLowerInvariant() switch
        {
            null or "auto" => CsvFormat.Auto,
            "keepassxc" => CsvFormat.KeePassXC,
            "bitwarden" => CsvFormat.Bitwarden,
            "1password" or "onepassword" => CsvFormat.OnePassword,
            "generic" or "genel" => CsvFormat.Generic,
            _ => throw new UsageException("--format şunlardan biri olmalı: auto, keepassxc, bitwarden, 1password, generic."),
        };

        var result = CsvImporter.Import(File.ReadAllText(source), format);
        using var session = Open(args);
        var (fresh, duplicates) = CsvImporter.SplitDuplicates(session.Data, result.Entries);

        Console.WriteLine($"Biçim       : {CsvImporter.FormatName(result.Format)}");
        Console.WriteLine($"Yeni kayıt  : {fresh.Count}");
        Console.WriteLine($"Yineleme    : {duplicates.Count} (atlanacak)");
        Console.WriteLine($"Atlanan satır: {result.SkippedRows} (giriş kaydı değil)");
        foreach (var warning in result.Warnings)
            ConsoleUi.Warning($"  • {warning}");

        if (fresh.Count == 0)
        {
            ConsoleUi.Info("İçe aktarılacak yeni kayıt yok.");
            return 0;
        }
        if (!args.Has("yes") && !ConsoleUi.Confirm($"{fresh.Count} kayıt içe aktarılsın mı?"))
        {
            ConsoleUi.Info("İptal edildi.");
            return 1;
        }

        session.Data.Entries.AddRange(fresh);
        session.Save();
        ConsoleUi.Success($"{fresh.Count} kayıt içe aktarıldı.");
        ConsoleUi.Warning("CSV dosyası parolaları şifresiz içerir; işiniz bittiyse silin.");
        return 0;
    }

    // ---- yardımcılar ----

    private static string VaultPath(CliArgs args) => Path.GetFullPath(args.Get("vault") ?? VaultPaths.DefaultVaultPath);

    private static VaultSession Open(CliArgs args)
    {
        var path = VaultPath(args);
        if (!File.Exists(path))
            throw new VaultException($"Kasa bulunamadı: {path}\nÖnce 'localvault-cli init' ile oluşturun.");

        var password = args.Has("password-stdin")
            ? ConsoleUi.ReadStdinLine("ana parola")
            : ConsoleUi.ReadSecret("Ana parola: ");
        return VaultSession.Open(path, password);
    }

    private static string ReadNewMasterPassword(CliArgs args)
    {
        string password;
        if (args.Has("password-stdin"))
        {
            password = ConsoleUi.ReadStdinLine("yeni ana parola");
        }
        else
        {
            password = ConsoleUi.ReadSecret("Yeni ana parola: ");
            if (ConsoleUi.ReadSecret("Yeni ana parola (tekrar): ") != password)
                throw new UsageException("Parolalar eşleşmiyor.");
        }

        if (password.Length < MinMasterPasswordLength)
            throw new UsageException($"Ana parola en az {MinMasterPasswordLength} karakter olmalı.");
        if (password.Length < 12)
            ConsoleUi.Warning("Öneri: ana parola için en az 12 karakter veya birkaç kelimelik bir cümle kullanın.");
        return password;
    }

    private static void ApplyEntryOptions(CliArgs args, VaultEntry entry, bool isNew)
    {
        if (args.Get("title") is { } title) entry.Title = title.Trim();
        if (args.Get("username") is { } username) entry.Username = username;
        if (args.Get("notes") is { } notes) entry.Notes = notes;
        if (args.HasOption("url")) entry.Urls = [.. args.GetAll("url").Select(u => u.Trim())];
        if (args.HasOption("tag")) entry.Tags = [.. args.GetAll("tag").Select(t => t.Trim())];

        if (args.Get("match") is { } match)
            entry.MatchMode = Enum.TryParse<UrlMatchMode>(match, ignoreCase: true, out var mode)
                ? mode
                : throw new UsageException("--match şunlardan biri olmalı: domain, host, startswith, exact, never.");

        foreach (var url in entry.Urls)
            if (!UrlMatcher.TryParse(url, out _))
                throw new UsageException($"Geçersiz URL: {url}");

        // Parola: --password > --generate > (yeni kayıtta) etkileşimli sorma
        if (args.Get("password") is { } password)
        {
            entry.Password = password;
            ConsoleUi.Warning("Not: --password ile verilen parola komut geçmişinde kalabilir.");
        }
        else if (args.Has("generate"))
        {
            entry.Password = PasswordGenerator.Generate(PasswordOptionsFrom(args));
            ConsoleUi.Info("Rastgele parola üretildi ('show --reveal' ile görebilirsiniz).");
        }
        else if (isNew && !Console.IsInputRedirected)
        {
            entry.Password = ConsoleUi.ReadSecret("Hesap parolası (boş bırakılabilir): ");
        }

        if (args.Get("totp") is { } totp)
            entry.Totp = ParseTotp(args, totp, entry);
    }

    private static TotpSettings ParseTotp(CliArgs args, string value, VaultEntry entry)
    {
        if (OtpAuthUri.IsOtpAuthUri(value))
        {
            var info = OtpAuthUri.Parse(value);
            if (string.IsNullOrWhiteSpace(entry.Title) && info.Issuer is not null)
                entry.Title = info.Issuer;
            if (string.IsNullOrWhiteSpace(entry.Username) && info.Account is not null)
                entry.Username = info.Account;
            return info.Settings;
        }

        var settings = new TotpSettings { Secret = OtpAuthUri.NormalizeSecret(value) };
        if (args.GetInt("totp-digits") is { } digits) settings.Digits = digits;
        if (args.GetInt("totp-period") is { } period) settings.Period = period;
        if (args.Get("totp-algorithm") is { } alg)
            settings.Algorithm = Enum.TryParse<OtpHashAlgorithm>(alg, ignoreCase: true, out var a)
                ? a
                : throw new UsageException("--totp-algorithm şunlardan biri olmalı: SHA1, SHA256, SHA512.");

        settings.Validate();
        return settings;
    }

    private static PasswordOptions PasswordOptionsFrom(CliArgs args) => new()
    {
        Length = args.GetInt("length") ?? 20,
        Symbols = !args.Has("no-symbols"),
        Digits = !args.Has("no-digits"),
        Uppercase = !args.Has("no-upper"),
        Lowercase = !args.Has("no-lower"),
        ExcludeAmbiguous = args.Has("exclude-ambiguous"),
    };

    /// <summary>Kaydı tam ID, ID öneki (en az 4 karakter), tam başlık veya benzersiz başlık parçasıyla bulur.</summary>
    private static VaultEntry Resolve(VaultData data, string key)
    {
        if (Guid.TryParse(key, out var id))
            return data.FindById(id) ?? throw new VaultException($"Kayıt bulunamadı: {key}");

        if (key.Length >= 4 && key.All(Uri.IsHexDigit))
        {
            var byPrefix = data.Entries.Where(e => e.Id.ToString("N").StartsWith(key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byPrefix.Count == 1)
                return byPrefix[0];
        }

        var exact = data.Entries.Where(e => e.Title.Equals(key, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0];

        var candidates = exact.Count > 1 ? exact : data.Search(key).ToList();
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new VaultException($"Kayıt bulunamadı: {key}"),
            _ => throw new VaultException(
                $"'{key}' birden fazla kayıtla eşleşti; ID ile belirtin:\n" +
                string.Join("\n", candidates.Select(c => $"  {ShortId(c)}  {c.Title}  {c.Username}"))),
        };
    }

    private static string ShortId(VaultEntry e) => e.Id.ToString("N")[..8];

    private static string Mask(string password) => password.Length == 0 ? "(boş)" : new string('•', 8);

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
