namespace Vault.Core.Generation;

public enum StrengthLevel
{
    VeryWeak,
    Weak,
    Fair,
    Strong,
    VeryStrong,
}

public readonly record struct StrengthEstimate(double Bits, StrengthLevel Level);

/// <summary>
/// Kaba parola gücü tahmini: karakter havuzu × etkin uzunluk. Sözlük saldırılarını tam modellemez;
/// yalnızca kullanıcıya yön göstermek içindir.
/// </summary>
public static class PasswordStrength
{
    private static readonly string[] CommonPasswords =
    [
        "password", "parola", "sifre", "şifre", "123456", "12345678", "123456789", "qwerty", "abc123",
        "111111", "iloveyou", "admin", "letmein", "welcome", "galatasaray", "fenerbahce", "besiktas",
    ];

    public static StrengthEstimate Estimate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return new StrengthEstimate(0, StrengthLevel.VeryWeak);

        var lower = password.ToLowerInvariant();
        if (CommonPasswords.Any(c => lower.Contains(c, StringComparison.Ordinal)) && password.Length < 16)
            return new StrengthEstimate(0, StrengthLevel.VeryWeak);

        int pool = 0;
        if (password.Any(char.IsAsciiLetterLower)) pool += 26;
        if (password.Any(char.IsAsciiLetterUpper)) pool += 26;
        if (password.Any(char.IsAsciiDigit)) pool += 10;
        if (password.Any(c => c < 128 && !char.IsAsciiLetterOrDigit(c))) pool += 33;
        if (password.Any(c => c >= 128)) pool += 64;   // ç, ğ, ı, ö, ş, ü vb.

        // Ardışık tekrarlar ("aaaa") ve düz diziler ("1234", "abcd") az bilgi taşır.
        var effectiveLength = 1.0;
        for (var i = 1; i < password.Length; i++)
        {
            var delta = password[i] - password[i - 1];
            effectiveLength += delta is 0 or 1 or -1 ? 0.25 : 1;
        }

        var bits = effectiveLength * Math.Log2(Math.Max(pool, 2));
        var level = bits switch
        {
            < 28 => StrengthLevel.VeryWeak,
            < 40 => StrengthLevel.Weak,
            < 60 => StrengthLevel.Fair,
            < 80 => StrengthLevel.Strong,
            _ => StrengthLevel.VeryStrong,
        };
        return new StrengthEstimate(bits, level);
    }
}
