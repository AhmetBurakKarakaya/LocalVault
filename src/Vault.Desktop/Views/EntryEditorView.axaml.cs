using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class EntryEditorView : UserControl
{
    public EntryEditorView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => TitleBox.Focus(), DispatcherPriority.Input);
    }

    // Ctrl+S (macOS'ta Cmd+S): kaydet. KeyBinding yerine KeyDown; bkz. VaultView.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.S && e.KeyModifiers == VaultView.PrimaryModifier
            && DataContext is EntryEditorViewModel vm && vm.SaveCommand.CanExecute(null))
        {
            vm.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
