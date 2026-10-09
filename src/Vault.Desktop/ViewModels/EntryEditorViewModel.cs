using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.AutoType;
using Vault.Core.Generation;
using Vault.Core.Matching;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Yeni kayıt ekleme ve mevcut kaydı düzenleme formu.</summary>
public sealed partial class EntryEditorViewModel : ViewModelBase
{
    public static IReadOnlyList<Option<UrlMatchMode>> MatchModeOptions { get; } =
    [
        new(UrlMatchMode.Domain, "Alan adı ve alt alan adları"),
        new(UrlMatchMode.Host, "Yalnızca aynı host"),
        new(UrlMatchMode.StartsWith, "Adres bununla başlıyorsa"),
        new(UrlMatchMode.Exact, "Birebir aynı adres"),
        new(UrlMatchMode.Never, "Otomatik önerme"),
    ];

    public static IReadOnlyList<OtpHashAlgorithm> Algorithms { get; } = Enum.GetValues<OtpHashAlgorithm>();

    private readonly Func<EntryEditorViewModel, bool> _onSave;
    private readonly Action _onCancel;
    private readonly Action<EntryEditorViewModel> _onRequestGenerator;
    private readonly Func<QrSource, Task<QrScanResult>>? _scanQr;
    private readonly Action<QrScanResult>? _onMultipleAccounts;
    private bool _parsingTotp;

