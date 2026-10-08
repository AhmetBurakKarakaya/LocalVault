using System.Globalization;
using Vault.Core.Models;

namespace Vault.Core.Otp;

/// <summary>Google Authenticator / QR kodlarında kullanılan otpauth:// URI'si.</summary>
public sealed record OtpAuthInfo(TotpSettings Settings, string? Issuer, string? Account);

public static class OtpAuthUri
{
    private const string Scheme = "otpauth";

    public static bool IsOtpAuthUri(string value) =>
        value.TrimStart().StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>otpauth://totp/Issuer:hesap?secret=...&amp;issuer=...&amp;algorithm=SHA1&amp;digits=6&amp;period=30</summary>
    public static OtpAuthInfo Parse(string uri)
    {
        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Geçerli bir otpauth:// adresi değil.");

        var type = parsed.Host.ToLowerInvariant();
        if (type == "hotp")
            throw new FormatException("Sayaç tabanlı HOTP henüz desteklenmiyor; yalnızca TOTP.");
        if (type != "totp")
            throw new FormatException($"Bilinmeyen OTP türü: '{parsed.Host}'.");

        var query = ParseQuery(parsed.Query);
        if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            throw new FormatException("otpauth adresinde 'secret' parametresi yok.");

        var settings = new TotpSettings { Secret = NormalizeSecret(secret) };

        if (query.TryGetValue("algorithm", out var alg))
        {
            settings.Algorithm = alg.ToUpperInvariant() switch
            {
                "SHA1" => OtpHashAlgorithm.SHA1,
                "SHA256" => OtpHashAlgorithm.SHA256,
                "SHA512" => OtpHashAlgorithm.SHA512,
                _ => throw new FormatException($"Desteklenmeyen algoritma: '{alg}'."),
            };
        }
        if (query.TryGetValue("digits", out var digits))
            settings.Digits = ParseInt(digits, "digits");
        if (query.TryGetValue("period", out var period))
            settings.Period = ParseInt(period, "period");

        try
        {
            settings.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(ex.Message, ex);
        }

        // Etiket: "Issuer:hesap" veya yalnızca "hesap"
        var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
        string? labelIssuer = null, account = label;
        var colon = label.IndexOf(':');
        if (colon >= 0)
        {
            labelIssuer = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }

        query.TryGetValue("issuer", out var issuer);
        issuer = string.IsNullOrWhiteSpace(issuer) ? labelIssuer : issuer;

        return new OtpAuthInfo(settings, NullIfEmpty(issuer), NullIfEmpty(account));
    }

    public static string Build(TotpSettings settings, string? issuer, string? account)
    {
        settings.Validate();

        var label = string.IsNullOrEmpty(issuer)
            ? Uri.EscapeDataString(account ?? "")
            : $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account ?? "")}";

        var uri = $"otpauth://totp/{label}?secret={NormalizeSecret(settings.Secret)}";
        if (!string.IsNullOrEmpty(issuer))
            uri += $"&issuer={Uri.EscapeDataString(issuer)}";
        return uri + $"&algorithm={settings.Algorithm}&digits={settings.Digits}&period={settings.Period}";
    }

    /// <summary>Gizli anahtarı boşluksuz, büyük harfli ve dolgusuz Base32 hâline getirir.</summary>
    public static string NormalizeSecret(string secret) =>
        new string(secret.Where(c => c is not (' ' or '-' or '\t' or '=')).ToArray()).ToUpperInvariant();

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }
        return result;
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new FormatException($"'{name}' parametresi sayı olmalı.");

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
