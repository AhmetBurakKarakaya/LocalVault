using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop.Tests;

/// <summary>
/// Her ekranı gerçek Skia çizimiyle PNG'ye döker (görsel kontrol için) ve çizim sırasında
/// hiçbir XAML bağlama hatası oluşmadığını doğrular.
/// Çıktı klasörü: LOCALVAULT_SCREENSHOTS ortam değişkeni veya test çıktısı altındaki "screenshots".
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private static readonly string OutputDir =
        Environment.GetEnvironmentVariable("LOCALVAULT_SCREENSHOTS") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "screenshots");

    private readonly TestEnvironment _env = new();

    public ScreenshotTests()
    {
        Directory.CreateDirectory(OutputDir);
        BindingErrorSink.Instance.Clear();
    }

    public void Dispose()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        _env.Dispose();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task RenderAllScreens(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        // 1) İlk açılış: kasa yok → oluşturma ekranı
        var main = new MainWindowViewModel(_env.Services);
        var window = new MainWindow { DataContext = main, Width = 1120, Height = 720 };
        window.Show();
        var create = (CreateVaultViewModel)main.CurrentPage;
        create.Password = "Kx7#mQ2p!zR9";
        Capture(window, theme, "01-create");

        // 2) Kilit ekranı (hatalı parola denemesiyle)
        await create.CreateCommand.ExecuteAsync(null);   // eşleşme hatası
        _env.Services.Vault.Lock();
        File.Delete(_env.VaultPath);
        _env.SeedVault(TestEnvironment.SampleEntries());
        main = new MainWindowViewModel(_env.Services);
        window.DataContext = main;
        var unlock = (UnlockViewModel)main.CurrentPage;
        unlock.Password = "yanlış";
        await unlock.UnlockCommand.ExecuteAsync(null);
        unlock.Password = "abc";
        Capture(window, theme, "02-unlock");

        // 3) Kasa: liste + TOTP'li kayıt ayrıntısı
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        var vault = (VaultViewModel)main.CurrentPage;
        Capture(window, theme, "03-vault-empty-selection");

        vault.SelectedItem = vault.Items.First(i => i.Title == "GitHub");
        await vault.CopyPasswordCommand.ExecuteAsync(null);
        Capture(window, theme, "04-vault-detail");

        vault.SelectedItem = vault.Items.First(i => i.Title == "Şirket Bankası");
        vault.Detail!.TogglePasswordVisibilityCommand.Execute(null);
        Capture(window, theme, "05-vault-detail-8digit-revealed");

        // 4) Düzenleyici (gelişmiş TOTP ayarları açık)
        vault.EditEntryCommand.Execute(null);
        vault.Editor!.ShowTotpAdvanced = true;
        Capture(window, theme, "06-editor");

        // 5) Yeni kayıt + parola üreteci katmanı
        vault.Editor.CancelCommand.Execute(null);
        vault.NewEntryCommand.Execute(null);
        vault.Editor!.Title = "Yeni hesap";
        vault.Editor.TotpSecret = "not base32!";
        vault.Editor.GeneratePasswordCommand.Execute(null);
        Capture(window, theme, "07-generator");

        // 6) Ayarlar ve silme onayı
        vault.CloseOverlayCommand.Execute(null);
        vault.Editor.CancelCommand.Execute(null);
        vault.OpenSettingsCommand.Execute(null);
        Capture(window, theme, "08-settings");

        vault.CloseOverlayCommand.Execute(null);
        vault.SelectedItem = vault.Items.First(i => i.Title == "Netflix");
        vault.DeleteEntryCommand.Execute(null);
        Capture(window, theme, "09-confirm-delete");

        vault.CloseOverlayCommand.Execute(null);
        vault.ChangeMasterPasswordCommand.Execute(null);
        Capture(window, theme, "10-change-password");

        // Tarayıcı eşleştirme onayı ve ayarlarda bağlı tarayıcı listesi
        vault.CloseOverlayCommand.Execute(null);
        var approval = vault.RequestBrowserApprovalAsync("Chrome", "630-DCD", CancellationToken.None);
        Capture(window, theme, "12-browser-approval");
        ((BrowserApprovalViewModel)vault.Overlay!).DenyCommand.Execute(null);
        Assert.False(await approval);
        _env.Services.Vault.Session.Data.BrowserAssociations.Add(new Vault.Core.Models.BrowserAssociation
        {
            ClientId = "c1", Key = Convert.ToBase64String(new byte[32]), Name = "Chrome",
        });
        _env.Services.Vault.Session.Data.BrowserAssociations.Add(new Vault.Core.Models.BrowserAssociation
        {
            ClientId = "c2", Key = Convert.ToBase64String(new byte[32]), Name = "Firefox",
        });
        vault.OpenSettingsCommand.Execute(null);
        Capture(window, theme, "13-settings-browsers");

        // Faz 5: Auto-Type seçimi ve içe aktarma önizlemesi
        vault.CloseOverlayCommand.Execute(null);
        var choice = vault.RequestAutoTypeChoiceAsync("10.0.0.5 - Uzak Masaüstü Bağlantısı", []);
        Capture(window, theme, "14-autotype-chooser");
        vault.CloseOverlayCommand.Execute(null);
        Assert.Null(await choice);

        _env.Platform.NextImage = Phase5FlowTests.QrImage(
            "otpauth://totp/Dropbox:ahmet@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Dropbox");
        await vault.ImportQrCommand.ExecuteAsync(Vault.Desktop.Services.QrSource.Screen);
        Capture(window, theme, "15-import-preview");

        // 7) Arama sonucu yok + dar pencere
        vault.CloseOverlayCommand.Execute(null);
        vault.SearchText = "bulunamayacak";
        window.Width = 820;
        window.Height = 560;
        Capture(window, theme, "11-no-results-narrow");

        window.Close();
        Assert.Empty(BindingErrorSink.Instance.Errors);
    }

    private static void Capture(Window window, string theme, string name)
    {
        // Sayfa geçişleri ve saydamlık animasyonları bitsin diye zamanlayıcıyı birkaç kare ilerlet.
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(OutputDir, $"{theme}-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
