using System.Text.RegularExpressions;
using Vault.Core.Models;

namespace Vault.Core.AutoType;

/// <summary>
/// Pencere başlığını kayıtların Auto-Type desenleriyle eşleştirir.
/// Desende * (herhangi bir metin) ve ? (tek karakter) kullanılabilir; joker içermeyen desen
/// başlığın içinde geçiyorsa eşleşir. Büyük/küçük harf duyarsızdır.
/// </summary>
public static class WindowMatcher
{
    public static bool Matches(string pattern, string windowTitle)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0 || string.IsNullOrEmpty(windowTitle))
            return false;

        if (pattern.IndexOfAny(['*', '?']) < 0)
            return windowTitle.Contains(pattern, StringComparison.InvariantCultureIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(windowTitle, regex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    }

    public static IReadOnlyList<VaultEntry> FindMatches(IEnumerable<VaultEntry> entries, string windowTitle) =>
        entries
            .Where(e => e.AutoTypeWindows.Any(p => Matches(p, windowTitle)))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
