using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class AutoTypeChooserView : UserControl
{
    public AutoTypeChooserView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Search.Focus(), DispatcherPriority.Input);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AutoTypeChooserViewModel vm && vm.ConfirmCommand.CanExecute(null))
            vm.ConfirmCommand.Execute(null);
    }
}
