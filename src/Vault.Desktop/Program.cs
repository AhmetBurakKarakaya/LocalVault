using Avalonia;
using Vault.Desktop.Services;

namespace Vault.Desktop;

internal static class Program
{
    /// <summary>Bu örneğin tek-örnek kilidi; App pencereyi göster sinyallerini dinlemek için kullanır.</summary>
    internal static SingleInstance? Instance { get; private set; }

    // Avalonia başlatılmadan önce Avalonia API'leri veya SynchronizationContext kullanılmamalı.
    [STAThread]
    public static int Main(string[] args)
    {
        Instance = SingleInstance.TryAcquire();
        if (Instance is null)
        {
            // Zaten çalışıyor (muhtemelen tepside): mevcut pencereyi öne getir ve çık.
            SingleInstance.SignalExistingInstance();
            return 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance.Dispose();
        }
    }

    // Görsel tasarımcı da bu yöntemi kullanır; kaldırmayın.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
