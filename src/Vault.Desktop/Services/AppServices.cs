namespace Vault.Desktop.Services;

/// <summary>Uygulamanın paylaşılan servisleri (basit kompozisyon kökü).</summary>
public sealed class AppServices
{
    public required SettingsStore SettingsStore { get; init; }
    public required VaultService Vault { get; init; }
    public required IClipboardService Clipboard { get; init; }
    public required IPlatformService Platform { get; init; }

    public AppSettings Settings => SettingsStore.Current;

    /// <summary>Tarayıcı entegrasyonu (headless testlerde null).</summary>
    public BrowserIntegration? Browser { get; init; }

    /// <summary>Auto-Type (yalnızca Windows masaüstü yaşam döngüsünde).</summary>
    public AutoTypeService? AutoType { get; init; }

    /// <summary>Tema değişikliği gibi uygulama geneline yayılan ayarları uygular.</summary>
    public Action<AppSettings>? ApplySettings { get; init; }
}
