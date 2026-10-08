using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vault.Core.Generation;
using Vault.Desktop.Services;

namespace Vault.Desktop.ViewModels;

public sealed partial class PasswordGeneratorViewModel : ViewModelBase
{
    private readonly IClipboardService _clipboard;
    private readonly Action<string>? _onUse;
    private readonly Action _onClose;
    private readonly Action<string> _notify;

    [ObservableProperty] public partial int Length { get; set; } = 20;
    [ObservableProperty] public partial bool Uppercase { get; set; } = true;
    [ObservableProperty] public partial bool Lowercase { get; set; } = true;
    [ObservableProperty] public partial bool Digits { get; set; } = true;
    [ObservableProperty] public partial bool Symbols { get; set; } = true;
    [ObservableProperty] public partial bool ExcludeAmbiguous { get; set; }
    [ObservableProperty] public partial string Generated { get; set; } = "";
    [ObservableProperty] public partial string EntropyText { get; set; } = "";
    [ObservableProperty] public partial int StrengthValue { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public PasswordGeneratorViewModel(IClipboardService clipboard, Action<string>? onUse, Action onClose, Action<string> notify)
    {
        _clipboard = clipboard;
        _onUse = onUse;
        _onClose = onClose;
        _notify = notify;
        Regenerate();
    }

    public bool CanUse => _onUse is not null;

    partial void OnLengthChanged(int value) => Regenerate();
    partial void OnUppercaseChanged(bool value) => Regenerate();
    partial void OnLowercaseChanged(bool value) => Regenerate();
    partial void OnDigitsChanged(bool value) => Regenerate();
    partial void OnSymbolsChanged(bool value) => Regenerate();
    partial void OnExcludeAmbiguousChanged(bool value) => Regenerate();

    [RelayCommand]
    private void Regenerate()
    {
        var options = new PasswordOptions
        {
            Length = Length,
            Uppercase = Uppercase,
            Lowercase = Lowercase,
            Digits = Digits,
            Symbols = Symbols,
            ExcludeAmbiguous = ExcludeAmbiguous,
        };
        try
        {
            Generated = PasswordGenerator.Generate(options);
            var bits = PasswordGenerator.EstimateEntropyBits(options);
            var level = bits switch { < 28 => StrengthLevel.VeryWeak, < 40 => StrengthLevel.Weak, < 60 => StrengthLevel.Fair, < 80 => StrengthLevel.Strong, _ => StrengthLevel.VeryStrong };
            StrengthValue = (int)level + 1;
            EntropyText = $"{Display.StrengthLabel(level)} · ~{bits:0} bit";
            ErrorMessage = null;
        }
        catch (ArgumentException ex)
        {
            Generated = "";
            EntropyText = "";
            StrengthValue = 0;
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Generated.Length == 0)
            return;
        await _clipboard.CopyAsync(Generated);
        _notify("Parola kopyalandı.");
    }

    [RelayCommand]
    private void Use()
    {
        if (Generated.Length == 0)
            return;
        _onUse?.Invoke(Generated);
        _onClose();
    }

    [RelayCommand]
    private void Close() => _onClose();
}
