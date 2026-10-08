using System.Text;
using System.Text.Json.Nodes;
using Vault.Core.Crypto;
using Vault.Core.Models;
using Vault.Core.Storage;

namespace Vault.Core.Tests;

public sealed class VaultSessionTests : IDisposable
{
    private const string Password = "doğru-parola-123";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LocalVaultTests", Guid.NewGuid().ToString("N"));
    private string VaultPath => Path.Combine(_dir, "vault.json");

    // Testlerin hızlı çalışması için düşük maliyetli KDF.
    private static KdfParameters FastKdf() => new()
    {
        Salt = Convert.ToBase64String(new byte[16].Select((_, i) => (byte)i).ToArray()),
        MemoryKb = 64,
        Iterations = 1,
        Parallelism = 1,
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void CreateSaveAndReopenRoundTrips()
    {
        using (var session = VaultSession.Create(VaultPath, Password, FastKdf()))
        {
            session.Data.Entries.Add(new VaultEntry
            {
                Title = "GitHub",
                Urls = ["https://github.com"],
                Username = "ahmet",
                Password = "çok-gizli",
                Totp = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP" },
            });
            session.Save();
        }

        using var reopened = VaultSession.Open(VaultPath, Password);
        var entry = Assert.Single(reopened.Data.Entries);
        Assert.Equal("çok-gizli", entry.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", entry.Totp!.Secret);
    }

    [Fact]
    public void FileOnDiskDoesNotContainSecretsInPlainText()
    {
        using (var session = VaultSession.Create(VaultPath, Password, FastKdf()))
        {
            session.Data.Entries.Add(new VaultEntry { Title = "Banka", Username = "kullanici42", Password = "ParolaBurada" });
            session.Save();
        }

        var text = File.ReadAllText(VaultPath);
        Assert.DoesNotContain("ParolaBurada", text);
        Assert.DoesNotContain("kullanici42", text);
        Assert.DoesNotContain("Banka", text);
        Assert.Contains("\"cipher\": \"aes-256-gcm\"", text);
    }

    [Fact]
    public void WrongPasswordThrows()
    {
        VaultSession.Create(VaultPath, Password, FastKdf()).Dispose();
        Assert.Throws<InvalidMasterPasswordException>(() => VaultSession.Open(VaultPath, "yanlış"));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("tag")]
    [InlineData("nonce")]
    public void TamperedCiphertextIsDetected(string field)
    {
        VaultSession.Create(VaultPath, Password, FastKdf()).Dispose();
        MutateJson(json =>
        {
            var bytes = Convert.FromBase64String(json[field]!.GetValue<string>());
            bytes[0] ^= 0xFF;
            json[field] = Convert.ToBase64String(bytes);
        });

        Assert.Throws<InvalidMasterPasswordException>(() => VaultSession.Open(VaultPath, Password));
    }

    [Fact]
    public void TamperedKdfHeaderIsDetected()
    {
        // KDF parametreleri AAD'ye bağlı; anahtar aynı kalsa bile başlık değişikliği yakalanmalı.
        VaultSession.Create(VaultPath, Password, FastKdf()).Dispose();
        MutateJson(json => json["kdf"]!["memoryKb"] = 128);

        Assert.Throws<InvalidMasterPasswordException>(() => VaultSession.Open(VaultPath, Password));
    }

    [Fact]
    public void FileSavedWithUtf8BomCanBeOpened()
    {
        VaultSession.Create(VaultPath, Password, FastKdf()).Dispose();
        File.WriteAllText(VaultPath, File.ReadAllText(VaultPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        using var session = VaultSession.Open(VaultPath, Password);
        Assert.Empty(session.Data.Entries);
    }

    [Fact]
    public void NonVaultFileIsRejected()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(VaultPath, """{"entries":[]}""");
        Assert.Throws<VaultFormatException>(() => VaultSession.Open(VaultPath, Password));
    }

    [Fact]
    public void CreateRefusesToOverwriteExistingVault()
    {
        VaultSession.Create(VaultPath, Password, FastKdf()).Dispose();
        Assert.Throws<VaultException>(() => VaultSession.Create(VaultPath, Password, FastKdf()));
    }

    [Fact]
    public void SaveKeepsBackupOfPreviousVersion()
    {
        using var session = VaultSession.Create(VaultPath, Password, FastKdf());
        session.Data.Entries.Add(new VaultEntry { Title = "A" });
        session.Save();

        Assert.True(File.Exists(VaultPath + ".bak"));
        Assert.False(File.Exists(VaultPath + ".tmp"));
    }

    [Fact]
    public void ChangeMasterPasswordReencryptsWithNewSalt()
    {
        string oldSalt;
        using (var session = VaultSession.Create(VaultPath, Password, FastKdf()))
        {
            session.Data.Entries.Add(new VaultEntry { Title = "A" });
            session.Save();
            oldSalt = ReadJson()["kdf"]!["salt"]!.GetValue<string>();
            session.ChangeMasterPassword("yeni-parola");
        }

        Assert.NotEqual(oldSalt, ReadJson()["kdf"]!["salt"]!.GetValue<string>());
        Assert.Throws<InvalidMasterPasswordException>(() => VaultSession.Open(VaultPath, Password));
        using var reopened = VaultSession.Open(VaultPath, "yeni-parola");
        Assert.Single(reopened.Data.Entries);
    }

    [Fact]
    public void ExternalModificationIsDetectedOnSave()
    {
        using var first = VaultSession.Create(VaultPath, Password, FastKdf());
        using (var second = VaultSession.Open(VaultPath, Password))
        {
            second.Data.Entries.Add(new VaultEntry { Title = "ikinci" });
            second.Save();
        }

        first.Data.Entries.Add(new VaultEntry { Title = "birinci" });
        Assert.Throws<VaultConcurrencyException>(first.Save);
    }

    [Fact]
    public void ReloadPicksUpExternalChangesAndAllowsSavingAgain()
    {
        using var first = VaultSession.Create(VaultPath, Password, FastKdf());
        using (var second = VaultSession.Open(VaultPath, Password))
        {
            second.Data.Entries.Add(new VaultEntry { Title = "dışarıdan" });
            second.Save();
        }

        first.Reload();
        Assert.Equal("dışarıdan", Assert.Single(first.Data.Entries).Title);

        first.Data.Entries.Add(new VaultEntry { Title = "sonra" });
        first.Save();
        using var check = VaultSession.Open(VaultPath, Password);
        Assert.Equal(2, check.Data.Entries.Count);
    }

    [Fact]
    public void ReloadAfterExternalPasswordChangeRequiresReopen()
    {
        using var first = VaultSession.Create(VaultPath, Password, FastKdf());
        using (var second = VaultSession.Open(VaultPath, Password))
            second.ChangeMasterPassword("başka-parola");

        Assert.Throws<VaultReopenRequiredException>(first.Reload);
    }

    [Fact]
    public void VerifyMasterPassword()
    {
        using var session = VaultSession.Create(VaultPath, Password, FastKdf());
        Assert.True(session.VerifyMasterPassword(Password));
        Assert.False(session.VerifyMasterPassword("başka"));
    }

    [Fact]
    public void UnicodeNormalizationFormsDeriveSameKey()
    {
        var composed = "şifre";                              // ş = U+015F
        var decomposed = "şifre";                       // s + birleşen çengel
        Assert.NotEqual(composed, decomposed);
        Assert.Equal(KeyDerivation.DeriveKey(composed, FastKdf()), KeyDerivation.DeriveKey(decomposed, FastKdf()));
    }

    [Theory]
    [InlineData("scrypt", 16, 64, 1, 1)]
    [InlineData("argon2id", 8, 64, 1, 1)]       // tuz çok kısa
    [InlineData("argon2id", 16, 4, 1, 1)]       // bellek < 8 * paralellik
    [InlineData("argon2id", 16, 64, 0, 1)]      // tur sayısı 0
    [InlineData("argon2id", 16, 64, 1, 0)]      // paralellik 0
    public void InvalidKdfParametersAreRejected(string alg, int saltBytes, int memoryKb, int iterations, int parallelism)
    {
        var p = new KdfParameters
        {
            Algorithm = alg,
            Salt = Convert.ToBase64String(new byte[saltBytes]),
            MemoryKb = memoryKb,
            Iterations = iterations,
            Parallelism = parallelism,
        };
        Assert.Throws<VaultFormatException>(() => KeyDerivation.DeriveKey("x", p));
    }

    [Fact]
    public void PlainJsonExportImportRoundTrips()
    {
        var data = new VaultData();
        data.Entries.Add(new VaultEntry { Title = "X", Password = "p", MatchMode = UrlMatchMode.Host });

        var json = VaultJson.ToPlainJson(data);
        Assert.Contains("\"matchMode\": \"Host\"", json);

        var back = VaultJson.FromPlainJson(json);
        Assert.Equal("p", Assert.Single(back.Entries).Password);
    }

    private JsonObject ReadJson() => JsonNode.Parse(File.ReadAllText(VaultPath))!.AsObject();

    private void MutateJson(Action<JsonObject> mutate)
    {
        var json = ReadJson();
        mutate(json);
        File.WriteAllText(VaultPath, json.ToJsonString(), Encoding.UTF8);
    }
}
