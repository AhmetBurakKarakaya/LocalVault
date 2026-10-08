using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Vault.Desktop.Views;

public partial class EntryEditorView : UserControl
{
    public EntryEditorView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => TitleBox.Focus(), DispatcherPriority.Input);
    }
}
