using Vault.Core.Matching;
using Vault.Core.Models;
using Vault.Core.Otp;

namespace Vault.Core.Import;

public enum CsvFormat
{
    Auto,
    KeePassXC,
    Bitwarden,
    OnePassword,
    Generic,
}

public sealed record ImportResult(CsvFormat Format, List<VaultEntry> Entries, List<string> Warnings, int SkippedRows);

/// <summary>
/// Başka parola yöneticilerinin CSV dışa aktarımlarını kayıtlara dönüştürür.
/// KeePassXC: Group, Title, Username, Password, URL, Notes, TOTP …
/// Bitwarden: folder, favorite, type, name, notes, fields, reprompt, login_uri, login_username, login_password, login_totp
/// 1Password: Title, Url, Username, Password, OTPAuth, Favorite, Archived, Tags, Notes
/// Diğerleri: başlık/ad, kullanıcı adı, parola, URL sütunlarını içeren genel CSV.
/// </summary>
public static class CsvImporter
{
    public static string FormatName(CsvFormat format) => format switch
    {
        CsvFormat.KeePassXC => "KeePassXC",
        CsvFormat.Bitwarden => "Bitwarden",
        CsvFormat.OnePassword => "1Password",
        CsvFormat.Generic => "Genel CSV",
        _ => "Otomatik",
    };

    public static ImportResult Import(string csv, CsvFormat format = CsvFormat.Auto)
    {
        var rows = CsvReader.Parse(csv);
        if (rows.Count == 0)
            throw new FormatException("CSV dosyası boş.");

        var header = rows[0].Select(Normalize).ToArray();
        if (format == CsvFormat.Auto)
            format = Detect(header);

        var columns = format switch
        {
            CsvFormat.Bitwarden => new Columns(header, ["name"], ["loginusername"], ["loginpassword"], ["loginuri"], ["notes"], ["logintotp"], ["folder"], ["type"]),
            CsvFormat.KeePassXC => new Columns(header, ["title"], ["username"], ["password"], ["url"], ["notes"], ["totp"], ["group"], []),
            CsvFormat.OnePassword => new Columns(header, ["title"], ["username"], ["password"], ["url", "website", "urls"], ["notes", "notesplain"],
                ["otpauth", "onetimepassword"], ["tags"], ["type", "category"]),
            _ => new Columns(header, ["title", "name", "account", "site", "hesap", "başlık", "baslik"],
                ["username", "user", "login", "email", "loginname", "kullanıcıadı", "kullaniciadi"],
                ["password", "pass", "parola", "şifre", "sifre"], ["url", "website", "uri", "loginuri", "adres"],
                ["notes", "note", "notlar", "extra"], ["totp", "otp", "otpauth", "logintotp"], ["group", "folder", "grouping", "tags"], []),
        };
        if (columns.Password < 0 && columns.Username < 0)
            throw new FormatException("CSV'de kullanıcı adı veya parola sütunu bulunamadı. Başlık satırı: " + string.Join(", ", rows[0]));

        var entries = new List<VaultEntry>();
        var warnings = new List<string>();
        var skipped = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string Get(int index) => index >= 0 && index < row.Length ? row[index].Trim() : "";

            // Bitwarden/1Password: yalnızca giriş kayıtları (kart, kimlik, not değil)
            var type = Get(columns.Type).ToLowerInvariant();
            if (type.Length > 0 && type is not ("login" or "password" or "giriş"))
            {
                skipped++;
                continue;
            }

            var entry = new VaultEntry
            {
                Title = Get(columns.Title),
                Username = Get(columns.Username),
                Password = columns.Password >= 0 && columns.Password < row.Length ? row[columns.Password] : "",
                Notes = columns.Notes >= 0 && columns.Notes < row.Length ? row[columns.Notes] : "",
            };

            foreach (var url in SplitList(Get(columns.Url)))
            {
                if (UrlMatcher.TryParse(url, out _))
                    entry.Urls.Add(url);
            }

            var group = Get(columns.Group);
            foreach (var tag in format is CsvFormat.OnePassword or CsvFormat.Generic && !group.Contains('/')
                         ? SplitList(group)
                         : [GroupTag(group)])
            {
                if (tag.Length > 0 && !entry.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase))
                    entry.Tags.Add(tag);
            }

