using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Models;

namespace Vault.Desktop.ViewModels;

public sealed record AutoTypeChoice(VaultEntry Entry, bool RememberWindow);

/// <summary>
/// Auto-Type için kayıt seçimi: pencere birden fazla kayıtla eşleştiğinde veya hiç eşleşmediğinde açılır.
/// Eşleşme yoksa "bu pencereyi hatırla" varsayılan olarak işaretlidir; bir dahaki sefere doğrudan yazılır.
/// </summary>
public sealed partial class AutoTypeChooserViewModel : ViewModelBase
{
    private readonly IReadOnlyList<VaultEntry> _matches;
    private readonly IReadOnlyList<VaultEntry> _all;
    private readonly Action<AutoTypeChoice?> _onDone;
    private bool _done;

    public ObservableCollection<EntryListItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial EntryListItemViewModel? SelectedItem { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool RememberWindow { get; set; }

    public AutoTypeChooserViewModel(string windowTitle, IReadOnlyList<VaultEntry> matches, IReadOnlyList<VaultEntry> all,
        Action<AutoTypeChoice?> onDone)
    {
        WindowTitle = windowTitle;
        _matches = matches;
        _all = all;
        _onDone = onDone;
        RememberWindow = matches.Count == 0;
        Filter();
    }

    public string WindowTitle { get; }
    public bool HasMatches => _matches.Count > 0;
    public string Message => HasMatches
        ? "Bu pencere birden fazla kayıtla eşleşiyor. Hangisi yazılsın?"
        : "Bu pencere için Auto-Type tanımlı bir kayıt yok. Yazılacak kaydı seçin.";

    partial void OnSearchTextChanged(string value) => Filter();

    private void Filter()
    {
        IEnumerable<VaultEntry> source = string.IsNullOrWhiteSpace(SearchText) && HasMatches
            ? _matches
            : _all.Where(e => string.IsNullOrWhiteSpace(SearchText)
                              || e.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                              || e.Username.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

        Items.Clear();
        foreach (var entry in source)
            Items.Add(new EntryListItemViewModel(entry));
        SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Finish(SelectedItem is { } item ? new AutoTypeChoice(item.Entry, RememberWindow) : null);

    private bool CanConfirm() => SelectedItem is not null;

    [RelayCommand]
    private void Cancel() => Finish(null);

    public void Finish(AutoTypeChoice? choice)
    {
        if (_done)
            return;
        _done = true;
        _onDone(choice);
    }
}

/// <summary>CSV veya çok hesaplı QR içe aktarımının önizlemesi ve onayı.</summary>
public sealed partial class ImportPreviewViewModel : ViewModelBase
{
    private const int PreviewLimit = 200;

    private readonly IReadOnlyList<VaultEntry> _fresh;
    private readonly Func<IReadOnlyList<VaultEntry>, bool> _onImport;
    private readonly Action _onClose;

    public ImportPreviewViewModel(string source, IReadOnlyList<VaultEntry> fresh, int duplicates, int skippedRows,
        IReadOnlyList<string> warnings, bool plaintextFile, Func<IReadOnlyList<VaultEntry>, bool> onImport, Action onClose)
    {
        Source = source;
        _fresh = fresh;
        _onImport = onImport;
        _onClose = onClose;
        Warnings = warnings;
        PlaintextFile = plaintextFile;
        Items = fresh.Take(PreviewLimit).Select(e => new EntryListItemViewModel(e)).ToList();

        var parts = new List<string> { $"{fresh.Count} yeni kayıt" };
        if (duplicates > 0) parts.Add($"{duplicates} kayıt zaten kasada (atlanacak)");
        if (skippedRows > 0) parts.Add($"{skippedRows} satır giriş kaydı değil (atlanacak)");
        Summary = string.Join(" · ", parts);
    }

    public string Source { get; }
    public string Summary { get; }
    public IReadOnlyList<EntryListItemViewModel> Items { get; }
    public bool HasMore => _fresh.Count > PreviewLimit;
    public string MoreText => $"… ve {_fresh.Count - PreviewLimit} kayıt daha";
    public IReadOnlyList<string> Warnings { get; }
    public bool HasWarnings => Warnings.Count > 0;
    public bool PlaintextFile { get; }
    public string ImportText => _fresh.Count == 1 ? "1 kaydı içe aktar" : $"{_fresh.Count} kaydı içe aktar";

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        if (_onImport(_fresh))
            _onClose();
    }

    private bool CanImport() => _fresh.Count > 0;

    [RelayCommand]
    private void Cancel() => _onClose();
}
