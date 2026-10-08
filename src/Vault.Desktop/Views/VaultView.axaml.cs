using Avalonia.Controls;
using Avalonia.Input;

namespace Vault.Desktop.Views;

public partial class VaultView : UserControl
{
    public VaultView() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
