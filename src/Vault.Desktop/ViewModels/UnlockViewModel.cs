using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

public sealed partial class UnlockViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _onUnlocked;
    private readonly Action _onCreateNew;

    [ObservableProperty]
    public partial string VaultPath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Kilit ekranının neden açıldığını anlatan bilgi (ör. tarayıcı eklentisi erişim istedi).</summary>
    [ObservableProperty]
    public partial string? Hint { get; set; }

    public UnlockViewModel(AppServices services, Action onUnlocked, Action onCreateNew)
    {
        _services = services;
        _onUnlocked = onUnlocked;
        _onCreateNew = onCreateNew;
        VaultPath = services.Settings.EffectiveVaultPath;
        Password = "";
    }

    public string VaultFileName => Path.GetFileName(VaultPath);

    partial void OnVaultPathChanged(string value) => OnPropertyChanged(nameof(VaultFileName));

    private bool CanUnlock() => !IsBusy && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _services.Vault.UnlockAsync(VaultPath, Password);
            Password = "";

            if (_services.Settings.VaultPath != VaultPath)
            {
                _services.Settings.VaultPath = VaultPath;
                TrySaveSettings();
            }
            _onUnlocked();
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorMessage = "Ana parola yanlış.";
            Password = "";
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

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _services.Platform.PickExistingVaultAsync();
        if (path is not null)
        {
            VaultPath = path;
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private void CreateNew() => _onCreateNew();

    private void TrySaveSettings()
    {
        try
        {
            _services.SettingsStore.Save();
        }
        catch (IOException)
        {
            // Ayar kaydedilemese de kasa açık; bir sonraki açılışta varsayılan yol kullanılır.
        }
    }
}
