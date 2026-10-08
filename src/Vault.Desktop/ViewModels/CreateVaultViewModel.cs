using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core;
using Vault.Core.Generation;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

public sealed partial class CreateVaultViewModel : ViewModelBase
{
    public const int MinMasterPasswordLength = 8;

    private readonly AppServices _services;
    private readonly Action _onCreated;
    private readonly Action? _onBack;

    [ObservableProperty]
    public partial string VaultPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthValue), nameof(StrengthText))]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsBusy { get; set; }

    public CreateVaultViewModel(AppServices services, Action onCreated, Action? onBack)
    {
        _services = services;
        _onCreated = onCreated;
        _onBack = onBack;
        VaultPath = File.Exists(services.Settings.EffectiveVaultPath)
            ? Path.Combine(Path.GetDirectoryName(services.Settings.EffectiveVaultPath)!, "yeni-kasa.json")
            : services.Settings.EffectiveVaultPath;
        Password = "";
        ConfirmPassword = "";
    }

    public bool CanGoBack => _onBack is not null;

    /// <summary>0-4 arası güç seviyesi (çubuk göstergesi için).</summary>
    public int StrengthValue => Password.Length == 0 ? 0 : (int)PasswordStrength.Estimate(Password).Level + 1;

    public string StrengthText => Password.Length == 0
        ? "Uzun ve tahmin edilmesi zor bir parola seçin. Unutursanız kasa açılamaz."
        : Display.StrengthLabel(PasswordStrength.Estimate(Password).Level);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ErrorMessage = Validate();
        if (ErrorMessage is not null)
            return;

        IsBusy = true;
        try
        {
            await _services.Vault.CreateAsync(VaultPath, Password);
            Password = ConfirmPassword = "";
            _services.Settings.VaultPath = VaultPath;
            try
            {
                _services.SettingsStore.Save();
            }
            catch (IOException)
            {
            }
            _onCreated();
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

    private bool CanCreate() => !IsBusy;

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(VaultPath))
            return "Kasa dosyası için bir konum seçin.";
        if (File.Exists(VaultPath))
            return "Bu konumda zaten bir dosya var. Başka bir konum seçin.";
        if (Password.Length < MinMasterPasswordLength)
            return $"Ana parola en az {MinMasterPasswordLength} karakter olmalı.";
        if (Password != ConfirmPassword)
            return "Parolalar eşleşmiyor.";
        return null;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _services.Platform.PickNewVaultPathAsync(VaultPath);
        if (path is not null)
            VaultPath = path;
    }

    [RelayCommand]
    private void Back() => _onBack?.Invoke();
}
