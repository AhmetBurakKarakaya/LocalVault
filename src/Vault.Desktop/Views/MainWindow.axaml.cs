using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vault.Desktop.Services;

namespace Vault.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateTitleBarHeight();
        // macOS'ta kırmızı/sarı/yeşil pencere düğmeleri sol üsttedir; başlık onların sağından başlar.
        if (OperatingSystem.IsMacOS())
            TitleContent.Margin = new Thickness(80, 0, 16, 0);

        // Windows dışındaki platformlarda otomatik kilit, uygulama içi etkinliğe göre hesaplanır.
        AddHandler(KeyDownEvent, (_, _) => Idle?.ReportActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => Idle?.ReportActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public IIdleTimeSource? Idle { get; set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // XAML'da ayarlanınca (Avalonia 12.1, Win32) pencere çerçevesiz ve ekran boyutunda açılıyor;
        // bu yüzden genişletme pencere açıldıktan sonra yapılır.
        ExtendClientAreaToDecorationsHint = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty)
            UpdateBackdrop();
        else if (change.Property == WindowDecorationMarginProperty)
            UpdateTitleBarHeight();
    }

    // Özellikler XAML yüklenirken de değişir; o anda adlandırılmış öğeler henüz atanmamış olabilir.
    private void UpdateTitleBarHeight()
    {
        if (TitleBar is not null && WindowDecorationMargin.Top > 0)
            TitleBar.Height = Math.Max(WindowDecorationMargin.Top, 32);
    }

    // Mica/Acrylic varsa pencere zemini saydam bırakılır; yoksa düz tema rengi kullanılır.
    private void UpdateBackdrop()
    {
        var level = ActualTransparencyLevel;
        if (level == WindowTransparencyLevel.Mica || level == WindowTransparencyLevel.AcrylicBlur)
            Background = Brushes.Transparent;
        else
            this[!BackgroundProperty] = this.GetResourceObservable("WindowBg").ToBinding();
    }
}
