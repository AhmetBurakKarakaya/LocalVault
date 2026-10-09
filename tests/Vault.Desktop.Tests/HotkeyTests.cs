using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+A", "Ctrl+Alt+A")]
    [InlineData("ctrl + alt + a", "Ctrl+Alt+A")]
    [InlineData("Alt+Ctrl+A", "Ctrl+Alt+A")]           // değiştirici sırası normalleştirilir
    [InlineData("Control+Shift+f8", "Ctrl+Shift+F8")]
    [InlineData("Win+Alt+D5", "Alt+Win+5")]             // Avalonia rakam tuşu adı
    [InlineData("Meta+F24", "Win+F24")]
    public void ParsesAndFormats(string input, string expected) =>
        Assert.Equal(expected, HotkeyGesture.TryParse(input)!.ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]          // tuş yok
    [InlineData("Ctrl+Alt+A+B")]      // iki tuş
    [InlineData("Ctrl+Space")]        // desteklenmeyen tuş
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F")]            // "F" harfi olarak geçerli olmalı, "F" + sayı değil
    public void RejectsOrHandlesInvalid(string? input)
    {
        var gesture = HotkeyGesture.TryParse(input);
        if (input == "Ctrl+F")
            Assert.Equal("Ctrl+F", gesture!.ToString());
        else
            Assert.Null(gesture);
    }

    [Fact]
    public void BrokenSettingFallsBackToDefault()
    {
        Assert.Equal(HotkeyGesture.Default, HotkeyGesture.FromSettings("saçma"));
        Assert.Equal(HotkeyGesture.Default, HotkeyGesture.FromSettings(null));
        Assert.Equal("Ctrl+Alt+A", HotkeyGesture.Default.ToString());
    }

    [Theory]
    [InlineData("A")]                 // değiştirici yok
    [InlineData("Shift+A")]           // büyük harf yazmayı engeller
    [InlineData("F9")]
    [InlineData("Ctrl+C")]            // kopyala
    [InlineData("Alt+F4")]
    [InlineData("Win+L")]
    public void RejectsUnsafeCombinations(string input) =>
        Assert.NotNull(HotkeyGesture.TryParse(input)!.Validate());

    [Theory]
    [InlineData("Ctrl+Alt+A")]
    [InlineData("Ctrl+Shift+L")]
    [InlineData("Win+Alt+K")]
    [InlineData("Alt+F9")]
    public void AcceptsSafeCombinations(string input) =>
        Assert.Null(HotkeyGesture.TryParse(input)!.Validate());

    [Fact]
    public void WarnsAboutAltGrCharactersOnTurkishKeyboard()
    {
        Assert.Contains("@", HotkeyGesture.TryParse("Ctrl+Alt+Q")!.Warning());
        Assert.Contains("{", HotkeyGesture.TryParse("Ctrl+Alt+7")!.Warning());
        Assert.Null(HotkeyGesture.TryParse("Ctrl+Alt+A")!.Warning());
        Assert.Null(HotkeyGesture.TryParse("Ctrl+Shift+Alt+Q")!.Warning());   // Shift eklenince AltGr değil
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", 0x3u, 0x41u)]
    [InlineData("Ctrl+Alt+7", 0x3u, 0x37u)]
    [InlineData("Shift+Win+F12", 0xCu, 0x7Bu)]
    [InlineData("Alt+F1", 0x1u, 0x70u)]
    public void MapsToWin32Codes(string input, uint modifiers, uint virtualKey)
    {
        var gesture = HotkeyGesture.TryParse(input)!;
        Assert.Equal(modifiers, gesture.Win32Modifiers);
        Assert.Equal(virtualKey, gesture.Win32VirtualKey);
    }
}