            if (string.IsNullOrWhiteSpace(entry.Title))
                entry.Title = entry.Urls.Count > 0 && UrlMatcher.TryParse(entry.Urls[0], out var uri) ? uri.Host : entry.Username;
            if (string.IsNullOrWhiteSpace(entry.Title) && entry.Password.Length == 0)
            {
                skipped++;
                continue;
            }

            var totp = Get(columns.Totp);
            if (totp.Length > 0)
                entry.Totp = ParseTotp(totp, entry.Title, warnings);

            entries.Add(entry);
        }

        return new ImportResult(format, entries, warnings, skipped);
    }

    /// <summary>Kasada aynı başlık + kullanıcı adı + host'a sahip kayıt varsa onu yineleme sayar.</summary>
    public static (List<VaultEntry> New, List<VaultEntry> Duplicates) SplitDuplicates(VaultData vault, IEnumerable<VaultEntry> imported)
    {
        var keys = vault.Entries.Select(Key).ToHashSet();
        var fresh = new List<VaultEntry>();
        var duplicates = new List<VaultEntry>();
        foreach (var entry in imported)
        {
            if (keys.Add(Key(entry)))
                fresh.Add(entry);
            else
                duplicates.Add(entry);
        }
        return (fresh, duplicates);

        static string Key(VaultEntry e)
        {
            var host = e.Urls.Count > 0 && UrlMatcher.TryParse(e.Urls[0], out var uri) ? uri.IdnHost.ToLowerInvariant() : "";
            return $"{e.Title.Trim().ToUpperInvariant()}\n{e.Username.Trim().ToUpperInvariant()}\n{host}";
        }
    }

    private static CsvFormat Detect(string[] header)
    {
        bool Has(string name) => header.Contains(name);
        if (Has("loginusername") && Has("loginpassword"))
            return CsvFormat.Bitwarden;
        if (Has("group") && Has("title") && Has("username") && Has("password"))
            return CsvFormat.KeePassXC;
        if (Has("title") && Has("password") && (Has("otpauth") || Has("archived") || Has("favorite")))
            return CsvFormat.OnePassword;
        return CsvFormat.Generic;
    }

    private static TotpSettings? ParseTotp(string value, string title, List<string> warnings)
    {
        try
        {
            if (OtpAuthUri.IsOtpAuthUri(value))
                return OtpAuthUri.Parse(value).Settings;
            if (value.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{title}: Steam doğrulama kodları desteklenmiyor; TOTP aktarılmadı.");
                return null;
            }
            var settings = new TotpSettings { Secret = OtpAuthUri.NormalizeSecret(value) };
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            warnings.Add($"{title}: TOTP okunamadı ({ex.Message}); kayıt TOTP'siz aktarıldı.");
            return null;
        }
    }

    /// <summary>KeePassXC grup yolu "Root/İş/Banka" → "İş/Banka".</summary>
    private static string GroupTag(string group)
    {
        var parts = group.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count > 0 && parts[0] is "Root" or "Kök" or "Passwords" or "Parolalar")
            parts.RemoveAt(0);
        return string.Join("/", parts);
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>"Login Username" → "loginusername", "One-time password" → "onetimepassword"</summary>
    private static string Normalize(string header) =>
        new string(header.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());

    private sealed class Columns(string[] header, string[] title, string[] username, string[] password, string[] url,
        string[] notes, string[] totp, string[] group, string[] type)
    {
        public int Title { get; } = Find(header, title);
        public int Username { get; } = Find(header, username);
        public int Password { get; } = Find(header, password);
        public int Url { get; } = Find(header, url);
        public int Notes { get; } = Find(header, notes);
        public int Totp { get; } = Find(header, totp);
        public int Group { get; } = Find(header, group);
        public int Type { get; } = Find(header, type);

        private static int Find(string[] header, string[] names)
        {
            foreach (var name in names)
            {
                var index = Array.IndexOf(header, name);
                if (index >= 0)
                    return index;
            }
            return -1;
        }
    }
}
