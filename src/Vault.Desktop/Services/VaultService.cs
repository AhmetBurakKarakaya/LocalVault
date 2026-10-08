using Vault.Core.Models;
using Vault.Core.Storage;

namespace Vault.Desktop.Services;

/// <summary>
/// Açık kasa oturumunun tek sahibi. Kilitlendiğinde oturum (ve türetilmiş anahtar) yok edilir.
/// Faz 3'te native host istekleri de bu servis üzerinden yanıtlanacak.
/// </summary>
public sealed class VaultService : IDisposable
{
    private VaultSession? _session;

    public event EventHandler? Unlocked;
    public event EventHandler? Locked;

    /// <summary>Kasa içeriği arayüz dışından (ör. tarayıcı eklentisi) değiştirildi.</summary>
    public event EventHandler? DataChanged;

    public void NotifyDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);

    public bool IsUnlocked => _session is not null;

    public VaultSession Session => _session ?? throw new InvalidOperationException("Kasa kilitli.");

    /// <summary>Yalnızca testler içindir: yeni kasalar için hızlı KDF parametreleri.</summary>
    internal Func<KdfParameters>? KdfFactoryForTests { get; set; }

    public async Task UnlockAsync(string path, string masterPassword)
    {
        // Argon2 ~0.5-1 sn sürer; arayüz donmasın diye arka planda çalıştır.
        var session = await Task.Run(() => VaultSession.Open(path, masterPassword));
        SetSession(session);
    }

    public async Task CreateAsync(string path, string masterPassword)
    {
        var kdf = KdfFactoryForTests?.Invoke();
        var session = await Task.Run(() => VaultSession.Create(path, masterPassword, kdf));
        SetSession(session);
    }

    public void Lock()
    {
        if (_session is null)
            return;

        _session.Dispose();
        _session = null;
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private void SetSession(VaultSession session)
    {
        _session?.Dispose();
        _session = session;
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