public sealed class HotkeySettingsTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<VaultViewModel> UnlockAsync(MainWindowViewModel main)
    {
        var unlock = Assert.IsType<UnlockViewModel>(main.CurrentPage);
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        return Assert.IsType<VaultViewModel>(main.CurrentPage);
    }

    [AvaloniaFact]
    public async Task RecordingValidatesAndSavesHotkey()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(new MainWindowViewModel(_env.Services));
        vault.OpenSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(vault.Overlay);
        Assert.Equal("Ctrl+Alt+A", settings.HotkeyDisplay);
        Assert.True(settings.IsDefaultHotkey);

        settings.BeginHotkeyRecording();
        Assert.Equal("Kısayola basın…", settings.HotkeyDisplay);

        // Yalnızca değiştiriciler: önizleme, kayıt sürer
        Assert.False(settings.RecordHotkeyKey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "LeftShift"));
        Assert.Equal("Ctrl+Shift+…", settings.HotkeyDisplay);

        // Yasak birleşim: hata gösterilir, kayıt sürer
        Assert.True(settings.RecordHotkeyKey(HotkeyModifiers.Ctrl, "C"));
        Assert.True(settings.IsRecordingHotkey);
        Assert.True(settings.HotkeyMessageIsError);

        // Geçerli birleşim
        Assert.True(settings.RecordHotkeyKey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "K"));
        Assert.False(settings.IsRecordingHotkey);
        Assert.Equal("Ctrl+Shift+K", settings.HotkeyDisplay);
        Assert.False(settings.IsDefaultHotkey);
        Assert.Null(settings.HotkeyMessage);

        settings.SaveCommand.Execute(null);
        Assert.Equal("Ctrl+Shift+K", Services.SettingsStore.Load(_env.Services.SettingsStore.FilePath).Current.AutoTypeHotkey);

        // Yeni açılan ayrıntı ve düzenleyici seçilen kısayolu gösterir
        vault.SelectedItem = vault.Items.First();
        Assert.Equal("Auto-Type (Ctrl+Shift+K)", vault.Detail!.AutoTypeLabel);
        vault.EditEntryCommand.Execute(null);
        Assert.Contains("Ctrl+Shift+K", vault.Editor!.AutoTypeHint);
    }

    [AvaloniaFact]
    public async Task ResetAndCancelLeaveSettingsConsistent()
    {
        _env.Services.Settings.AutoTypeHotkey = "Ctrl+Alt+Q";
        _env.SeedVault();
        var vault = await UnlockAsync(new MainWindowViewModel(_env.Services));
        vault.OpenSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(vault.Overlay);
        Assert.Contains("@", settings.HotkeyMessage);   // AltGr uyarısı açılışta görünür

        settings.ResetHotkeyCommand.Execute(null);
        Assert.True(settings.IsDefaultHotkey);
        Assert.Null(settings.HotkeyMessage);

        // Kaydetmeden kapatınca ayar değişmez
        settings.CancelCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+Q", _env.Services.Settings.AutoTypeHotkey);
    }

    [AvaloniaFact]
    public async Task RealKeyPressesAreRecordedAndEscapeOnlyCancelsRecording()
    {
        if (!OperatingSystem.IsWindows())
            return;   // Auto-Type (ve kısayol satırı) yalnızca Windows'ta görünür
        _env.SeedVault(TestEnvironment.SampleEntries());
        var main = new MainWindowViewModel(_env.Services);
        var window = new MainWindow { DataContext = main, Width = 1120, Height = 720 };
        window.Show();
        var vault = await UnlockAsync(main);
        vault.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var settings = Assert.IsType<SettingsViewModel>(vault.Overlay);
        var box = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HotkeyBox");
        box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.IsRecordingHotkey);

        // Esc yalnızca kaydı iptal eder; ayarlar penceresi (kasa ekranının Esc kısayolu) kapanmaz
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsRecordingHotkey);
        Assert.Same(settings, vault.Overlay);

        box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Kasa ekranının kendi kısayolu (Ctrl+L = kilitle) kayıt sırasında çalışmaz, kısayol olarak kaydedilir
        window.KeyPress(Key.L, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.L, "L");
        Dispatcher.UIThread.RunJobs();
        Assert.True(_env.Services.Vault.IsUnlocked);
        Assert.Equal("Ctrl+Shift+L", settings.AutoTypeHotkey.ToString());

        box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        Assert.Equal("Ctrl+Alt+…", settings.HotkeyDisplay);
        window.KeyPress(Key.K, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsRecordingHotkey);
        Assert.Equal("Ctrl+Alt+K", settings.AutoTypeHotkey.ToString());
        window.Close();
    }
}
