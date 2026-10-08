using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vault.Desktop.Services;

namespace Vault.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Windows dışındaki platformlarda otomatik kilit, uygulama içi etkinliğe göre hesaplanır.
        AddHandler(KeyDownEvent, (_, _) => Idle?.ReportActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => Idle?.ReportActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public IIdleTimeSource? Idle { get; set; }
}
