using System.Security.Cryptography;
using Vault.Core.Crypto;
using Vault.Core.Models;

namespace Vault.Core.Storage;

/// <summary>
/// Açılmış (kilidi çözülmüş) bir kasa. Türetilmiş anahtarı bellekte tutar; böylece her
/// kayıtta Argon2 yeniden çalıştırılmaz. İş bitince <see cref="Dispose"/> anahtarı sıfırlar.
/// </summary>
public sealed class VaultSession : IDisposable
{
    private byte[] _key;
    private KdfParameters _kdf;
    private byte[] _loadedFileHash;
    private bool _disposed;

    public string FilePath { get; }
    public VaultData Data { get; private set; }

    private VaultSession(string filePath, byte[] key, KdfParameters kdf, VaultData data, byte[] loadedFileHash)
    {
        FilePath = filePath;
        _key = key;
        _kdf = kdf;
        Data = data;
        _loadedFileHash = loadedFileHash;
    }

    /// <summary>Yeni ve boş bir kasa oluşturup diske yazar. Dosya zaten varsa hata verir.</summary>
    public static VaultSession Create(string filePath, string masterPassword, KdfParameters? kdf = null)
    {
        filePath = Path.GetFullPath(filePath);
        if (File.Exists(filePath))
            throw new VaultException($"Bu konumda zaten bir kasa var: {filePath}");

        kdf ??= KdfParameters.CreateDefault();
        var key = KeyDerivation.DeriveKey(masterPassword, kdf);
        var session = new VaultSession(filePath, key, kdf, new VaultData(), []);
        session.Save();
        return session;
    }

    public static VaultSession Open(string filePath, string masterPassword)
    {
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath))
            throw new VaultException($"Kasa dosyası bulunamadı: {filePath}");

        var raw = File.ReadAllBytes(filePath);
        var file = VaultJson.DeserializeFile(raw);
        var key = KeyDerivation.DeriveKey(masterPassword, file.Kdf);

        byte[]? plaintext = null;
        try
        {
            plaintext = VaultCipher.Decrypt(file, key);
            var data = VaultJson.DeserializeData(plaintext);
            return new VaultSession(filePath, key, file.Kdf, data, SHA256.HashData(raw));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// İçeriği şifreleyip atomik olarak kaydeder. Önceki sürüm "<c>.bak</c>" olarak saklanır.
    /// Dosya açıldıktan sonra dışarıdan değiştirildiyse <see cref="VaultConcurrencyException"/> fırlatır.
    /// </summary>
    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureNotModifiedExternally();

        var file = new VaultFile { Kdf = _kdf };
        var plaintext = VaultJson.SerializeData(Data);
        try
        {
            VaultCipher.Encrypt(file, _key, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var bytes = VaultJson.SerializeFile(file);
        AtomicFile.Write(FilePath, bytes);
        _loadedFileHash = SHA256.HashData(bytes);
    }

    /// <summary>Ana parolayı değiştirir (yeni tuz + yeni anahtar) ve hemen kaydeder.</summary>
    public void ChangeMasterPassword(string newMasterPassword, KdfParameters? kdf = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var newKdf = kdf ?? _kdf.WithNewSalt();
        var newKey = KeyDerivation.DeriveKey(newMasterPassword, newKdf);

        var oldKey = _key;
        var oldKdf = _kdf;
        _key = newKey;
        _kdf = newKdf;
        try
        {
            Save();
            CryptographicOperations.ZeroMemory(oldKey);
        }
        catch
        {
            _key = oldKey;
            _kdf = oldKdf;
            CryptographicOperations.ZeroMemory(newKey);
            throw;
        }
    }

    /// <summary>
    /// Dosyayı diskten yeniden okur (ör. başka bir uygulama değiştirdiğinde). Kaydedilmemiş bellek içi
    /// değişiklikler kaybolur. Ana parola başka yerde değiştirildiyse mevcut anahtar işe yaramayacağından
    /// <see cref="VaultReopenRequiredException"/> fırlatır.
    /// </summary>
    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var raw = File.ReadAllBytes(FilePath);
        var file = VaultJson.DeserializeFile(raw);
        if (!SameKdf(file.Kdf, _kdf))
            throw new VaultReopenRequiredException();

        var plaintext = VaultCipher.Decrypt(file, _key);
        try
        {
            Data = VaultJson.DeserializeData(plaintext);
            _loadedFileHash = SHA256.HashData(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static bool SameKdf(KdfParameters a, KdfParameters b) =>
        a.Algorithm == b.Algorithm && a.Salt == b.Salt && a.MemoryKb == b.MemoryKb
        && a.Iterations == b.Iterations && a.Parallelism == b.Parallelism;

    /// <summary>Verilen ana parolanın bu kasaya ait olup olmadığını denetler (ör. kilit ekranı için).</summary>
    public bool VerifyMasterPassword(string masterPassword)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidate = KeyDerivation.DeriveKey(masterPassword, _kdf);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, _key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private void EnsureNotModifiedExternally()
    {
        if (!File.Exists(FilePath))
            return;

        var current = SHA256.HashData(File.ReadAllBytes(FilePath));
        if (!CryptographicOperations.FixedTimeEquals(current, _loadedFileHash))
            throw new VaultConcurrencyException();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
