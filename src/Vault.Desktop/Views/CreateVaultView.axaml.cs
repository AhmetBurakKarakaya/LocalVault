using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Vault.Desktop.Views;

public partial class CreateVaultView : UserControl
{
    public CreateVaultView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => PasswordBox.Focus(), DispatcherPriority.Input);
    }
}
