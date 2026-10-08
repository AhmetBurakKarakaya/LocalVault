using System.Security.Cryptography;
using System.Text;
using Vault.Core.Models;

namespace Vault.Core.Crypto;

/// <summary>Kasa içeriğini AES-256-GCM ile şifreler/çözer.</summary>
internal static class VaultCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary><paramref name="plaintext"/>'i şifreler ve sonucu <paramref name="file"/> içine yazar.</summary>
    public static void Encrypt(VaultFile file, byte[] key, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAssociatedData(file));

        file.Nonce = Convert.ToBase64String(nonce);
        file.Tag = Convert.ToBase64String(tag);
        file.Data = Convert.ToBase64String(ciphertext);
    }

    /// <summary>Şifreyi çözer. Çağıran, dönen diziyi kullandıktan sonra sıfırlamalıdır.</summary>
    public static byte[] Decrypt(VaultFile file, byte[] key)
    {
        if (file.Cipher != VaultFile.CipherName)
            throw new VaultFormatException($"Desteklenmeyen şifreleme: '{file.Cipher}'.");

        byte[] nonce, tag, ciphertext;
        try
        {
            nonce = Convert.FromBase64String(file.Nonce);
            tag = Convert.FromBase64String(file.Tag);
            ciphertext = Convert.FromBase64String(file.Data);
        }
        catch (FormatException ex)
        {
            throw new VaultFormatException("Kasa dosyasındaki şifreli veri okunamadı.", ex);
        }

        if (nonce.Length != NonceSize || tag.Length != TagSize)
            throw new VaultFormatException("Kasa dosyasındaki nonce/tag uzunluğu geçersiz.");

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAssociatedData(file));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidMasterPasswordException();
        }
    }

    // Başlık alanları şifreli veriye bağlanır: KDF parametreleri vb. değiştirilirse doğrulama başarısız olur.
    private static byte[] BuildAssociatedData(VaultFile f)
    {
        var k = f.Kdf;
        return Encoding.UTF8.GetBytes(
            $"{f.Format}|{f.Version}|{f.Cipher}|{k.Algorithm}|{k.Salt}|{k.MemoryKb}|{k.Iterations}|{k.Parallelism}");
    }
}
