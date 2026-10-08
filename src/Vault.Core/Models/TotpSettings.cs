using Vault.Core.Otp;

namespace Vault.Core.Models;

public sealed class TotpSettings
{
    public const int MinDigits = 6;
    public const int MaxDigits = 10;
    public const int MinPeriod = 1;
    public const int MaxPeriod = 300;

    /// <summary>Base32 kodlu paylaşılan gizli anahtar.</summary>
    public string Secret { get; set; } = "";
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public OtpHashAlgorithm Algorithm { get; set; } = OtpHashAlgorithm.SHA1;

    /// <summary>Ayarları doğrular; geçersizse <see cref="ArgumentException"/> fırlatır.</summary>
    public void Validate()
    {
        if (!Base32.TryDecode(Secret, out var key) || key.Length == 0)
            throw new ArgumentException("TOTP gizli anahtarı geçerli bir Base32 değeri değil.");
        if (Digits is < MinDigits or > MaxDigits)
            throw new ArgumentException($"TOTP hane sayısı {MinDigits}-{MaxDigits} arasında olmalı.");
        if (Period is < MinPeriod or > MaxPeriod)
            throw new ArgumentException($"TOTP periyodu {MinPeriod}-{MaxPeriod} saniye arasında olmalı.");
        if (!Enum.IsDefined(Algorithm))
            throw new ArgumentException("Desteklenmeyen TOTP algoritması.");
    }
}
