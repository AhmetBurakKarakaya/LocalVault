using CommunityToolkit.Mvvm.ComponentModel;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Kilit ekranı ↔ kasa oluşturma ↔ kasa görünümü arasında gezinmeyi yönetir.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _services.Vault.Locked += (_, _) => ShowUnlock();

        if (File.Exists(services.Settings.EffectiveVaultPath))
            CurrentPage = CreateUnlockPage();
        else
            CurrentPage = CreateCreatePage(canGoBack: false);
    }

    partial void OnCurrentPageChanging(ViewModelBase oldValue, ViewModelBase newValue)
    {
        if (oldValue is IDisposable disposable)
            disposable.Dispose();
    }

    private void ShowUnlock() => CurrentPage = CreateUnlockPage();

    /// <summary>Kasa kilitliyken tarayıcı erişim istedi: kilit ekranında açıklama göster.</summary>
    public void ShowBrowserUnlockHint() =>
        ShowUnlockHint("Tarayıcı eklentisi kasanıza erişmek istiyor. Devam etmek için kilidi açın, sonra tarayıcıda tekrar deneyin.");

    public void ShowUnlockHint(string message)
    {
        if (CurrentPage is UnlockViewModel unlock)
            unlock.Hint = message;
    }

    public Task<AutoTypeChoice?> RequestAutoTypeChoiceAsync(string windowTitle, IReadOnlyList<Vault.Core.Models.VaultEntry> matches) =>
        CurrentPage is VaultViewModel vault
            ? vault.RequestAutoTypeChoiceAsync(windowTitle, matches)
            : Task.FromResult<AutoTypeChoice?>(null);

    /// <summary>Kasa açıksa onay katmanını gösterir; kilitliyse isteği reddeder.</summary>
    public Task<bool> RequestBrowserApprovalAsync(string browser, string verificationCode, CancellationToken ct) =>
        CurrentPage is VaultViewModel vault
            ? vault.RequestBrowserApprovalAsync(browser, verificationCode, ct)
            : Task.FromResult(false);

    public void Notify(string message)
    {
        if (CurrentPage is VaultViewModel vault)
            vault.ShowStatus(message);
    }

    private void ShowVault() => CurrentPage = new VaultViewModel(_services);

    private UnlockViewModel CreateUnlockPage() =>
        new(_services, onUnlocked: ShowVault, onCreateNew: () => CurrentPage = CreateCreatePage(canGoBack: true));

    private CreateVaultViewModel CreateCreatePage(bool canGoBack) =>
        new(_services, onCreated: ShowVault, onBack: canGoBack ? ShowUnlock : null);
}
