using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core;
using Vault.Core.Import;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

/// <summary>Kilidi açık kasanın ana ekranı: liste + ayrıntı/düzenleme paneli + katmanlar.</summary>
public sealed partial class VaultViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _ticker;
    private readonly QrScanService _qr;
    private DispatcherTimer? _statusTimer;

    public ObservableCollection<EntryListItemViewModel> Items { get; } = [];

    /// <summary>Sabit gezinme kategorileri.</summary>
    public IReadOnlyList<NavItemViewModel> Categories { get; } =
    [
        new("all", "Tüm kayıtlar", "IconGrid", _ => true),
        new("totp", "Doğrulama kodları", "IconClock", e => e.Totp is not null),
        new("autotype", "Auto-Type", "IconKeyboard", e => e.AutoTypeWindows.Count > 0),
    ];

    /// <summary>Kayıtlardaki etiketler (sayılarıyla); kayıtlar değiştikçe güncellenir.</summary>
    public ObservableCollection<NavItemViewModel> Tags { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle))]
    public partial NavItemViewModel? SelectedCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListTitle))]
    public partial NavItemViewModel? SelectedTag { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditEntryCommand), nameof(DeleteEntryCommand))]
    public partial EntryListItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial EntryDetailViewModel? Detail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsNotEditing), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(EditEntryCommand), nameof(DeleteEntryCommand), nameof(NewEntryCommand))]
    public partial EntryEditorViewModel? Editor { get; set; }

    /// <summary>Ekranın üzerinde gösterilen katman (onay, parola üreteci, ayarlar...).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    public partial ViewModelBase? Overlay { get; set; }

    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial bool IsStatusError { get; set; }

    public VaultViewModel(AppServices services)
    {
        _services = services;
        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        _ticker.Start();
        _qr = new QrScanService(services.Platform);
        _services.Vault.DataChanged += OnDataChanged;
        SelectedCategory = Categories[0];
        Refresh();
    }

    public string ListTitle => SelectedTag?.Label ?? SelectedCategory?.Label ?? Categories[0].Label;
    public bool HasTags => Tags.Count > 0;

    private NavItemViewModel ActiveFilter => SelectedTag ?? SelectedCategory ?? Categories[0];

    // Kategori ve etiket listeleri tek bir seçim gibi davranır.
    partial void OnSelectedCategoryChanged(NavItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedTag = null;
            Refresh(SelectedItem?.Entry.Id);
        }
        else if (SelectedTag is null)
        {
            SelectedCategory = Categories[0];
        }
    }

    partial void OnSelectedTagChanged(NavItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedCategory = null;
            Refresh(SelectedItem?.Entry.Id);
        }
        else if (SelectedCategory is null && !_updatingTags)
        {
            SelectedCategory = Categories[0];
        }
    }

    private bool _updatingTags;

    /// <summary>Sayıları ve etiket listesini kayıtlara göre günceller (mevcut öğe örnekleri korunur).</summary>
    private void UpdateNavigation()
    {
        foreach (var category in Categories)
            category.Count = Data.Entries.Count(category.Filter);

        var counts = Data.Entries
            .SelectMany(e => e.Tags.Distinct(StringComparer.CurrentCultureIgnoreCase))
            .GroupBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);

        _updatingTags = true;
        try
        {
            foreach (var stale in Tags.Where(t => !counts.ContainsKey(t.Label)).ToList())
            {
                if (ReferenceEquals(SelectedTag, stale))
                {
                    SelectedTag = null;
                    SelectedCategory = Categories[0];
                }
                Tags.Remove(stale);
            }
            foreach (var (tag, count) in counts.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = Tags.FirstOrDefault(t => t.Label.Equals(tag, StringComparison.CurrentCultureIgnoreCase));
                if (item is null)
                {
                    item = NavItemViewModel.ForTag(tag);
                    var index = Tags.TakeWhile(t => string.Compare(t.Label, tag, StringComparison.CurrentCultureIgnoreCase) < 0).Count();
                    Tags.Insert(index, item);
                }
                item.Count = count;
            }
        }
        finally
        {
            _updatingTags = false;
        }
        OnPropertyChanged(nameof(HasTags));
    }

    /// <summary>Kasa arayüz dışından değişti (ör. tarayıcıdan hesap kaydedildi): listeyi yenile.</summary>
    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_services.Vault.IsUnlocked)
            Refresh(SelectedItem?.Entry.Id);
    }

    private VaultData Data => _services.Vault.Session.Data;

    public bool IsEditing => Editor is not null;
    public bool IsNotEditing => Editor is null;
    public bool HasOverlay => Overlay is not null;
    public bool ShowPlaceholder => Detail is null && Editor is null;
    public bool IsEmpty => Data.Entries.Count == 0;
    public bool HasNoResults => Items.Count == 0 && !IsEmpty;
    public string CountText => Items.Count == Data.Entries.Count || ActiveFilter != Categories[0] && string.IsNullOrWhiteSpace(SearchText)
        ? $"{Items.Count} kayıt"
        : $"{Items.Count} / {Data.Entries.Count} kayıt";
    /// <summary>Ekrandaki/panodaki görüntüden QR okuma yalnızca Windows'ta var (dosyadan okuma her yerde).</summary>
    public bool CanScanScreen => OperatingSystem.IsWindows();
    public string VaultName => Path.GetFileName(_services.Vault.Session.FilePath);

    partial void OnSearchTextChanged(string value) => Refresh(SelectedItem?.Entry.Id);

    partial void OnSelectedItemChanged(EntryListItemViewModel? value) =>
        Detail = value is null ? null : new EntryDetailViewModel(value.Entry, _services, ShowStatus, onEdit: EditEntry, onDelete: DeleteEntry);

    /// <summary>Listeyi arama metnine göre yeniden oluşturur ve mümkünse seçimi korur.</summary>
    private void Refresh(Guid? selectId = null)
    {
        UpdateNavigation();
        var filter = ActiveFilter.Filter;
        Items.Clear();
        foreach (var entry in Data.Search(SearchText).Where(filter))
            Items.Add(new EntryListItemViewModel(entry));

        SelectedItem = selectId is { } id ? Items.FirstOrDefault(i => i.Entry.Id == id) : null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(CountText));
    }

    private void Tick()
    {
        Detail?.RefreshTotp();
        Editor?.RefreshTotp();
    }

    // ---- Kayıt işlemleri ----

    private bool CanStartEditing() => Editor is null;
    private bool CanModifySelected() => Editor is null && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanStartEditing))]
    private void NewEntry() => Editor = CreateEditor(null);

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void EditEntry()
    {
        if (SelectedItem is { } item)
            Editor = CreateEditor(item.Entry);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void DeleteEntry()
    {
        if (SelectedItem is not { } item)
            return;

        Overlay = new ConfirmViewModel(
            "Kaydı sil",
            $"'{item.Title}' kalıcı olarak silinsin mi? Bu işlem geri alınamaz.",
            "Sil",
            isDestructive: true,
            onConfirm: () =>
            {
                Data.Remove(item.Entry.Id);
                if (TrySave())
                    ShowStatus($"'{item.Title}' silindi.");
                Refresh();
            },
            onClose: CloseOverlay);
    }

    private EntryEditorViewModel CreateEditor(VaultEntry? source) => new(
        source,
        onSave: SaveEditor,
        onCancel: () => Editor = null,
        onRequestGenerator: editor => Overlay = new PasswordGeneratorViewModel(
            _services.Clipboard, onUse: password => editor.Password = password, onClose: CloseOverlay, notify: ShowStatus),
        scanQr: _qr.ScanAsync,
        onMultipleAccounts: ShowQrImport)
    {
        AutoTypeHotkey = HotkeyGesture.FromSettings(_services.Settings.AutoTypeHotkey).ToString(),
    };

    private bool SaveEditor(EntryEditorViewModel editor)
    {
        // Kasa dışarıdan yeniden yüklenmiş olabilir; kaydı her zaman güncel veride ID ile bul.
        var entry = Data.FindById(editor.Id);
        if (entry is null)
        {
            entry = new VaultEntry { Id = editor.Id };
            Data.Entries.Add(entry);
        }
        editor.ApplyTo(entry);

        if (!TrySave())
            return false;

        Editor = null;
        Refresh(entry.Id);
        ShowStatus(editor.IsNew ? $"'{entry.Title}' eklendi." : $"'{entry.Title}' kaydedildi.");
        return true;
    }

    /// <summary>Kasayı diske yazar; hata olursa kullanıcıya gösterir.</summary>
    private bool TrySave()
    {
        try
        {
            _services.Vault.Session.Save();
            return true;
        }
        catch (VaultConcurrencyException)
        {
            Overlay = new ConfirmViewModel(
                "Kasa dışarıdan değişmiş",
                "Kasa dosyası başka bir uygulama tarafından değiştirilmiş, bu yüzden değişiklikleriniz kaydedilmedi.\n\n" +
                "Diskteki sürüm yüklensin mi? Açık bir düzenleme varsa form korunur; yükledikten sonra tekrar kaydedebilirsiniz.",
                "Yeniden yükle",
                isDestructive: false,
                onConfirm: ReloadFromDisk,
                onClose: CloseOverlay);
            return false;
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Kaydedilemedi: {ex.Message}", isError: true);
            return false;
        }
    }

    private void ReloadFromDisk()
    {
        try
        {
            _services.Vault.Session.Reload();
            Refresh(SelectedItem?.Entry.Id);
            ShowStatus("Kasa diskten yeniden yüklendi.");
        }
        catch (VaultReopenRequiredException ex)
        {
            ShowStatus(ex.Message, isError: true);
            _services.Vault.Lock();
        }
        catch (Exception ex) when (ex is VaultException or IOException)
        {
            ShowStatus($"Yüklenemedi: {ex.Message}", isError: true);
        }
    }

    // ---- Kopyalama kısayolları (Ctrl+B / Ctrl+C / Ctrl+T) ----

    [RelayCommand]
    private Task CopyUsernameAsync() => Detail?.CopyUsernameAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task CopyPasswordAsync() => Detail?.CopyPasswordAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task CopyTotpAsync() => Detail?.CopyTotpAsync() ?? Task.CompletedTask;

    // ---- Katmanlar ----

    [RelayCommand]
    private void OpenGenerator() =>
        Overlay = new PasswordGeneratorViewModel(_services.Clipboard, onUse: null, onClose: CloseOverlay, notify: ShowStatus);

    [RelayCommand]
    private void OpenSettings() => Overlay = new SettingsViewModel(_services, CloseOverlay, ShowStatus);

    [RelayCommand]
    private void ChangeMasterPassword() => Overlay = new ChangeMasterPasswordViewModel(_services, CloseOverlay, ShowStatus);

    [RelayCommand]
    private void CloseOverlay() => Overlay = null;

    // ---- İçe aktarma (CSV, QR) ----

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var path = await _services.Platform.PickFileAsync("İçe aktarılacak CSV dosyasını seçin", "CSV dosyaları", ["*.csv", "*.txt"]);
        if (path is null)
            return;

        ImportResult result;
        try
        {
            result = CsvImporter.Import(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            ShowStatus($"CSV okunamadı: {ex.Message}", isError: true);
            return;
        }

        var (fresh, duplicates) = CsvImporter.SplitDuplicates(Data, result.Entries);
        Overlay = new ImportPreviewViewModel($"{CsvImporter.FormatName(result.Format)} · {Path.GetFileName(path)}",
            fresh, duplicates.Count, result.SkippedRows, result.Warnings, plaintextFile: true, AddImported, CloseOverlay);
    }

    [RelayCommand]
    private async Task ImportQrAsync(QrSource source)
    {
        var result = await _qr.ScanAsync(source);
        if (result.Cancelled)
            return;
        if (result.Error is not null)
        {
            ShowStatus(result.Error, isError: true);
            return;
        }
        ShowQrImport(result);
    }

    private void ShowQrImport(QrScanResult result)
    {
        var (fresh, duplicates) = CsvImporter.SplitDuplicates(Data, result.Accounts.Select(OtpImport.ToEntry));
        Overlay = new ImportPreviewViewModel(
            result.Accounts.Count == 1 ? "QR kodu" : $"QR kodu · {result.Accounts.Count} hesap",
            fresh, duplicates.Count, 0, result.Warnings, plaintextFile: false, AddImported, CloseOverlay);
    }

    private bool AddImported(IReadOnlyList<VaultEntry> entries)
    {
        Data.Entries.AddRange(entries);
        if (!TrySave())
        {
            foreach (var entry in entries)
                Data.Remove(entry.Id);
            return false;
        }
        Refresh(entries.Count == 1 ? entries[0].Id : null);
        ShowStatus(entries.Count == 1 ? $"'{entries[0].Title}' içe aktarıldı." : $"{entries.Count} kayıt içe aktarıldı.");
        return true;
    }

    // ---- Auto-Type kayıt seçimi ----

    public Task<AutoTypeChoice?> RequestAutoTypeChoiceAsync(string windowTitle, IReadOnlyList<VaultEntry> matches)
    {
        var tcs = new TaskCompletionSource<AutoTypeChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        AutoTypeChooserViewModel? chooser = null;
        chooser = new AutoTypeChooserViewModel(windowTitle, matches, Data.Entries, choice =>
        {
            tcs.TrySetResult(choice);
            if (ReferenceEquals(Overlay, chooser))
                Overlay = null;
        });
        Overlay = chooser;
        return tcs.Task;
    }

    partial void OnOverlayChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        // Onay katmanı yanıtlanmadan kapandıysa (Esc, başka katman) istek reddedilmiş sayılır.
        if (oldValue is BrowserApprovalViewModel approval)
            approval.Cancel();
        if (oldValue is AutoTypeChooserViewModel chooser)
            chooser.Finish(null);
    }

    /// <summary>Tarayıcı eklentisinin bağlanma isteğini kullanıcıya sorar.</summary>
    public Task<bool> RequestBrowserApprovalAsync(string browser, string verificationCode, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowserApprovalViewModel? approval = null;
        approval = new BrowserApprovalViewModel(browser, verificationCode, approved =>
        {
            tcs.TrySetResult(approved);
            if (ReferenceEquals(Overlay, approval))
                Overlay = null;
        });

        // Zaman aşımı veya bağlantı kopması: UI iş parçacığında katmanı kapat.
        ct.Register(() => Dispatcher.UIThread.Post(approval.Cancel));
        Overlay = approval;
        return tcs.Task;
    }

    /// <summary>Esc: önce katmanı, sonra düzenleyiciyi kapatır.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (Overlay is not null)
            Overlay = null;
        else if (Editor is not null)
            Editor = null;
        else
            SearchText = "";
    }

    [RelayCommand]
    private async Task LockAsync()
    {
        await _services.Clipboard.ClearIfOwnedAsync();
        _services.Vault.Lock();
    }

    // ---- Durum mesajı ----

    public void ShowStatus(string message) => ShowStatus(message, isError: false);

    public void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;

        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(isError ? 8 : 4), DispatcherPriority.Background, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            StatusMessage = null;
        });
        _statusTimer.Start();
    }

    public void Dispose()
    {
        (Overlay as BrowserApprovalViewModel)?.Cancel();
        (Overlay as AutoTypeChooserViewModel)?.Finish(null);
        _services.Vault.DataChanged -= OnDataChanged;
        _ticker.Stop();
        _statusTimer?.Stop();
    }
}
