using Avalonia.Threading;
using Vault.Bridge;
using Vault.Core.Models;

namespace Vault.Desktop.Services;

/// <summary>Köprü işleyicisinin açık kasaya erişimi (yalnızca UI iş parçacığında çağrılır).</summary>
public sealed class DesktopVaultAccess(VaultService vault) : IVaultAccess
{
    public bool IsUnlocked => vault.IsUnlocked;
    public VaultData Data => vault.Session.Data;
    public void Save()
    {
        vault.Session.Save();
        vault.NotifyDataChanged();   // ör. tarayıcıdan kaydedilen hesap listede hemen görünsün
    }
}

/// <summary>
/// Tarayıcı entegrasyonu: native host'u tarayıcılara kaydeder ve named pipe sunucusunu çalıştırır.
/// Gelen istekler UI iş parçacığında işlenir; böylece kasa oturumuna arayüzle aynı iş parçacığından erişilir.
/// </summary>
public sealed class BrowserIntegration(VaultService vault, IBridgeUi ui, NativeMessagingRegistrar? registrar = null) : IAsyncDisposable
{
    private readonly BridgeRequestHandler _handler = new(new DesktopVaultAccess(vault), ui);
    private readonly NativeMessagingRegistrar _registrar = registrar ?? NativeMessagingRegistrar.CreateDefault();
    private BridgeServer? _server;

    public static string HostPath => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "LocalVault.NativeHost.exe" : "LocalVault.NativeHost");

    public bool IsRunning => _server is not null;

    /// <summary>Kullanıcıya gösterilecek durum metni.</summary>
    public string Status { get; private set; } = "Kapalı";

    public event Action? StatusChanged;

    public void Start()
    {
        if (_server is not null)
            return;

        try
        {
            // Uygulama taşınmış/güncellenmiş olabilir: kayıt bu kopyayı göstermiyorsa yenile.
            if (NativeMessagingRegistrar.AllBrowsers.Any(b => !_registrar.IsRegistered(b, HostPath)))
                _registrar.Register(HostPath);
            Status = "Açık — Chrome, Edge ve Firefox için kayıtlı";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Status = $"Tarayıcı kaydı yapılamadı: {ex.Message}";
        }

        _server = new BridgeServer(BridgeProtocol.DefaultPipeName,
            (request, ct) => Dispatcher.UIThread.InvokeAsync(() => _handler.HandleAsync(request, ct)));
        _server.Error += ex => System.Diagnostics.Trace.WriteLine($"LocalVault köprü hatası: {ex}");
        _server.Start();
        StatusChanged?.Invoke();
    }

    public async Task StopAsync(bool unregister)
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
        if (unregister)
        {
            try
            {
                _registrar.Unregister();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kayıt kaldırılamasa da sunucu kapalı; eklenti "uygulama çalışmıyor" görür.
            }
        }
        Status = "Kapalı";
        StatusChanged?.Invoke();
    }

    public ValueTask DisposeAsync() => new(StopAsync(unregister: false));
}
