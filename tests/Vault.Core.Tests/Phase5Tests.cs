using System.Text;
using Vault.Core.AutoType;
using Vault.Core.Import;
using Vault.Core.Models;
using Vault.Core.Otp;
using ZXing;
using ZXing.QrCode;
using FormatException = System.FormatException;

namespace Vault.Core.Tests;

public class AutoTypeSequenceTests
{
    private static readonly VaultEntry Entry = new()
    {
        Title = "Sunucu", Username = "ahmet", Password = "p{a}ss Ş", Urls = ["https://ornek.com"],
        Totp = new TotpSettings { Secret = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890")) },
    };

    [Fact]
    public void DefaultSequenceTypesUsernameTabPasswordEnter()
    {
        var actions = AutoTypeSequence.Compile(Entry);
        Assert.Equal(
        [
            new AutoTypeAction.Text("ahmet"),
            new AutoTypeAction.Key(AutoTypeKey.Tab),
            new AutoTypeAction.Text("p{a}ss Ş"),
            new AutoTypeAction.Key(AutoTypeKey.Enter),
        ], actions);
    }

    [Fact]
    public void SupportsRepeatDelayLiteralsAndTotp()
    {
        var entry = new VaultEntry
        {
            Username = "u", Password = "p", Totp = Entry.Totp,
            AutoTypeSequence = "{USERNAME}{TAB 2}{DELAY 250}x{{}y{}}{password}{ENTER}{TOTP}{enter}",
        };
        var time = DateTimeOffset.FromUnixTimeSeconds(59);
        var actions = AutoTypeSequence.Compile(entry, time);

        Assert.Equal(
        [
            new AutoTypeAction.Text("u"),
            new AutoTypeAction.Key(AutoTypeKey.Tab, 2),
            new AutoTypeAction.Delay(250),
            new AutoTypeAction.Text("x{y}p"),
            new AutoTypeAction.Key(AutoTypeKey.Enter),
            new AutoTypeAction.Text("287082"),   // RFC 6238 vektörü, 6 hane
            new AutoTypeAction.Key(AutoTypeKey.Enter),
        ], actions);
    }

    [Theory]
    [InlineData("{USERNAME")]
    [InlineData("abc}")]
    [InlineData("{BILINMEYEN}")]
    [InlineData("{TAB 0}")]
    [InlineData("{TAB 999}")]
    [InlineData("{DELAY}")]
    [InlineData("{DELAY 99999}")]
    [InlineData("{USERNAME 2}")]
    public void InvalidSequencesAreRejected(string sequence)
    {
        Assert.NotNull(AutoTypeSequence.Validate(sequence));
    }

    [Fact]
    public void TotpWithoutSecretIsAnError()
    {
        var entry = new VaultEntry { Username = "u", AutoTypeSequence = "{USERNAME}{TOTP}" };
        Assert.Throws<FormatException>(() => AutoTypeSequence.Compile(entry));
    }

    [Fact]
    public void PasswordIsNotLeakedThroughToString()
    {
        Assert.DoesNotContain("gizli", new AutoTypeAction.Text("gizli-parola").ToString());
    }
}

public class WindowMatcherTests
{
    [Theory]
    [InlineData("*Uzak Masaüstü*", "10.0.0.5 - Uzak Masaüstü Bağlantısı", true)]
    [InlineData("*uzak masaüstü*", "10.0.0.5 - UZAK MASAÜSTÜ Bağlantısı", true)]
    [InlineData("OpenVPN*", "OpenVPN Connect", true)]
    [InlineData("OpenVPN*", "Bağlan - OpenVPN", false)]
    [InlineData("Putty", "sunucu - PuTTY", true)]               // jokersiz: içeriyorsa
    [InlineData("Login ?", "Login 2", true)]
    [InlineData("Login ?", "Login 22", false)]
    [InlineData("a.b", "axb", false)]                            // nokta düzenli ifade olarak yorumlanmaz
    [InlineData("", "her şey", false)]
    public void MatchesWithWildcards(string pattern, string title, bool expected)
    {
        Assert.Equal(expected, WindowMatcher.Matches(pattern, title));
    }

    [Fact]
    public void FindMatchesReturnsSortedEntries()
    {
        var entries = new[]
        {
            new VaultEntry { Title = "VPN", AutoTypeWindows = ["OpenVPN*"] },
            new VaultEntry { Title = "RDP iş", AutoTypeWindows = ["*Uzak Masaüstü*", "*Remote Desktop*"] },
            new VaultEntry { Title = "Atanmamış" },
            new VaultEntry { Title = "RDP ev", AutoTypeWindows = ["*Remote Desktop*"] },
        };
        Assert.Equal(["RDP ev", "RDP iş"], WindowMatcher.FindMatches(entries, "srv01 - Remote Desktop Connection").Select(e => e.Title));
    }
}

public class CsvImportTests
{
    [Fact]
    public void ReaderHandlesQuotesNewlinesBomAndSemicolons()
    {
        var rows = CsvReader.Parse("﻿a;\"b;c\";\"satır\r\niki\";\"tırnak \"\"x\"\"\"\r\n\r\n1;;3;\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b;c", "satır\r\niki", "tırnak \"x\""], rows[0]);
        Assert.Equal(["1", "", "3", ""], rows[1]);
    }

