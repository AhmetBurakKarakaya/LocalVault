using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Headless.XUnit;
using Vault.Core.AutoType;
using Vault.Core.Models;
using Vault.Core.Otp;
using Vault.Core.Storage;
using Vault.Desktop.Platform;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;
using ZXing;
using ZXing.QrCode;

namespace Vault.Desktop.Tests;

[SupportedOSPlatform("windows")]
public class Win32InputTests
{
    private static (ushort Vk, ushort Scan, uint Flags) Key(Win32Input.Input input) =>
        (input.Union.Keyboard.VirtualKey, input.Union.Keyboard.ScanCode, input.Union.Keyboard.Flags);

    [Fact]
    public void TextIsSentAsUnicodeKeyDownUpPairs()
    {
        var steps = Win32Input.Build([new AutoTypeAction.Text("aŞ")]);

        Assert.Equal(2, steps.Count);
        Assert.Equal([(0, 'a', 0x4u), (0, 'a', 0x6u)], steps[0].Inputs!.Select(Key));
        Assert.Equal([(0, 'Ş', 0x4u), (0, 'Ş', 0x6u)], steps[1].Inputs!.Select(Key));
        Assert.All(steps.SelectMany(s => s.Inputs!), i => Assert.Equal(1u, i.Type));
    }

    [Fact]
    public void SpecialKeysUseVirtualKeysAndExtendedFlag()
    {
        var steps = Win32Input.Build([new AutoTypeAction.Key(AutoTypeKey.Tab, 2), new AutoTypeAction.Key(AutoTypeKey.Left)]);

        Assert.Equal(3, steps.Count);
        Assert.Equal([(0x09, 0, 0u), (0x09, 0, 2u)], steps[0].Inputs!.Select(Key));
        Assert.Equal([(0x25, 0, 1u), (0x25, 0, 3u)], steps[2].Inputs!.Select(Key));   // ok tuşu: extended
    }

    [Fact]
    public void NewlinesBecomeEnterAndDelaysAreSeparateSteps()
    {
        var steps = Win32Input.Build([new AutoTypeAction.Text("a\r\nb\t"), new AutoTypeAction.Delay(300)]);

        Assert.Equal(5, steps.Count);
        Assert.Equal(0x0D, steps[1].Inputs![0].Union.Keyboard.VirtualKey);
        Assert.Equal(0x09, steps[3].Inputs![0].Union.Keyboard.VirtualKey);
        Assert.Null(steps[4].Inputs);
        Assert.Equal(300, steps[4].DelayMs);
    }

    [Fact]
    public void SurrogatePairsAreSentAsTwoUnicodeUnits()
    {
        var steps = Win32Input.Build([new AutoTypeAction.Text("🔑")]);
        Assert.Equal(2, steps.Count);
        Assert.Equal("🔑", new string(steps.Select(s => (char)s.Inputs![0].Union.Keyboard.ScanCode).ToArray()));
    }

    [Fact]
    public void FunctionKeysMapToVkF1ToF12()
    {
        Assert.Equal((ushort)0x70, Win32Input.VirtualKey(AutoTypeKey.F1).Vk);
        Assert.Equal((ushort)0x7B, Win32Input.VirtualKey(AutoTypeKey.F12).Vk);
    }

    [Fact]
    public void InputStructHasNativeSize()
    {
        // SendInput, cbSize yanlışsa hiçbir şey göndermez: x64'te 40, x86'da 28 bayt olmalı.
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf<Win32Input.Input>());
    }
}

