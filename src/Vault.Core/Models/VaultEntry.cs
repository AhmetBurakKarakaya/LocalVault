namespace Vault.Core.Models;

/// <summary>Kasadaki tek bir hesap kaydı.</summary>
public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public List<string> Urls { get; set; } = [];
    public UrlMatchMode MatchMode { get; set; } = UrlMatchMode.Domain;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public TotpSettings? Totp { get; set; }
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    /// <summary>Auto-Type: bu kaydın yazılacağı pencere başlığı desenleri (ör. "*Uzak Masaüstü*").</summary>
    public List<string> AutoTypeWindows { get; set; } = [];

    /// <summary>Auto-Type tuş dizisi; null ise <see cref="AutoType.AutoTypeSequence.Default"/>.</summary>
    public string? AutoTypeSequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

/// <summary>Kaydın hangi sayfalarda önerileceğini belirler.</summary>
public enum UrlMatchMode
{
    /// <summary>Aynı host veya alt alan adları (github.com → login.github.com).</summary>
    Domain,
    /// <summary>Yalnızca birebir aynı host.</summary>
    Host,
    /// <summary>Sayfa URL'si kayıtlı URL ile başlamalı.</summary>
    StartsWith,
    /// <summary>Sayfa URL'si (sorgu dahil) birebir aynı olmalı.</summary>
    Exact,
    /// <summary>Hiçbir sayfada otomatik önerilmez.</summary>
    Never,
}
