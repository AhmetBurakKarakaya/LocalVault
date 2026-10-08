using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Vault.Desktop.Views;

public partial class UnlockView : UserControl
{
    public UnlockView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Kilit ekranı açılınca doğrudan parola yazılabilsin.
        Dispatcher.UIThread.Post(() => PasswordBox.Focus(), DispatcherPriority.Input);
    }
}
