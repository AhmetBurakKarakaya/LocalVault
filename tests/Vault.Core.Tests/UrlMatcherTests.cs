using Vault.Core.Matching;
using Vault.Core.Models;

namespace Vault.Core.Tests;

public class UrlMatcherTests
{
    [Theory]
    // Aynı host ve alt alan adları
    [InlineData("https://github.com", "https://github.com/login", MatchQuality.Host)]
    [InlineData("github.com", "https://github.com/login", MatchQuality.Host)]
    [InlineData("https://github.com", "https://gist.github.com/", MatchQuality.Subdomain)]
    [InlineData("https://GitHub.COM", "https://github.com", MatchQuality.Host)]
    [InlineData("https://www.example.com", "https://example.com", MatchQuality.Subdomain)]
    [InlineData("https://example.com", "https://www.example.com", MatchQuality.Subdomain)]
    [InlineData("https://example.com/app", "https://example.com/app/login", MatchQuality.PathPrefix)]
    // Oltalama / taklit alan adları — asla eşleşmemeli
    [InlineData("https://github.com", "https://github.com.evil.com/login", MatchQuality.None)]
    [InlineData("https://github.com", "https://evil-github.com", MatchQuality.None)]
    [InlineData("https://github.com", "https://evilgithub.com", MatchQuality.None)]
    [InlineData("https://github.com", "https://xn--gthub-n4a.com", MatchQuality.None)]
    [InlineData("https://bank.com.tr", "https://evil.com.tr", MatchQuality.None)]
    [InlineData("https://www.gov.tr", "https://evil.gov.tr", MatchQuality.None)]
    [InlineData("https://login.example.com", "https://example.com", MatchQuality.None)]
    [InlineData("https://login.example.com", "https://other.example.com", MatchQuality.None)]
    // HTTPS'ten HTTP'ye düşürme yok; tersi serbest
    [InlineData("https://example.com", "http://example.com", MatchQuality.None)]
    [InlineData("http://example.com", "https://example.com", MatchQuality.Host)]
    // Portlar
    [InlineData("http://localhost:3000", "http://localhost:3000/login", MatchQuality.Host)]
    [InlineData("http://localhost:3000", "http://localhost:4000/login", MatchQuality.None)]
    [InlineData("https://example.com", "https://example.com:8443", MatchQuality.Host)]
    // IP adresleri yalnızca birebir eşleşir
    [InlineData("https://10.0.0.1", "https://10.0.0.1/admin", MatchQuality.Host)]
    [InlineData("https://0.0.1", "https://10.0.0.1/admin", MatchQuality.None)]
    // Desteklenmeyen şemalar
    [InlineData("https://example.com", "file:///C:/example.com", MatchQuality.None)]
    [InlineData("https://example.com", "javascript:alert(1)", MatchQuality.None)]
    public void DomainMode(string entryUrl, string pageUrl, MatchQuality expected)
    {
        Assert.Equal(expected, UrlMatcher.Match(entryUrl, pageUrl, UrlMatchMode.Domain));
    }

    [Theory]
    [InlineData("https://github.com", "https://github.com/x", MatchQuality.Host)]
    [InlineData("https://github.com", "https://gist.github.com", MatchQuality.None)]
    public void HostMode(string entryUrl, string pageUrl, MatchQuality expected)
    {
        Assert.Equal(expected, UrlMatcher.Match(entryUrl, pageUrl, UrlMatchMode.Host));
    }

    [Theory]
    [InlineData("https://example.com/app", "https://example.com/app", MatchQuality.PathPrefix)]
    [InlineData("https://example.com/app", "https://example.com/app/login?x=1", MatchQuality.PathPrefix)]
    [InlineData("https://example.com/app", "https://example.com/application", MatchQuality.None)]
    [InlineData("https://example.com/app", "https://sub.example.com/app", MatchQuality.None)]
    public void StartsWithMode(string entryUrl, string pageUrl, MatchQuality expected)
    {
        Assert.Equal(expected, UrlMatcher.Match(entryUrl, pageUrl, UrlMatchMode.StartsWith));
    }

    [Theory]
    [InlineData("https://example.com/login?a=1", "https://example.com/login?a=1#frag", MatchQuality.Exact)]
    [InlineData("https://example.com/login/", "https://example.com/login", MatchQuality.Exact)]
    [InlineData("https://example.com/login?a=1", "https://example.com/login?a=2", MatchQuality.None)]
    public void ExactMode(string entryUrl, string pageUrl, MatchQuality expected)
    {
        Assert.Equal(expected, UrlMatcher.Match(entryUrl, pageUrl, UrlMatchMode.Exact));
    }

    [Fact]
    public void NeverModeNeverMatches()
    {
        Assert.Equal(MatchQuality.None, UrlMatcher.Match("https://a.com", "https://a.com", UrlMatchMode.Never));
    }

    [Fact]
    public void FindMatchesOrdersByQualityAndSkipsNonMatching()
    {
        var root = new VaultEntry { Title = "Kök", Urls = ["https://example.com"] };
        var app = new VaultEntry { Title = "Uygulama", Urls = ["https://example.com/app"] };
        var sub = new VaultEntry { Title = "Alt", Urls = ["https://other.com", "https://example.com"], MatchMode = UrlMatchMode.Host };
        var other = new VaultEntry { Title = "Başka", Urls = ["https://other.com"] };

        var matches = UrlMatcher.FindMatches([root, app, sub, other], "https://example.com/app/login");

        Assert.Equal(["Uygulama", "Alt", "Kök"], matches.Select(m => m.Entry.Title));
        Assert.Equal("https://example.com", matches[1].MatchedUrl);
    }
}
