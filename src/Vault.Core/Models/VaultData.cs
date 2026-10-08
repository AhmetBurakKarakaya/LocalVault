namespace Vault.Core.Models;

/// <summary>Kasanın şifresi çözülmüş içeriği.</summary>
public sealed class VaultData
{
    public List<VaultEntry> Entries { get; set; } = [];

    /// <summary>Onaylanmış tarayıcı eklentisi bağlantıları (anahtarlar kasayla birlikte şifrelenir).</summary>
    public List<BrowserAssociation> BrowserAssociations { get; set; } = [];

    public VaultEntry? FindById(Guid id) => Entries.Find(e => e.Id == id);

    public IEnumerable<VaultEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Entries.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);

        return Entries
            .Where(e => Contains(e.Title, query)
                        || Contains(e.Username, query)
                        || e.Urls.Any(u => Contains(u, query))
                        || e.Tags.Any(t => Contains(t, query)))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);
    }

    public bool Remove(Guid id) => Entries.RemoveAll(e => e.Id == id) > 0;

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Kullanıcının onayladığı bir tarayıcı eklentisi örneği.</summary>
public sealed class BrowserAssociation
{
    /// <summary>Eklentinin rastgele ürettiği kimlik.</summary>
    public string ClientId { get; set; } = "";
    /// <summary>İstekleri imzalamak için paylaşılan 256 bit anahtar (Base64).</summary>
    public string Key { get; set; } = "";
    /// <summary>Kullanıcıya gösterilen ad, ör. "Chrome".</summary>
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