[SupportedOSPlatform("windows")]
public class DibTests
{
    private static byte[] Dib(int width, int height, int bitCount, bool topDown, Func<int, int, (byte B, byte G, byte R)> pixel)
    {
        var bpp = bitCount / 8;
        var stride = (width * bpp + 3) & ~3;
        var data = new byte[40 + stride * height];
        BinaryPrimitives.WriteInt32LittleEndian(data, 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), topDown ? -height : height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)bitCount);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = topDown ? y : height - 1 - y;
            var (b, g, r) = pixel(x, y);
            var i = 40 + row * stride + x * bpp;
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
        }
        return data;
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(32, true)]
    [InlineData(32, false)]
    public void ConvertsToTopDownBgra(int bitCount, bool topDown)
    {
        var dib = Dib(3, 2, bitCount, topDown, (x, y) => ((byte)(x * 10), (byte)(y * 10), 7));
        var image = ScreenCapture.FromDib(dib)!;

        Assert.Equal((3, 2), (image.Width, image.Height));
        var i = (1 * 3 + 2) * 4;   // satır 1, sütun 2
        Assert.Equal([20, 10, 7, 255], image.Pixels[i..(i + 4)]);
    }

    [Fact]
    public void RejectsUnsupportedFormats()
    {
        Assert.Null(ScreenCapture.FromDib(new byte[10]));
        Assert.Null(ScreenCapture.FromDib(Dib(2, 2, 32, true, (_, _) => (0, 0, 0))[..44]));   // kesik
    }
}

