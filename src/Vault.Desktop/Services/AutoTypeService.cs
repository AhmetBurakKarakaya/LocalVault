using Avalonia.Threading;
using Vault.Core.AutoType;
using Vault.Core.Models;
using Vault.Desktop.Platform;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Services;

/// <summary>Auto-Type'ın arayüzle etkileşimi (App uygular).</summary>
public interface IAutoTypeUi
{
    /// <summary>Pencereyi gösterip kayıt seçtirir; vazgeçilirse null.</summary>
    Task<AutoTypeChoice?> ChooseAsync(string windowTitle, IReadOnlyList<VaultEntry> matches);

    /// <summary>Seçimden sonra LocalVault penceresini eski durumuna (gizli/küçük) döndürür.</summary>
    void RestoreAfterChoice();

    void RequestUnlock(string message);
    void Notify(string message);
}

/// <summary>
/// Genel kısayol (varsayılan Ctrl+Alt+A, ayarlardan değiştirilebilir): önde olan pencerenin başlığına
/// göre kaydı bulur ve kullanıcı adı/parolayı klavye girdisi olarak yazar (RDP, VPN istemcisi, masaüstü uygulamaları için).
/// </summary>
/// Win32 çağrıları yalnızca Windows'ta yapılır; diğer platformlarda Start() hiçbir şey yapmaz.
public sealed class AutoTypeService(VaultService vault, IAutoTypeUi ui) : IDisposable
{
    private GlobalHotkey? _hotkey;
    private bool _busy;
    private bool _suspended;

    public bool IsRunning => _hotkey is not null;
    public string Status { get; private set; } = "Kapalı";

    /// <summary>Kullanılan (veya bir sonraki Start'ta kullanılacak) kısayol.</summary>
    public HotkeyGesture Hotkey { get; private set; } = HotkeyGesture.Default;
    private string HotkeyText => Hotkey.ToString();

    public void Start(HotkeyGesture? hotkey = null)
    {
        if (hotkey is not null && hotkey != Hotkey)
        {
            Stop();
            Hotkey = hotkey;
        }
        if (!OperatingSystem.IsWindows())
        {
            Status = "Auto-Type yalnızca Windows'ta kullanılabilir.";
            return;
        }
        if (_hotkey is not null)
            return;
        var registered = new GlobalHotkey(Hotkey.Win32Modifiers, Hotkey.Win32VirtualKey);
        if (!registered.IsRegistered)
        {
            registered.Dispose();
            Status = $"{HotkeyText} başka bir uygulama tarafından kullanılıyor; Ayarlar'dan başka bir kısayol seçin.";
            return;
        }
        registered.Pressed += () => Dispatcher.UIThread.Post(() => _ = RunAsync());
        _hotkey = registered;
        Status = $"Açık — kısayol: {HotkeyText}";
    }

    public void Stop()
    {
        _suspended = false;
        if (OperatingSystem.IsWindows())
            _hotkey?.Dispose();
        _hotkey = null;
        Status = "Kapalı";
    }

    /// <summary>
    /// Kısayol kaydedilirken geçici olarak bırakılır; yoksa mevcut kısayola basmak tuşu kayıt kutusuna
    /// ulaştırmaz (Windows onu doğrudan bize kısayol olarak iletir).
    /// </summary>
    public void Suspend()
    {
        if (_hotkey is null)
            return;
        Stop();
        _suspended = true;
    }

    public void Resume()
    {
        if (!_suspended)
            return;
        _suspended = false;
        Start();
    }

    /// <summary>Kısayolun bu bilgisayarda kaydedilebildiğini dener (başka uygulama kullanıyor mu?).</summary>
    public static bool CanRegister(HotkeyGesture hotkey)
    {
        if (!OperatingSystem.IsWindows())
            return true;
        var probe = new GlobalHotkey(hotkey.Win32Modifiers, hotkey.Win32VirtualKey);
        var ok = probe.IsRegistered;
        probe.Dispose();
        return ok;
    }

    /// <summary>Kısayola basıldığında (UI iş parçacığında) çalışır.</summary>
    public async Task RunAsync()
    {
        if (_busy || !OperatingSystem.IsWindows())
            return;
        _busy = true;
        try
        {
            var target = Win32Windows.GetForeground();
            if (target.Handle == 0 || target.ProcessId == Environment.ProcessId)
                return;   // LocalVault'un kendi penceresine yazılmaz

            if (!vault.IsUnlocked)
            {
                ui.RequestUnlock($"Auto-Type için kasanın kilidini açın, sonra \"{Shorten(target.Title)}\" penceresinde {HotkeyText} tuşlarına tekrar basın.");
                return;
            }

            var matches = WindowMatcher.FindMatches(vault.Session.Data.Entries, target.Title);
            VaultEntry entry;
            if (matches.Count == 1)
            {
                entry = matches[0];
            }
            else
            {
                var choice = await ui.ChooseAsync(target.Title, matches);
                ui.RestoreAfterChoice();
                if (choice is null)
                    return;
                entry = choice.Entry;
                if (choice.RememberWindow && !string.IsNullOrWhiteSpace(target.Title))
                    Remember(entry, target.Title);

                // Seçim için LocalVault öne gelmişti; odağı hedef pencereye geri ver.
                Win32Windows.Activate(target.Handle);
                await Task.Delay(250);
            }

            IReadOnlyList<Vault.Core.AutoType.AutoTypeAction> actions;
            try
            {
                actions = AutoTypeSequence.Compile(entry);
            }
            catch (FormatException ex)
            {
                ui.Notify($"'{entry.Title}' için Auto-Type dizisi hatalı: {ex.Message}");
                return;
            }

            await Task.Run(() =>
            {
                if (OperatingSystem.IsWindows())
                    Win32Input.Type(actions, target.Handle);
            });
        }
        catch (AutoTypeAbortedException ex)
        {
            ui.Notify(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Remember(VaultEntry entry, string windowTitle)
    {
        if (entry.AutoTypeWindows.Contains(windowTitle, StringComparer.InvariantCultureIgnoreCase))
            return;
        entry.AutoTypeWindows.Add(windowTitle);
        entry.Touch();
        try
        {
            vault.Session.Save();
            vault.NotifyDataChanged();
        }
        catch (Exception ex) when (ex is Vault.Core.VaultException or IOException)
        {
            ui.Notify($"Pencere kayda eklenemedi: {ex.Message}");
        }
    }

    private static string Shorten(string title) => title.Length <= 60 ? title : title[..57] + "…";

    public void Dispose() => Stop();
}