    [Fact]
    public void UnterminatedQuoteThrows()
    {
        Assert.Throws<FormatException>(() => CsvReader.Parse("a,\"b\n"));
    }

    [Fact]
    public void ImportsKeePassXC()
    {
        const string csv = """
            "Group","Title","Username","Password","URL","Notes","TOTP","Icon","Last Modified","Created"
            "Root/İş","GitHub","ahmet","p@ss,1","https://github.com","not
            ikinci satır","otpauth://totp/GitHub:ahmet?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&digits=8","0","2024-01-01","2024-01-01"
            "Root","Wi-Fi","","ev-parola","","","","0","",""
            """;
        var result = CsvImporter.Import(csv);

        Assert.Equal(CsvFormat.KeePassXC, result.Format);
        Assert.Equal(2, result.Entries.Count);
        var github = result.Entries[0];
        Assert.Equal("p@ss,1", github.Password);
        Assert.Equal(["https://github.com"], github.Urls);
        Assert.Equal(["İş"], github.Tags);
        Assert.Equal(8, github.Totp!.Digits);
        Assert.Contains("ikinci satır", github.Notes);
        Assert.Empty(result.Entries[1].Tags);   // "Root" etiket olmaz
    }

    [Fact]
    public void ImportsBitwardenLoginsOnly()
    {
        const string csv = """
            folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp
            Bankalar,1,login,Banka,,,0,"https://internet.bank.com.tr,androidapp://com.bank",12345678,Gizli!1,JBSW Y3DP EHPK 3PXP
            ,,card,Kredi kartı,,,0,,,,
            ,,login,,,,0,https://example.com,user@example.com,pw,steam://ABCDEF
            """;
        var result = CsvImporter.Import(csv);

        Assert.Equal(CsvFormat.Bitwarden, result.Format);
        Assert.Equal(1, result.SkippedRows);   // kart atlandı
        var bank = result.Entries[0];
        Assert.Equal(["https://internet.bank.com.tr"], bank.Urls);   // androidapp:// atlandı
        Assert.Equal(["Bankalar"], bank.Tags);
        Assert.Equal("JBSWY3DPEHPK3PXP", bank.Totp!.Secret);
        Assert.Equal("example.com", result.Entries[1].Title);       // başlık yoksa host
        Assert.Null(result.Entries[1].Totp);
        Assert.Contains(result.Warnings, w => w.Contains("Steam"));
    }

    [Fact]
    public void ImportsOnePassword()
    {
        const string csv = """
            Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes
            Gmail,https://accounts.google.com,ahmet@gmail.com,g-pass,,false,false,"kişisel,e-posta",
            AWS,https://console.aws.amazon.com,admin,aws-pass,otpauth://totp/AWS?secret=GEZDGNBVGY3TQOJQ,false,false,iş,kök hesap
            """;
        var result = CsvImporter.Import(csv);

        Assert.Equal(CsvFormat.OnePassword, result.Format);
        Assert.Equal(["kişisel", "e-posta"], result.Entries[0].Tags);
        Assert.Equal("GEZDGNBVGY3TQOJQ", result.Entries[1].Totp!.Secret);
        Assert.Equal("kök hesap", result.Entries[1].Notes);
    }

    [Fact]
    public void ImportsGenericCsvAndReportsInvalidTotp()
    {
        const string csv = "name;url;username;password;totp\nSite;site.com;u;p;geçersiz!\n";
        var result = CsvImporter.Import(csv);

        Assert.Equal(CsvFormat.Generic, result.Format);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(["site.com"], entry.Urls);
        Assert.Null(entry.Totp);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void RejectsCsvWithoutCredentialColumns()
    {
        Assert.Throws<FormatException>(() => CsvImporter.Import("ad,soyad\nAhmet,K\n"));
    }

    [Fact]
    public void SplitsDuplicatesAgainstVault()
    {
        var vault = new VaultData();
        vault.Entries.Add(new VaultEntry { Title = "GitHub", Username = "ahmet", Urls = ["https://github.com/login"] });
        var imported = new[]
        {
            new VaultEntry { Title = "github", Username = "AHMET", Urls = ["https://github.com"] },   // yineleme
            new VaultEntry { Title = "GitHub", Username = "başka", Urls = ["https://github.com"] },
            new VaultEntry { Title = "GitHub", Username = "başka", Urls = ["https://github.com"] },   // dosya içi yineleme
        };
        var (fresh, duplicates) = CsvImporter.SplitDuplicates(vault, imported);
        Assert.Single(fresh);
        Assert.Equal(2, duplicates.Count);
    }
}

public class OtpMigrationTests
{
    private static byte[] Varint(long value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        } while (value != 0);
        return [.. bytes];
    }

