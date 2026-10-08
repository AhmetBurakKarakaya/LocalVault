using Vault.Core.Models;

namespace Vault.Core.Otp;

/// <summary>QR kodlarından okunan metinleri TOTP hesaplarına çevirir (otpauth:// ve otpauth-migration://).</summary>
public static class OtpImport
{
    public sealed record Result(IReadOnlyList<OtpAuthInfo> Accounts, IReadOnlyList<string> Warnings);

    public static Result FromQrTexts(IEnumerable<string> texts)
    {
        var accounts = new List<OtpAuthInfo>();
        var warnings = new List<string>();
        foreach (var text in texts)
        {
            try
            {
                if (OtpAuthUri.IsOtpAuthUri(text))
                {
                    accounts.Add(OtpAuthUri.Parse(text));
                }
                else if (OtpMigration.IsMigrationUri(text))
                {
                    var migration = OtpMigration.Parse(text);
                    accounts.AddRange(migration.Accounts);
                    warnings.AddRange(migration.Warnings);
                }
                else
                {
                    warnings.Add("Bulunan QR kodu bir doğrulama (2FA) kodu değil.");
                }
            }
            catch (FormatException ex)
            {
                warnings.Add($"QR kodu okunamadı: {ex.Message}");
            }
        }
        return new Result(accounts, warnings);
    }

    /// <summary>QR'dan gelen hesabı yeni bir kasa kaydına dönüştürür.</summary>
    public static VaultEntry ToEntry(OtpAuthInfo account) => new()
    {
        Title = account.Issuer ?? account.Account ?? "Doğrulama kodu",
        Username = account.Account ?? "",
        Totp = account.Settings,
    };
}
