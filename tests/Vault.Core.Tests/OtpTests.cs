using System.Text;
using Vault.Core.Models;
using Vault.Core.Otp;

namespace Vault.Core.Tests;

public class Base32Tests
{
    // RFC 4648 §10
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Rfc4648Vectors(string plain, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));
        Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded)));
    }

    [Fact]
    public void DecodeIsTolerantToSpacesCaseAndMissingPadding()
    {
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode("mzxw 6ytb-oi")));
    }

    [Theory]
    [InlineData("MZXW1")]      // '1' alfabede yok
    [InlineData("MZ=XW")]      // dolgudan sonra karakter
    public void DecodeRejectsInvalidInput(string input)
    {
        Assert.False(Base32.TryDecode(input, out _));
    }
}

public class OtpGeneratorTests
{
    private static readonly byte[] Rfc4226Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 4226 Ek D
    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void HotpRfc4226Vectors(long counter, string expected)
    {
        Assert.Equal(expected, OtpGenerator.ComputeHotp(Rfc4226Secret, counter, 6, OtpHashAlgorithm.SHA1));
    }

    // RFC 6238 Ek B
    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void TotpRfc6238Vectors(long unixTime, string sha1, string sha256, string sha512)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(unixTime);

        Assert.Equal(sha1, Totp("12345678901234567890", OtpHashAlgorithm.SHA1, time));
        Assert.Equal(sha256, Totp("12345678901234567890123456789012", OtpHashAlgorithm.SHA256, time));
        Assert.Equal(sha512, Totp("1234567890123456789012345678901234567890123456789012345678901234",
            OtpHashAlgorithm.SHA512, time));
    }

    [Fact]
    public void RemainingSecondsCountsDownWithinPeriod()
    {
        var settings = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP" };

        Assert.Equal(30, OtpGenerator.ComputeTotp(settings, DateTimeOffset.FromUnixTimeSeconds(60)).RemainingSeconds);
        Assert.Equal(1, OtpGenerator.ComputeTotp(settings, DateTimeOffset.FromUnixTimeSeconds(89)).RemainingSeconds);
    }

    [Fact]
    public void CodesAreZeroPadded()
    {
        var settings = new TotpSettings { Secret = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890")), Digits = 8 };
        Assert.Equal("07081804", OtpGenerator.ComputeTotp(settings, DateTimeOffset.FromUnixTimeSeconds(1111111109)).Code);
    }

    [Fact]
    public void InvalidSecretThrows()
    {
        Assert.Throws<ArgumentException>(() => OtpGenerator.ComputeTotp(new TotpSettings { Secret = "not-base32!" }));
    }

    private static string Totp(string asciiSecret, OtpHashAlgorithm alg, DateTimeOffset time)
    {
        var settings = new TotpSettings
        {
            Secret = Base32.Encode(Encoding.ASCII.GetBytes(asciiSecret)),
            Digits = 8,
            Period = 30,
            Algorithm = alg,
        };
        return OtpGenerator.ComputeTotp(settings, time).Code;
    }
}

public class OtpAuthUriTests
{
    [Fact]
    public void ParsesFullUri()
    {
        var info = OtpAuthUri.Parse(
            "otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA256&digits=8&period=60");

        Assert.Equal("ACME Co", info.Issuer);
        Assert.Equal("john.doe@email.com", info.Account);
        Assert.Equal("HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ", info.Settings.Secret);
        Assert.Equal(OtpHashAlgorithm.SHA256, info.Settings.Algorithm);
        Assert.Equal(8, info.Settings.Digits);
        Assert.Equal(60, info.Settings.Period);
    }

    [Fact]
    public void AppliesDefaultsAndLabelIssuer()
    {
        var info = OtpAuthUri.Parse("otpauth://totp/GitHub:ahmet?secret=jbsw%20y3dp%20ehpk%203pxp");

        Assert.Equal("GitHub", info.Issuer);
        Assert.Equal("ahmet", info.Account);
        Assert.Equal("JBSWY3DPEHPK3PXP", info.Settings.Secret);
        Assert.Equal(OtpHashAlgorithm.SHA1, info.Settings.Algorithm);
        Assert.Equal(6, info.Settings.Digits);
        Assert.Equal(30, info.Settings.Period);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("otpauth://totp/x")]                              // secret yok
    [InlineData("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP&counter=1")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=3")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    public void RejectsInvalidUris(string uri)
    {
        Assert.Throws<FormatException>(() => OtpAuthUri.Parse(uri));
    }

    [Fact]
    public void BuildRoundTrips()
    {
        var settings = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP", Digits = 8, Algorithm = OtpHashAlgorithm.SHA512 };
        var info = OtpAuthUri.Parse(OtpAuthUri.Build(settings, "Şirket A.Ş.", "ahmet@x.com"));

        Assert.Equal("Şirket A.Ş.", info.Issuer);
        Assert.Equal("ahmet@x.com", info.Account);
        Assert.Equal(8, info.Settings.Digits);
        Assert.Equal(OtpHashAlgorithm.SHA512, info.Settings.Algorithm);
    }
}
