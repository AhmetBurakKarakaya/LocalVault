using Vault.Core.Models;

namespace Vault.Core.Matching;

/// <summary>Eşleşmenin gücü; büyük değer daha iyi eşleşme demektir.</summary>
public enum MatchQuality
{
    None = 0,
    Subdomain = 1,
    Host = 2,
    PathPrefix = 3,
    Exact = 4,
}

public sealed record UrlMatch(VaultEntry Entry, MatchQuality Quality, string MatchedUrl);

/// <summary>
/// Bir sayfa adresine hangi kayıtların uyduğunu belirler. Kural olarak yalnızca kayıtlı host'un
/// kendisi veya onun alt alan adları eşleşir; bu sayede "github.com.evil.com" ya da
/// "evil-github.com" gibi taklit alan adları hiçbir zaman eşleşmez.
/// </summary>
public static class UrlMatcher
{
    public static IReadOnlyList<UrlMatch> FindMatches(IEnumerable<VaultEntry> entries, string pageUrl)
    {
        if (!TryParse(pageUrl, out var page))
            return [];

        var results = new List<UrlMatch>();
        foreach (var entry in entries)
        {
            var best = MatchQuality.None;
            string? bestUrl = null;
            foreach (var url in entry.Urls)
            {
                var quality = Match(url, page, entry.MatchMode);
                if (quality > best)
                {
                    best = quality;
                    bestUrl = url;
                }
            }

            if (best != MatchQuality.None)
                results.Add(new UrlMatch(entry, best, bestUrl!));
        }

        return results
            .OrderByDescending(m => m.Quality)
            .ThenBy(m => m.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static MatchQuality Match(string entryUrl, string pageUrl, UrlMatchMode mode) =>
        TryParse(pageUrl, out var page) ? Match(entryUrl, page, mode) : MatchQuality.None;

    private static MatchQuality Match(string entryUrl, Uri page, UrlMatchMode mode)
    {
        if (mode == UrlMatchMode.Never || !TryParse(entryUrl, out var entry))
            return MatchQuality.None;

        // HTTPS için kaydedilmiş bilgi düz HTTP sayfasına verilmez.
        if (entry.Scheme == Uri.UriSchemeHttps && page.Scheme != Uri.UriSchemeHttps)
            return MatchQuality.None;

        // Kayıtta özel port belirtildiyse sayfanın portu da aynı olmalı.
        if (!entry.IsDefaultPort && entry.Port != page.Port)
            return MatchQuality.None;

        var entryHost = NormalizeHost(entry);
        var pageHost = NormalizeHost(page);

        switch (mode)
        {
            case UrlMatchMode.Exact:
                return SameHost(entryHost, pageHost) && SamePathAndQuery(entry, page)
                    ? MatchQuality.Exact
                    : MatchQuality.None;

            case UrlMatchMode.StartsWith:
                return SameHost(entryHost, pageHost) && PathStartsWith(page, entry)
                    ? MatchQuality.PathPrefix
                    : MatchQuality.None;

            case UrlMatchMode.Host:
                return SameHost(entryHost, pageHost) ? MatchQuality.Host : MatchQuality.None;

            case UrlMatchMode.Domain:
                if (SameHost(entryHost, pageHost))
                    return entry.AbsolutePath.Length > 1 && PathStartsWith(page, entry)
                        ? MatchQuality.PathPrefix
                        : MatchQuality.Host;

                // IP adreslerinde "alt alan adı" kavramı yoktur.
                if (entry.HostNameType != UriHostNameType.Dns || page.HostNameType != UriHostNameType.Dns)
                    return MatchQuality.None;

                // "www.x.com" ↔ "x.com" eşdeğer sayılır; ancak alt alan adı kontrolü kırpılmamış host ile
                // yapılır, aksi hâlde "www.gov.tr" gibi bir kayıt tüm "*.gov.tr" sitelerine açılırdı.
                return SameHost(StripWww(entryHost), StripWww(pageHost))
                       || pageHost.EndsWith("." + entryHost, StringComparison.Ordinal)
                    ? MatchQuality.Subdomain
                    : MatchQuality.None;

            default:
                return MatchQuality.None;
        }
    }

    /// <summary>Şemasız adresleri ("github.com") https olarak yorumlar; yalnızca http/https kabul edilir.</summary>
    public static bool TryParse(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        url = url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }

    // IdnHost: Unicode alan adlarını punycode'a çevirir; böylece görsel taklitler ayrı host olarak kalır.
    private static string NormalizeHost(Uri uri) => uri.IdnHost.TrimEnd('.').ToLowerInvariant();

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.Ordinal) && host.Count(c => c == '.') >= 2 ? host[4..] : host;

    private static bool SameHost(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static bool SamePathAndQuery(Uri entry, Uri page) =>
        string.Equals(entry.AbsolutePath.TrimEnd('/'), page.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal)
        && string.Equals(entry.Query, page.Query, StringComparison.Ordinal);

    private static bool PathStartsWith(Uri page, Uri entry)
    {
        var prefix = entry.AbsolutePath;
        var path = page.AbsolutePath;
        if (prefix.Length <= 1)
            return true;

        // "/app" öneki "/app" ve "/app/..." ile eşleşir ama "/application" ile eşleşmez.
        prefix = prefix.TrimEnd('/');
        return path.Equals(prefix, StringComparison.Ordinal)
               || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }
}