    private static byte[] Field(int number, byte[] value) => [.. Varint(number << 3 | 2), .. Varint(value.Length), .. value];
    private static byte[] Field(int number, long value) => [.. Varint(number << 3), .. Varint(value)];

    private static byte[] Account(byte[] secret, string name, string issuer, int algorithm, int digits, int type) =>
    [
        .. Field(1, secret), .. Field(2, Encoding.UTF8.GetBytes(name)), .. Field(3, Encoding.UTF8.GetBytes(issuer)),
        .. Field(4, algorithm), .. Field(5, digits), .. Field(6, type),
    ];

    [Fact]
    public void ParsesGoogleAuthenticatorExport()
    {
        byte[] payload =
        [
            .. Field(1, Account(Base32.Decode("JBSWY3DPEHPK3PXP"), "ahmet@example.com", "GitHub", 1, 1, 2)),
            .. Field(1, Account([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], "Şirket:ahmet", "", 2, 2, 2)),
            .. Field(1, Account([1, 2, 3], "sayac", "Eski", 1, 1, 1)),   // HOTP → uyarı
            .. Field(2, 1), .. Field(3, 1), .. Field(4, 0),
        ];
        var uri = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));

        var result = OtpMigration.Parse(uri);

        Assert.Equal(2, result.Accounts.Count);
        var github = result.Accounts[0];
        Assert.Equal("GitHub", github.Issuer);
        Assert.Equal("ahmet@example.com", github.Account);
        Assert.Equal("JBSWY3DPEHPK3PXP", github.Settings.Secret);
        Assert.Equal(6, github.Settings.Digits);

        var company = result.Accounts[1];
        Assert.Equal("Şirket", company.Issuer);    // "Issuer:hesap" adından ayrıştırıldı
        Assert.Equal("ahmet", company.Account);
        Assert.Equal(OtpHashAlgorithm.SHA256, company.Settings.Algorithm);
        Assert.Equal(8, company.Settings.Digits);

        Assert.Contains("HOTP", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData("otpauth://totp/x?secret=ABC")]
    [InlineData("otpauth-migration://offline")]
    [InlineData("otpauth-migration://offline?data=%%%")]
    [InlineData("otpauth-migration://offline?data=CgM")]   // kesik protobuf
    public void RejectsInvalidInput(string uri)
    {
        Assert.Throws<FormatException>(() => OtpMigration.Parse(uri));
    }
}

public class QrDecoderTests
{
    /// <summary>QR kodunu, gürültülü büyük bir "ekran görüntüsünün" içine çizer (BGRA).</summary>
    private static (byte[] Pixels, int Width, int Height) RenderScreenshot(string text, int scale = 4)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0);
        const int width = 900, height = 600;
        var pixels = new byte[width * height * 4];
        var random = new Random(42);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var v = (byte)random.Next(170, 256);   // açık renkli, gürültülü arka plan
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }

        int offsetX = 520, offsetY = 140, quiet = 4 * scale;
        for (var y = -quiet; y < matrix.Height * scale + quiet; y++)
        for (var x = -quiet; x < matrix.Width * scale + quiet; x++)
        {
            var dark = x >= 0 && y >= 0 && x < matrix.Width * scale && y < matrix.Height * scale && matrix[x / scale, y / scale];
            var i = ((offsetY + y) * width + offsetX + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = dark ? (byte)20 : (byte)255;
        }
        return (pixels, width, height);
    }

    [Fact]
    public void DecodesOtpAuthQrFromScreenshot()
    {
        const string text = "otpauth://totp/GitHub:ahmet@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        var (pixels, width, height) = RenderScreenshot(text);

        var codes = QrDecoder.Decode(pixels, width, height, width * 4, PixelLayout.Bgra32);

        Assert.Equal(text, Assert.Single(codes));
        Assert.Equal("JBSWY3DPEHPK3PXP", OtpAuthUri.Parse(codes[0]).Settings.Secret);
    }

    [Fact]
    public void ReturnsEmptyWhenNoQr()
    {
        var pixels = Enumerable.Repeat((byte)200, 300 * 200 * 4).ToArray();
        Assert.Empty(QrDecoder.Decode(pixels, 300, 200, 300 * 4, PixelLayout.Rgba32));
    }

    [Fact]
    public void RejectsInvalidBufferSize()
    {
        Assert.Throws<ArgumentException>(() => QrDecoder.Decode(new byte[10], 10, 10, 40, PixelLayout.Bgra32));
    }
}
