using CommunityToolkit.Mvvm.Input;

namespace Vault.Desktop.ViewModels;

/// <summary>Bir tarayıcı eklentisinin kasaya bağlanma isteği için onay katmanı.</summary>
public sealed partial class BrowserApprovalViewModel(string browser, string verificationCode, Action<bool> onDecision) : ViewModelBase
{
    private bool _decided;

    public string Browser { get; } = browser;
    public string VerificationCode { get; } = verificationCode;
    public string Message =>
        $"{Browser} tarayıcısındaki LocalVault eklentisi kasanıza bağlanmak istiyor. Onaylarsanız eklenti, " +
        "ziyaret ettiğiniz sitelere ait kayıtları listeleyebilir ve sizin seçtiğiniz hesabı doldurabilir.";

    [RelayCommand]
    private void Approve() => Decide(true);

    [RelayCommand]
    private void Deny() => Decide(false);

    /// <summary>Katman başka bir yolla kapandıysa (Esc, kilitlenme, zaman aşımı) istek reddedilmiş sayılır.</summary>
    public void Cancel() => Decide(false);

    private void Decide(bool approved)
    {
        if (_decided)
            return;
        _decided = true;
        onDecision(approved);
    }
}
