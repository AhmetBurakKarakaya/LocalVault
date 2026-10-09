using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class VaultView : UserControl
{
    public VaultView() => InitializeComponent();

    /// <summary>Kısayolların ana değiştiricisi: macOS'ta Cmd, diğerlerinde Ctrl.</summary>
    internal static KeyModifiers PrimaryModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    // KeyBinding yerine kabarcıklanan KeyDown kullanılır: Avalonia 12'de KeyBinding eşleşen tuşu odaktaki
    // öğeye hiç ulaştırmıyordu (arama kutusunda Delete kaydı silmeye, Ctrl+C parolayı kopyalamaya gidiyordu).
    // Burada yalnızca metin kutusu gibi odaktaki öğenin kullanmadığı tuşlar işlenir.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not VaultViewModel vm)
            return;

        if (e.Key == Key.F && e.KeyModifiers == PrimaryModifier)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (Match(vm, e) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private static ICommand? Match(VaultViewModel vm, KeyEventArgs e)
    {
        if (e.KeyModifiers == PrimaryModifier)
        {
            return e.Key switch
            {
                Key.N => vm.NewEntryCommand,
                Key.E => vm.EditEntryCommand,
                Key.L => vm.LockCommand,
                Key.G => vm.OpenGeneratorCommand,
                Key.B => vm.CopyUsernameCommand,
                Key.C => vm.CopyPasswordCommand,
                Key.T => vm.CopyTotpCommand,
                _ => null,
            };
        }
        if (e.KeyModifiers == KeyModifiers.None)
        {
            return e.Key switch
            {
                Key.Escape => vm.EscapeCommand,
                Key.Delete => vm.DeleteEntryCommand,
                _ => null,
            };
        }
        return null;
    }
}
