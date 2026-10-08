using Vault.Core.Generation;

namespace Vault.Core.Tests;

public class PasswordGeneratorTests
{
    [Fact]
    public void GeneratesRequestedLengthWithEveryGroup()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate(new PasswordOptions { Length = 8 });

            Assert.Equal(8, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void RespectsDisabledGroupsAndAmbiguousExclusion()
    {
        var options = new PasswordOptions { Length = 200, Symbols = false, Uppercase = false, ExcludeAmbiguous = true };
        var password = PasswordGenerator.Generate(options);

        Assert.All(password, c => Assert.True(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));
        Assert.DoesNotContain('0', password);
        Assert.DoesNotContain('1', password);
        Assert.DoesNotContain('l', password);
    }

    [Fact]
    public void ProducesDifferentPasswords()
    {
        var set = Enumerable.Range(0, 50).Select(_ => PasswordGenerator.Generate(new PasswordOptions())).ToHashSet();
        Assert.Equal(50, set.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(513)]
    public void RejectsInvalidLength(int length)
    {
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordOptions { Length = length }));
    }

    [Fact]
    public void RejectsNoGroups()
    {
        var options = new PasswordOptions { Uppercase = false, Lowercase = false, Digits = false, Symbols = false };
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(options));
    }

    [Theory]
    [InlineData("", StrengthLevel.VeryWeak)]
    [InlineData("Parola123", StrengthLevel.VeryWeak)]          // yaygın kelime
    [InlineData("aaaaaaaaaaaaaaaa", StrengthLevel.VeryWeak)]   // tekrar
    [InlineData("abcdefghijklmnop", StrengthLevel.VeryWeak)]   // düz dizi
    [InlineData("kx7mq2", StrengthLevel.Weak)]
    [InlineData("Kx7mQ2pz", StrengthLevel.Fair)]
    [InlineData("Kx7#mQ2p!zR9", StrengthLevel.Strong)]
    [InlineData("Kx7#mQ2p!zR9vT4&wY8n", StrengthLevel.VeryStrong)]
    public void StrengthLevels(string password, StrengthLevel expected)
    {
        Assert.Equal(expected, PasswordStrength.Estimate(password).Level);
    }

    [Fact]
    public void GeneratedPasswordsAreVeryStrong()
    {
        Assert.Equal(StrengthLevel.VeryStrong, PasswordStrength.Estimate(PasswordGenerator.Generate(new PasswordOptions())).Level);
    }

    [Fact]
    public void EstimatesEntropy()
    {
        var bits = PasswordGenerator.EstimateEntropyBits(new PasswordOptions { Length = 20, Symbols = false, Uppercase = false, Lowercase = false });
        Assert.Equal(20 * Math.Log2(10), bits, precision: 6);
    }
}