    public Guid Id { get; }
    public bool IsNew { get; }
    public string Heading => IsNew ? "Yeni kayıt" : "Kaydı düzenle";

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Username { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthValue), nameof(StrengthText))]
    public partial string Password { get; set; } = "";

    [ObservableProperty] public partial bool IsPasswordVisible { get; set; }
    [ObservableProperty] public partial string UrlsText { get; set; } = "";
    [ObservableProperty] public partial Option<UrlMatchMode> SelectedMatchMode { get; set; } = MatchModeOptions[0];
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial string TagsText { get; set; } = "";

    [ObservableProperty] public partial string TotpSecret { get; set; } = "";
    [ObservableProperty] public partial int TotpDigits { get; set; } = 6;
    [ObservableProperty] public partial int TotpPeriod { get; set; } = 30;
    [ObservableProperty] public partial OtpHashAlgorithm TotpAlgorithm { get; set; } = OtpHashAlgorithm.SHA1;
    [ObservableProperty] public partial string TotpPreview { get; set; } = "";
    [ObservableProperty] public partial string? TotpError { get; set; }
    [ObservableProperty] public partial bool ShowTotpAdvanced { get; set; }

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    [ObservableProperty] public partial string AutoTypeWindowsText { get; set; } = "";
    [ObservableProperty] public partial string AutoTypeSequence { get; set; } = "";
    [ObservableProperty] public partial string? AutoTypeError { get; set; }
    [ObservableProperty] public partial bool IsScanning { get; set; }

    public string DefaultAutoTypeSequence => Vault.Core.AutoType.AutoTypeSequence.Default;

    /// <summary>Ayarlardaki Auto-Type kısayolu (açıklama metni için).</summary>
    public string AutoTypeHotkey { get; init; } = HotkeyGesture.DefaultText;
    public string AutoTypeHint => $"Bu pencerelerden biri öndeyken {AutoTypeHotkey} tuşlarına basınca kullanıcı adı ve parola yazılır.";
    public bool CanScanScreen => OperatingSystem.IsWindows();

    public EntryEditorViewModel(
        VaultEntry? source,
        Func<EntryEditorViewModel, bool> onSave,
        Action onCancel,
        Action<EntryEditorViewModel> onRequestGenerator,
        Func<QrSource, Task<QrScanResult>>? scanQr = null,
        Action<QrScanResult>? onMultipleAccounts = null)
    {
        _onSave = onSave;
        _onCancel = onCancel;
        _onRequestGenerator = onRequestGenerator;
        _scanQr = scanQr;
        _onMultipleAccounts = onMultipleAccounts;

        IsNew = source is null;
        Id = source?.Id ?? Guid.NewGuid();
        if (source is null)
            return;

        Title = source.Title;
        Username = source.Username;
        Password = source.Password;
        UrlsText = string.Join(Environment.NewLine, source.Urls);
        SelectedMatchMode = MatchModeOptions.First(o => o.Value == source.MatchMode);
        Notes = source.Notes;
        TagsText = string.Join(", ", source.Tags);
        AutoTypeWindowsText = string.Join(Environment.NewLine, source.AutoTypeWindows);
        AutoTypeSequence = source.AutoTypeSequence ?? "";
        if (source.Totp is { } totp)
        {
            TotpSecret = totp.Secret;
            TotpDigits = totp.Digits;
            TotpPeriod = totp.Period;
            TotpAlgorithm = totp.Algorithm;
            ShowTotpAdvanced = totp.Digits != 6 || totp.Period != 30 || totp.Algorithm != OtpHashAlgorithm.SHA1;
        }
    }

    public int StrengthValue => Password.Length == 0 ? 0 : (int)PasswordStrength.Estimate(Password).Level + 1;
    public string StrengthText => Password.Length == 0 ? "" : Display.StrengthLabel(PasswordStrength.Estimate(Password).Level);

    public bool HasTotp => !string.IsNullOrWhiteSpace(TotpSecret);

    partial void OnTotpSecretChanged(string value)
    {
        if (_parsingTotp)
            return;

        // otpauth:// adresi yapıştırıldıysa (QR kodun içeriği) tüm alanları ondan doldur.
        if (OtpAuthUri.IsOtpAuthUri(value))
        {
            try
            {
                var info = OtpAuthUri.Parse(value);
                _parsingTotp = true;
                TotpSecret = info.Settings.Secret;
                TotpDigits = info.Settings.Digits;
                TotpPeriod = info.Settings.Period;
                TotpAlgorithm = info.Settings.Algorithm;
                if (string.IsNullOrWhiteSpace(Title) && info.Issuer is not null)
                    Title = info.Issuer;
                if (string.IsNullOrWhiteSpace(Username) && info.Account is not null)
                    Username = info.Account;
            }
            catch (FormatException ex)
            {
                TotpError = ex.Message;
                TotpPreview = "";
                return;
            }
            finally
            {
                _parsingTotp = false;
            }
        }
        OnPropertyChanged(nameof(HasTotp));
        RefreshTotp();
    }

    partial void OnAutoTypeSequenceChanged(string value) =>
        AutoTypeError = string.IsNullOrWhiteSpace(value) ? null : Vault.Core.AutoType.AutoTypeSequence.Validate(value);

    partial void OnTotpDigitsChanged(int value) => RefreshTotp();
    partial void OnTotpPeriodChanged(int value) => RefreshTotp();
    partial void OnTotpAlgorithmChanged(OtpHashAlgorithm value) => RefreshTotp();

    public void RefreshTotp()
    {
        if (_parsingTotp)
            return;
        if (!HasTotp)
        {
            TotpPreview = "";
            TotpError = null;
            return;
        }
        try
        {
            var code = OtpGenerator.ComputeTotp(BuildTotp()!);
            TotpPreview = $"{Display.FormatOtp(code.Code)}  ·  {code.RemainingSeconds} sn";
            TotpError = null;
        }
        catch (ArgumentException ex)
        {
            TotpPreview = "";
            TotpError = ex.Message;
        }
    }

    private TotpSettings? BuildTotp() => HasTotp
        ? new TotpSettings
        {
            Secret = OtpAuthUri.NormalizeSecret(TotpSecret),
            Digits = TotpDigits,
            Period = TotpPeriod,
            Algorithm = TotpAlgorithm,
        }
        : null;

    private List<string> ParseUrls() =>
        UrlsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private List<string> ParseTags() =>
        TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    /// <summary>Formu doğrular; hata yoksa null döner.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            return "Başlık boş olamaz.";

        foreach (var url in ParseUrls())
            if (!UrlMatcher.TryParse(url, out _))
                return $"Geçersiz web adresi: {url}";

        if (HasTotp)
        {
            try
            {
                BuildTotp()!.Validate();
            }
            catch (ArgumentException ex)
            {
                return $"İki adımlı doğrulama: {ex.Message}";
            }
        }
        if (!string.IsNullOrWhiteSpace(AutoTypeSequence) && Vault.Core.AutoType.AutoTypeSequence.Validate(AutoTypeSequence) is { } autoTypeError)
            return $"Auto-Type dizisi: {autoTypeError}";
        if (AutoTypeSequence.Contains("{TOTP}", StringComparison.OrdinalIgnoreCase) && !HasTotp)
            return "Auto-Type dizisinde {TOTP} var ama kayıtta doğrulama kodu tanımlı değil.";
        return null;
    }

    /// <summary>Form değerlerini kayda yazar.</summary>
    public void ApplyTo(VaultEntry entry)
    {
        entry.Title = Title.Trim();
        entry.Username = Username.Trim();
        entry.Password = Password;
        entry.Urls = ParseUrls();
        entry.MatchMode = SelectedMatchMode.Value;
        entry.Notes = Notes;
        entry.Tags = ParseTags();
        entry.Totp = BuildTotp();
        entry.AutoTypeWindows = AutoTypeWindowsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        entry.AutoTypeSequence = string.IsNullOrWhiteSpace(AutoTypeSequence) ? null : AutoTypeSequence.Trim();
        entry.Touch();
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = Validate();
        if (ErrorMessage is null && !_onSave(this))
            ErrorMessage ??= "Kaydedilemedi.";
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void GeneratePassword() => _onRequestGenerator(this);

    [RelayCommand]
    private void ToggleTotpAdvanced() => ShowTotpAdvanced = !ShowTotpAdvanced;

    /// <summary>QR kodundan TOTP ekler (ekran, pano veya dosya).</summary>
    [RelayCommand]
    private async Task ScanQrAsync(QrSource source)
    {
        if (_scanQr is null || IsScanning)
            return;
        IsScanning = true;
        try
        {
            var result = await _scanQr(source);
            if (result.Cancelled)
                return;
            if (result.Error is not null)
            {
                TotpError = result.Error;
                return;
            }
            if (result.Accounts.Count > 1)
            {
                TotpError = null;
                _onMultipleAccounts?.Invoke(result);
                return;
            }
            ApplyAccount(result.Accounts[0]);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplyAccount(OtpAuthInfo account)
    {
        _parsingTotp = true;
        try
        {
            TotpSecret = account.Settings.Secret;
            TotpDigits = account.Settings.Digits;
            TotpPeriod = account.Settings.Period;
            TotpAlgorithm = account.Settings.Algorithm;
        }
        finally
        {
            _parsingTotp = false;
        }
        if (string.IsNullOrWhiteSpace(Title) && account.Issuer is not null)
            Title = account.Issuer;
        if (string.IsNullOrWhiteSpace(Username) && account.Account is not null)
            Username = account.Account;
        ShowTotpAdvanced = TotpDigits != 6 || TotpPeriod != 30 || TotpAlgorithm != OtpHashAlgorithm.SHA1;
        OnPropertyChanged(nameof(HasTotp));
        RefreshTotp();
    }

    [RelayCommand]
    private void ClearTotp()
    {
        TotpSecret = "";
        TotpDigits = 6;
        TotpPeriod = 30;
        TotpAlgorithm = OtpHashAlgorithm.SHA1;
    }
}
