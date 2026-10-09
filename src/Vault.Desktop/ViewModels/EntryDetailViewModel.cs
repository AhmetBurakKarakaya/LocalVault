using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Seçili kaydın salt okunur görünümü: kopyalama düğmeleri ve canlı TOTP.</summary>
public sealed partial class EntryDetailViewModel : ViewModelBase
{
    private const string PasswordMask = "••••••••••••";

    private readonly AppServices _services;
    private readonly Action<string> _notify;
    private readonly Action _onEdit;
    private readonly Action _onDelete;

    public VaultEntry Entry { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPassword))]
    public partial bool IsPasswordVisible { get; set; }

    [ObservableProperty]
    public partial string TotpCode { get; set; } = "";

    [ObservableProperty]
    public partial int TotpRemaining { get; set; }

    /// <summary>Kalan sürenin yüzdesi (0-100), ilerleme çubuğu için.</summary>
    [ObservableProperty]
    public partial double TotpProgress { get; set; }

    /// <summary>Kalan sürenin açısı (0-360), dairesel sayaç için.</summary>
    [ObservableProperty]
    public partial double TotpSweep { get; set; }

    [ObservableProperty]
    public partial bool IsTotpExpiring { get; set; }

    [ObservableProperty]
    public partial string? TotpError { get; set; }

    public EntryDetailViewModel(VaultEntry entry, AppServices services, Action<string> notify, Action onEdit, Action onDelete)
    {
        Entry = entry;
        _services = services;
        _notify = notify;
        _onEdit = onEdit;
        _onDelete = onDelete;
        UrlItems = entry.Urls.Select(url => new UrlItemViewModel(
            url,
            new AsyncRelayCommand(() => _services.Platform.OpenUrlAsync(url)),
            new AsyncRelayCommand(() => CopyUrlAsync(url)))).ToList();
        RefreshTotp();
    }

    public IReadOnlyList<UrlItemViewModel> UrlItems { get; }

    public string Title => Entry.Title;
    public string Username => Entry.Username;
    public bool HasUsername => !string.IsNullOrEmpty(Entry.Username);
    public bool HasPassword => !string.IsNullOrEmpty(Entry.Password);
    public string DisplayPassword => IsPasswordVisible ? Entry.Password : PasswordMask;
    public bool HasUrls => Entry.Urls.Count > 0;
    public bool HasTotp => Entry.Totp is not null;
    public string Notes => Entry.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Entry.Notes);
    public string Tags => string.Join(", ", Entry.Tags);
    public IReadOnlyList<string> TagList => Entry.Tags;
    public bool HasTags => Entry.Tags.Count > 0;
    public bool HasAutoType => Entry.AutoTypeWindows.Count > 0;
    public string AutoTypeText => string.Join(", ", Entry.AutoTypeWindows);
    public string AutoTypeLabel => $"Auto-Type ({HotkeyGesture.FromSettings(_services.Settings.AutoTypeHotkey)})";
    public string AutoTypeSequence => Entry.AutoTypeSequence ?? Vault.Core.AutoType.AutoTypeSequence.Default;
    /// <summary>Başlığın altındaki kısa bilgi: ilk web sitesinin alan adı.</summary>
    public string Subtitle => Entry.Urls.Select(HostOf).FirstOrDefault(h => h.Length > 0) ?? "";
    public bool HasSubtitle => Subtitle.Length > 0;
    public string Initial => Display.Initial(Entry.Title);
    public int ColorIndex => Display.ColorIndex(Entry.Title);
    public string MatchModeLabel => EntryEditorViewModel.MatchModeOptions.First(o => o.Value == Entry.MatchMode).Label;
    public string UpdatedText => $"Son değişiklik: {Entry.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}";

    private static string HostOf(string url) =>
        Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url, UriKind.Absolute, out var uri)
            ? uri.Host
            : url;

    public void RefreshTotp()
    {
        if (Entry.Totp is null)
            return;
        try
        {
            var code = OtpGenerator.ComputeTotp(Entry.Totp);
            TotpCode = Display.FormatOtp(code.Code);
            TotpRemaining = code.RemainingSeconds;
            TotpProgress = 100.0 * code.RemainingSeconds / code.Period;
            TotpSweep = 360.0 * code.RemainingSeconds / code.Period;
            IsTotpExpiring = code.RemainingSeconds <= 5;
            TotpError = null;
        }
        catch (ArgumentException ex)
        {
            TotpError = ex.Message;
            TotpCode = "";
        }
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    public async Task CopyUsernameAsync()
    {
        if (!HasUsername)
            return;
        await _services.Clipboard.CopyAsync(Entry.Username, sensitive: false);
        _notify("Kullanıcı adı kopyalandı.");
    }

    [RelayCommand]
    public async Task CopyPasswordAsync()
    {
        if (!HasPassword)
            return;
        await _services.Clipboard.CopyAsync(Entry.Password);
        _notify(SensitiveCopiedMessage("Parola"));
    }

    [RelayCommand]
    public async Task CopyTotpAsync()
    {
        if (Entry.Totp is null)
            return;
        // Kopyalama anında taze kod üret (ekrandaki kod saniyeler önce hesaplanmış olabilir).
        var code = OtpGenerator.ComputeTotp(Entry.Totp);
        await _services.Clipboard.CopyAsync(code.Code);
        _notify(SensitiveCopiedMessage("Doğrulama kodu"));
    }

    [RelayCommand]
    private void Edit() => _onEdit();

    [RelayCommand]
    private void Delete() => _onDelete();

    private async Task CopyUrlAsync(string url)
    {
        await _services.Clipboard.CopyAsync(url, sensitive: false);
        _notify("Adres kopyalandı.");
    }

    private string SensitiveCopiedMessage(string what)
    {
        var seconds = _services.Settings.ClipboardClearSeconds;
        return seconds > 0 ? $"{what} kopyalandı — {seconds} sn sonra panodan silinecek." : $"{what} kopyalandı.";
    }
}

public sealed record UrlItemViewModel(string Url, IAsyncRelayCommand OpenCommand, IAsyncRelayCommand CopyCommand);
