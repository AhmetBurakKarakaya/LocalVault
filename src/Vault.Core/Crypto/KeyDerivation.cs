using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Vault.Core.Models;

namespace Vault.Core.Crypto;

/// <summary>Ana paroladan Argon2id ile 256 bit AES anahtarı türetir.</summary>
public static class KeyDerivation
{
    public const int KeySize = 32;
    public const int MinSaltBytes = 16;

    // Bozuk/kötü niyetli bir dosyanın aşırı bellek veya süre harcatmasını engelleyen üst sınırlar.
    private const int MaxMemoryKb = 4 * 1024 * 1024;
    private const int MaxIterations = 100;
    private const int MaxParallelism = 64;

    public static byte[] DeriveKey(string masterPassword, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        var salt = Validate(parameters);

        // Aynı parolanın farklı Unicode gösterimleri (ör. "ş" birleşik/ayrık) aynı anahtarı üretsin.
        var passwordBytes = Encoding.UTF8.GetBytes(masterPassword.Normalize(NormalizationForm.FormC));
        try
        {
            using var argon = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKb,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };
            return argon.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Parametreleri doğrular ve çözülmüş tuzu döner.</summary>
    public static byte[] Validate(KdfParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);

        if (p.Algorithm != KdfParameters.Argon2id)
            throw new VaultFormatException($"Desteklenmeyen anahtar türetme algoritması: '{p.Algorithm}'.");

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(p.Salt);
        }
        catch (FormatException ex)
        {
            throw new VaultFormatException("KDF tuzu geçerli Base64 değil.", ex);
        }

        if (salt.Length < MinSaltBytes)
            throw new VaultFormatException($"KDF tuzu en az {MinSaltBytes} bayt olmalı.");
        if (p.Parallelism is < 1 or > MaxParallelism)
            throw new VaultFormatException("KDF paralellik değeri geçersiz.");
        if (p.MemoryKb < 8 * p.Parallelism || p.MemoryKb > MaxMemoryKb)
            throw new VaultFormatException("KDF bellek değeri geçersiz.");
        if (p.Iterations is < 1 or > MaxIterations)
            throw new VaultFormatException("KDF tur sayısı geçersiz.");

        return salt;
    }
}