public sealed class Phase5FlowTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<VaultViewModel> OpenVaultAsync(params VaultEntry[] entries)
    {
        _env.SeedVault(entries);
        var main = new MainWindowViewModel(_env.Services);
        var unlock = (UnlockViewModel)main.CurrentPage;
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        return (VaultViewModel)main.CurrentPage;
    }

    /// <summary>Verilen metni içeren QR kodlu bir "ekran görüntüsü".</summary>
    internal static CapturedImage QrImage(string text)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0);
        const int scale = 4, margin = 40;
        int width = matrix.Width * scale + margin * 2, height = matrix.Height * scale + margin * 2;
        var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        for (var y = 0; y < matrix.Height * scale; y++)
        for (var x = 0; x < matrix.Width * scale; x++)
        {
            if (!matrix[x / scale, y / scale])
                continue;
            var i = ((y + margin) * width + x + margin) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
        }
        return new CapturedImage(pixels, width, height);
    }

    private static string MigrationUri(params (string Issuer, string Account)[] accounts)
    {
        static byte[] V(long v) { var b = new List<byte>(); do { var x = (byte)(v & 0x7F); v >>= 7; b.Add(v != 0 ? (byte)(x | 0x80) : x); } while (v != 0); return [.. b]; }
        static byte[] F(int n, byte[] value) => [.. V(n << 3 | 2), .. V(value.Length), .. value];
        static byte[] I(int n, long value) => [.. V(n << 3), .. V(value)];
        var payload = accounts.SelectMany(a => F(1,
        [
            .. F(1, Base32.Decode("JBSWY3DPEHPK3PXP")), .. F(2, Encoding.UTF8.GetBytes(a.Account)),
            .. F(3, Encoding.UTF8.GetBytes(a.Issuer)), .. I(4, 1), .. I(5, 1), .. I(6, 2),
        ])).ToArray();
        return "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));
    }

    [AvaloniaFact]
    public async Task EditorAutoTypeFieldsAreValidatedAndSaved()
    {
        var vault = await OpenVaultAsync();
        vault.NewEntryCommand.Execute(null);
        var editor = vault.Editor!;
        editor.Title = "Sunucu";
        editor.Username = "admin";
        editor.AutoTypeWindowsText = "*Uzak Masaüstü*\r\n\r\n  PuTTY  ";

        editor.AutoTypeSequence = "{USERNAME}{TAB";
        Assert.NotNull(editor.AutoTypeError);
        editor.SaveCommand.Execute(null);
        Assert.Contains("Auto-Type", editor.ErrorMessage);

        editor.AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}{TOTP}";
        editor.SaveCommand.Execute(null);
        Assert.Contains("{TOTP}", editor.ErrorMessage);

        editor.AutoTypeSequence = "{USERNAME}{ENTER}{DELAY 500}{PASSWORD}{ENTER}";
        Assert.Null(editor.AutoTypeError);
        editor.SaveCommand.Execute(null);
        Assert.Null(vault.Editor);

        var saved = vault.Detail!.Entry;
        Assert.Equal(["*Uzak Masaüstü*", "PuTTY"], saved.AutoTypeWindows);
        Assert.Equal("{USERNAME}{ENTER}{DELAY 500}{PASSWORD}{ENTER}", saved.AutoTypeSequence);
        Assert.True(vault.Detail.HasAutoType);
    }

    [AvaloniaFact]
    public async Task ScanningSingleQrFillsTotpFields()
    {
        var vault = await OpenVaultAsync();
        vault.NewEntryCommand.Execute(null);
        var editor = vault.Editor!;
        _env.Platform.NextImage = QrImage("otpauth://totp/GitLab:ahmet@example.com?secret=GEZDGNBVGY3TQOJQ&issuer=GitLab&digits=8&algorithm=SHA256");

        await editor.ScanQrCommand.ExecuteAsync(QrSource.Clipboard);

        Assert.Equal("GEZDGNBVGY3TQOJQ", editor.TotpSecret);
        Assert.Equal(8, editor.TotpDigits);
        Assert.Equal(OtpHashAlgorithm.SHA256, editor.TotpAlgorithm);
        Assert.Equal("GitLab", editor.Title);
        Assert.Equal("ahmet@example.com", editor.Username);
        Assert.True(editor.ShowTotpAdvanced);
        Assert.Null(editor.TotpError);
        Assert.NotEmpty(editor.TotpPreview);
    }

    [AvaloniaFact]
    public async Task ScanningWithoutQrShowsError()
    {
        var vault = await OpenVaultAsync();
        vault.NewEntryCommand.Execute(null);
        _env.Platform.NextImage = new CapturedImage(Enumerable.Repeat((byte)200, 100 * 100 * 4).ToArray(), 100, 100);

        await vault.Editor!.ScanQrCommand.ExecuteAsync(QrSource.Screen);
        Assert.Contains("QR kodu bulunamadı", vault.Editor.TotpError);

        _env.Platform.NextImage = null;
        await vault.Editor.ScanQrCommand.ExecuteAsync(QrSource.Clipboard);
        Assert.Contains("Panoda görüntü yok", vault.Editor.TotpError);
    }

    [AvaloniaFact]
    public async Task GoogleAuthenticatorExportImportsAllAccounts()
    {
        var vault = await OpenVaultAsync(new VaultEntry { Title = "GitHub", Username = "ahmet" });
        _env.Platform.NextImage = QrImage(MigrationUri(("GitHub", "ahmet"), ("Dropbox", "a@b.com"), ("AWS", "root")));

        await vault.ImportQrCommand.ExecuteAsync(QrSource.Screen);

        var preview = Assert.IsType<ImportPreviewViewModel>(vault.Overlay);
        Assert.Contains("2 yeni kayıt", preview.Summary);
        Assert.Contains("1 kayıt zaten kasada", preview.Summary);
        preview.ImportCommand.Execute(null);

        Assert.Null(vault.Overlay);
        Assert.Equal(3, vault.Items.Count);
        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        Assert.Equal("JBSWY3DPEHPK3PXP", session.Data.Entries.Single(e => e.Title == "Dropbox").Totp!.Secret);
    }

    [AvaloniaFact]
    public async Task CsvImportPreviewsAndAddsNewEntries()
    {
        var vault = await OpenVaultAsync(new VaultEntry { Title = "GitHub", Username = "ahmet", Urls = ["https://github.com"] });
        var csv = Path.Combine(_env.Directory, "bitwarden.csv");
        await File.WriteAllTextAsync(csv, """
            folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp
            ,,login,GitHub,,,0,https://github.com,ahmet,x,
            İş,,login,Jira,,,0,https://jira.firma.com,ahmet.k,j-pass,
            ,,card,Kart,,,0,,,,
            """);
        _env.Platform.NextFile = csv;

        await vault.ImportCsvCommand.ExecuteAsync(null);

        var preview = Assert.IsType<ImportPreviewViewModel>(vault.Overlay);
        Assert.StartsWith("Bitwarden", preview.Source);
        Assert.True(preview.PlaintextFile);
        Assert.Equal("1 yeni kayıt · 1 kayıt zaten kasada (atlanacak) · 1 satır giriş kaydı değil (atlanacak)", preview.Summary);
        preview.ImportCommand.Execute(null);

        Assert.Equal(["GitHub", "Jira"], vault.Items.Select(i => i.Title));
        Assert.Equal(["İş"], vault.Items.Single(i => i.Title == "Jira").Entry.Tags);
    }

    [AvaloniaFact]
    public async Task InvalidCsvShowsErrorStatus()
    {
        var vault = await OpenVaultAsync();
        var csv = Path.Combine(_env.Directory, "bozuk.csv");
        await File.WriteAllTextAsync(csv, "ad,soyad\nA,B\n");
        _env.Platform.NextFile = csv;

        await vault.ImportCsvCommand.ExecuteAsync(null);

        Assert.Null(vault.Overlay);
        Assert.True(vault.IsStatusError);
        Assert.Contains("CSV okunamadı", vault.StatusMessage);
    }

    [AvaloniaFact]
    public async Task AutoTypeChooserReturnsSelectionOrNull()
    {
        var rdp = new VaultEntry { Title = "RDP", Username = "admin", AutoTypeWindows = ["*Uzak Masaüstü*"] };
        var vpn = new VaultEntry { Title = "VPN", Username = "ahmet" };
        var vault = await OpenVaultAsync(rdp, vpn);

        // Eşleşme yok: tüm kayıtlar listelenir, "hatırla" işaretli gelir.
        var pending = vault.RequestAutoTypeChoiceAsync("OpenVPN Connect", []);
        var chooser = Assert.IsType<AutoTypeChooserViewModel>(vault.Overlay);
        Assert.False(chooser.HasMatches);
        Assert.True(chooser.RememberWindow);
        Assert.Equal(2, chooser.Items.Count);
        chooser.SearchText = "vpn";
        Assert.Equal("VPN", Assert.Single(chooser.Items).Title);
        chooser.ConfirmCommand.Execute(null);

        var choice = await pending;
        Assert.Equal("VPN", choice!.Entry.Title);
        Assert.True(choice.RememberWindow);
        Assert.Null(vault.Overlay);

        // Esc ile kapatmak: vazgeçildi
        pending = vault.RequestAutoTypeChoiceAsync("x", [rdp, vpn]);
        Assert.False(((AutoTypeChooserViewModel)vault.Overlay!).RememberWindow);
        vault.EscapeCommand.Execute(null);
        Assert.Null(await pending);
    }

    [AvaloniaFact]
    public async Task ChangesFromBrowserRefreshTheList()
    {
        var vault = await OpenVaultAsync(new VaultEntry { Title = "A" });
        var access = new DesktopVaultAccess(_env.Services.Vault);

        access.Data.Entries.Add(new VaultEntry { Title = "Tarayıcıdan" });
        access.Save();

        Assert.Contains(vault.Items, i => i.Title == "Tarayıcıdan");
    }
}

[SupportedOSPlatform("windows")]
public class GlobalHotkeyTests
{
    [Fact]
    public void RegistersAndDetectsConflicts()
    {
        // Alışılmadık bir kombinasyon (Ctrl+Alt+Shift+F11): tuş gönderilmez, yalnızca kayıt denenir.
        const uint mods = GlobalHotkey.ModControl | GlobalHotkey.ModAlt | GlobalHotkey.ModShift;
        using var first = new GlobalHotkey(mods, 0x7A);
        Assert.True(first.IsRegistered);

        using var second = new GlobalHotkey(mods, 0x7A);
        Assert.False(second.IsRegistered);   // aynı kısayol başka biri tarafından alınmış
    }
}
