using System.Security.Cryptography;

namespace Vault.Core.Models;

/// <summary>
/// Diskteki vault.json dosyasının dış zarfı. Yalnızca <see cref="Data"/> şifrelidir;
/// diğer alanlar şifre çözme için gereken parametrelerdir ve AES-GCM'de ek doğrulanmış
/// veri (AAD) olarak kullanıldığından değiştirilirlerse şifre çözme başarısız olur.
/// </summary>
public sealed class VaultFile
{
    public const string FormatName = "localvault";
    public const int CurrentVersion = 1;
    public const string CipherName = "aes-256-gcm";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public KdfParameters Kdf { get; set; } = new();
    public string Cipher { get; set; } = CipherName;
    public string Nonce { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Data { get; set; } = "";
}

public sealed class KdfParameters
{
    public const string Argon2id = "argon2id";

    public string Algorithm { get; set; } = Argon2id;
    public string Salt { get; set; } = "";
    public int MemoryKb { get; set; } = 64 * 1024;
    public int Iterations { get; set; } = 3;
    public int Parallelism { get; set; } = 4;

    /// <summary>Yeni kasa için önerilen parametreler (64 MB, 3 tur, 4 iş parçacığı) ve rastgele tuz.</summary>
    public static KdfParameters CreateDefault() => new() { Salt = NewSalt() };

    public KdfParameters WithNewSalt() => new()
    {
        Algorithm = Algorithm,
        Salt = NewSalt(),
        MemoryKb = MemoryKb,
        Iterations = Iterations,
        Parallelism = Parallelism,
    };

    private static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}
