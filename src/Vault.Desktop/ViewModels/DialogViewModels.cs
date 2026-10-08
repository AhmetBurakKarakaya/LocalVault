using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core;
using Vault.Core.Generation;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Evet/Hayır onay penceresi (kasa görünümünün üzerinde katman olarak gösterilir).</summary>
public sealed partial class ConfirmViewModel(
    string title, string message, string confirmText, bool isDestructive, Action onConfirm, Action onClose) : ViewModelBase
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmText { get; } = confirmText;
    public bool IsDestructive { get; } = isDestructive;

    [RelayCommand]
    private void Confirm()
    {
        onClose();
        onConfirm();
    }

    [RelayCommand]
    private void Cancel() => onClose();
}

public sealed partial class ChangeMasterPasswordViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _onClose;
    private readonly Action<string> _notify;

    [ObservableProperty] public partial string CurrentPassword { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthValue), nameof(StrengthText))]
    public partial string NewPassword { get; set; } = "";

    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    public partial bool IsBusy { get; set; }

    public ChangeMasterPasswordViewModel(AppServices services, Action onClose, Action<string> notify)
    {
        _services = services;
        _onClose = onClose;
        _notify = notify;
    }

    public int StrengthValue => NewPassword.Length == 0 ? 0 : (int)PasswordStrength.Estimate(NewPassword).Level + 1;
    public string StrengthText => NewPassword.Length == 0 ? "" : Display.StrengthLabel(PasswordStrength.Estimate(NewPassword).Level);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        ErrorMessage = null;
        if (NewPassword.Length < CreateVaultViewModel.MinMasterPasswordLength)
        {
            ErrorMessage = $"Yeni ana parola en az {CreateVaultViewModel.MinMasterPasswordLength} karakter olmalı.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Yeni parolalar eşleşmiyor.";
            return;
        }

        IsBusy = true;
        try
        {
            var session = _services.Vault.Session;
            var current = CurrentPassword;
            var next = NewPassword;
            if (!await Task.Run(() => session.VerifyMasterPassword(current)))
            {
                ErrorMessage = "Mevcut ana parola yanlış.";
                return;
            }
            await Task.Run(() => session.ChangeMasterPassword(next));
            CurrentPassword = NewPassword = ConfirmPassword = "";
            _onClose();
            _notify("Ana parola değiştirildi.");
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanChange() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _onClose();
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    public static IReadOnlyList<Option<int>> AutoLockOptions { get; } =
    [
        new(0, "Kapalı"), new(1, "1 dakika"), new(5, "5 dakika"), new(10, "10 dakika"),
        new(15, "15 dakika"), new(30, "30 dakika"), new(60, "1 saat"),
    ];

    public static IReadOnlyList<Option<int>> ClipboardOptions { get; } =
    [
        new(0, "Silme"), new(10, "10 saniye"), new(20, "20 saniye"), new(30, "30 saniye"),
        new(60, "1 dakika"), new(120, "2 dakika"),
    ];

    public static IReadOnlyList<Option<AppTheme>> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Sistem"), new(AppTheme.Light, "Açık"), new(AppTheme.Dark, "Koyu"),
    ];

    private readonly AppServices _services;
    private readonly Action _onClose;
    private readonly Action<string> _notify;

    [ObservableProperty] public partial Option<int> AutoLock { get; set; }
    [ObservableProperty] public partial Option<int> ClipboardClear { get; set; }
    [ObservableProperty] public partial Option<AppTheme> Theme { get; set; }
    [ObservableProperty] public partial bool LockOnSessionLock { get; set; }
    [ObservableProperty] public partial bool LockOnMinimize { get; set; }
    [ObservableProperty] public partial bool MinimizeToTray { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial bool BrowserIntegrationEnabled { get; set; }
    [ObservableProperty] public partial bool AutoTypeEnabled { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    /// <summary>Kasayla eşleştirilmiş tarayıcı eklentileri.</summary>
    public ObservableCollection<BrowserAssociationItem> Associations { get; } = [];
    public bool HasAssociations => Associations.Count > 0;
    public string BrowserStatus => _services.Browser?.Status ?? "Kullanılamıyor";
    public bool AutoTypeSupported => OperatingSystem.IsWindows();
    public string AutoTypeStatus => _services.AutoType?.Status
        ?? (OperatingSystem.IsWindows() ? "Kapalı" : "Auto-Type yalnızca Windows'ta kullanılabilir.");

    public SettingsViewModel(AppServices services, Action onClose, Action<string> notify)
    {
        _services = services;
        _onClose = onClose;
        _notify = notify;

        var s = services.Settings;
        AutoLock = Closest(AutoLockOptions, s.AutoLockMinutes);
        ClipboardClear = Closest(ClipboardOptions, s.ClipboardClearSeconds);
        Theme = ThemeOptions.First(o => o.Value == s.Theme);
        LockOnSessionLock = s.LockOnSessionLock;
        LockOnMinimize = s.LockOnMinimize;
        MinimizeToTray = s.MinimizeToTray;
        StartMinimized = s.StartMinimized;
        BrowserIntegrationEnabled = s.BrowserIntegrationEnabled;
        AutoTypeEnabled = s.AutoTypeEnabled;

        if (services.Vault.IsUnlocked)
        {
            foreach (var association in services.Vault.Session.Data.BrowserAssociations)
                Associations.Add(new BrowserAssociationItem(association, RemoveAssociation));
        }
    }

    private void RemoveAssociation(BrowserAssociationItem item)
    {
        var associations = _services.Vault.Session.Data.BrowserAssociations;
        var index = associations.IndexOf(item.Association);
        if (index < 0)
            return;

        associations.RemoveAt(index);
        try
        {
            _services.Vault.Session.Save();
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            associations.Insert(index, item.Association);
            ErrorMessage = $"Bağlantı kaldırılamadı: {ex.Message}";
            return;
        }
        Associations.Remove(item);
        OnPropertyChanged(nameof(HasAssociations));
        _notify($"{item.Name} bağlantısı kaldırıldı; o eklenti artık kasaya erişemez.");
    }

    public string VaultPath => _services.Settings.EffectiveVaultPath;
    public string SettingsPath => _services.SettingsStore.FilePath;

    [RelayCommand]
    private void Save()
    {
        var s = _services.Settings;
        s.AutoLockMinutes = AutoLock.Value;
        s.ClipboardClearSeconds = ClipboardClear.Value;
        s.Theme = Theme.Value;
        s.LockOnSessionLock = LockOnSessionLock;
        s.LockOnMinimize = LockOnMinimize;
        s.MinimizeToTray = MinimizeToTray;
        s.StartMinimized = StartMinimized;
        s.BrowserIntegrationEnabled = BrowserIntegrationEnabled;
        s.AutoTypeEnabled = AutoTypeEnabled;
        try
        {
            _services.SettingsStore.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Ayarlar kaydedilemedi: {ex.Message}";
            return;
        }
        _services.ApplySettings?.Invoke(s);
        _onClose();
        _notify("Ayarlar kaydedildi.");
    }

    [RelayCommand]
    private void Cancel() => _onClose();

    private static Option<int> Closest(IReadOnlyList<Option<int>> options, int value) =>
        options.MinBy(o => Math.Abs(o.Value - value))!;
}

public sealed partial class BrowserAssociationItem(Vault.Core.Models.BrowserAssociation association, Action<BrowserAssociationItem> onRemove)
    : ViewModelBase
{
    public Vault.Core.Models.BrowserAssociation Association { get; } = association;
    public string Name => Association.Name;
    public string CreatedText => $"Bağlandı: {Association.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}";

    [RelayCommand]
    private void Remove() => onRemove(this);
}
