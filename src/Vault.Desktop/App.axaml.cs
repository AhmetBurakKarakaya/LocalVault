using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Vault.Bridge;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop;

public partial class App : Application, IBridgeUi, IAutoTypeUi
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private AppServices? _services;
    private AutoLockService? _autoLock;
    private NativeMenuItem? _lockMenuItem;
    private bool _exiting;
    private bool _hiddenBeforeChoice;
    private bool _minimizedBeforeChoice;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless testlerde masaüstü yaşam döngüsü yoktur; yalnızca kaynaklar yüklenir.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            StartDesktop(desktop);

        base.OnFrameworkInitializationCompleted();
    }

    private void StartDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        // Pencere kapatılınca uygulama tepside çalışmaya devam eder; çıkış tepsi menüsünden yapılır.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsStore = SettingsStore.Load();
        var idle = new IdleTimeSource();
        _window = new MainWindow { Idle = idle };

        var vault = new VaultService();
        _services = new AppServices
        {
            SettingsStore = settingsStore,
            Vault = vault,
            Browser = new BrowserIntegration(vault, this),
            AutoType = new AutoTypeService(vault, this),
            Clipboard = new ClipboardService(() => _window.Clipboard, () => settingsStore.Current.ClipboardClearSeconds),
            Platform = new PlatformService(() => _window),
            ApplySettings = ApplySettings,
        };
        ApplyTheme(settingsStore.Current);
        if (settingsStore.Current.BrowserIntegrationEnabled)
            _services.Browser.Start();
        if (settingsStore.Current.AutoTypeEnabled)
            _services.AutoType.Start(HotkeyGesture.FromSettings(settingsStore.Current.AutoTypeHotkey));

        _window.DataContext = new MainWindowViewModel(_services);
        _window.Closing += OnWindowClosing;
        _window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty
                && _window.WindowState == WindowState.Minimized
                && _services.Settings.LockOnMinimize)
                _ = LockAsync();
        };

        _autoLock = new AutoLockService(_services.Vault, () => _services.Settings, idle);
        _services.Vault.Locked += (_, _) =>
        {
            UpdateTrayMenu();
            // Kilit nedeni ne olursa olsun (boşta kalma, Windows kilidi, uyku) panodaki parola silinsin.
            _ = _services.Clipboard.ClearIfOwnedAsync();
        };
        _services.Vault.Unlocked += (_, _) => UpdateTrayMenu();
        CreateTrayIcon();
        // macOS: pencere gizliyken (tepside) Dock simgesine tıklanınca yeniden göster.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                    ShowMainWindow();
            };

        Program.Instance?.ListenForShowRequests(() => Dispatcher.UIThread.Post(ShowMainWindow));
        desktop.Exit += (_, _) => Cleanup();

        if (settingsStore.Current.StartMinimized)
            return;
        desktop.MainWindow = _window;
    }

    private void ApplySettings(AppSettings settings)
    {
        ApplyTheme(settings);
        if (_services?.AutoType is { } autoType)
        {
            if (settings.AutoTypeEnabled)
                autoType.Start(HotkeyGesture.FromSettings(settings.AutoTypeHotkey));   // kısayol değiştiyse yeniden kaydeder
            else if (autoType.IsRunning)
                autoType.Stop();
        }
        if (_services?.Browser is not { } browser)
            return;
        if (settings.BrowserIntegrationEnabled && !browser.IsRunning)
            browser.Start();
        else if (!settings.BrowserIntegrationEnabled && browser.IsRunning)
            _ = browser.StopAsync(unregister: true);
    }

    private void ApplyTheme(AppSettings settings) =>
        RequestedThemeVariant = settings.Theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    private void CreateTrayIcon()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://LocalVault/Assets/icon.png"));
        var showItem = new NativeMenuItem("LocalVault'u göster");
        showItem.Click += (_, _) => ShowMainWindow();
        _lockMenuItem = new NativeMenuItem("Kilitle");
        _lockMenuItem.Click += (_, _) => _ = LockAsync();
        var exitItem = new NativeMenuItem("Çıkış");
        exitItem.Click += (_, _) => _ = ExitAsync();

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "LocalVault",
            Menu = [showItem, _lockMenuItem, new NativeMenuItemSeparator(), exitItem],
        };
        tray.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, [tray]);
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_lockMenuItem is not null && _services is not null)
            _lockMenuItem.IsEnabled = _services.Vault.IsUnlocked;
    }

    private void ShowMainWindow()
    {
        if (_window is null)
            return;
        if (_desktop is not null && _desktop.MainWindow is null)
            _desktop.MainWindow = _window;

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || _services is null)
            return;

        if (_services.Settings.MinimizeToTray)
        {
            // Kapat düğmesi pencereyi tepsiye gizler; uygulama arka planda çalışmaya devam eder.
            e.Cancel = true;
            _window?.Hide();
        }
        else
        {
            e.Cancel = true;
            _ = ExitAsync();
        }
    }

    private async Task LockAsync()
    {
        if (_services is null)
            return;
        await _services.Clipboard.ClearIfOwnedAsync();
        _services.Vault.Lock();
    }

    private async Task ExitAsync()
    {
        _exiting = true;
        await LockAsync();
        if (_services?.Browser is { } browser)
            await browser.StopAsync(unregister: false);
        _window?.Close();
        _desktop?.Shutdown();
    }

    // ---- IBridgeUi: tarayıcı isteklerinin arayüze yansıması (UI iş parçacığında çağrılır) ----

    private MainWindowViewModel? MainViewModel => _window?.DataContext as MainWindowViewModel;

    Task<bool> IBridgeUi.ApproveAssociationAsync(string browser, string verificationCode, CancellationToken ct)
    {
        ShowMainWindow();
        return MainViewModel?.RequestBrowserApprovalAsync(browser, verificationCode, ct) ?? Task.FromResult(false);
    }

    void IBridgeUi.RequestUnlock()
    {
        ShowMainWindow();
        MainViewModel?.ShowBrowserUnlockHint();
    }

    void IBridgeUi.ShowApp() => ShowMainWindow();

    void IBridgeUi.Notify(string message) => MainViewModel?.Notify(message);

    // ---- IAutoTypeUi ----

    Task<AutoTypeChoice?> IAutoTypeUi.ChooseAsync(string windowTitle, IReadOnlyList<Vault.Core.Models.VaultEntry> matches)
    {
        _hiddenBeforeChoice = _window is { IsVisible: false };
        _minimizedBeforeChoice = _window is { WindowState: WindowState.Minimized };
        ShowMainWindow();
        return MainViewModel?.RequestAutoTypeChoiceAsync(windowTitle, matches) ?? Task.FromResult<AutoTypeChoice?>(null);
    }

    void IAutoTypeUi.RestoreAfterChoice()
    {
        if (_window is null)
            return;
        if (_hiddenBeforeChoice)
            _window.Hide();
        else if (_minimizedBeforeChoice)
            _window.WindowState = WindowState.Minimized;
    }

    void IAutoTypeUi.RequestUnlock(string message)
    {
        ShowMainWindow();
        MainViewModel?.ShowUnlockHint(message);
    }

    void IAutoTypeUi.Notify(string message) => MainViewModel?.Notify(message);

    private void Cleanup()
    {
        _autoLock?.Dispose();
        _services?.AutoType?.Dispose();
        _services?.Vault.Dispose();
    }
}
