using System.Text.Json;
using System.Text.Json.Serialization;
using Vault.Core.Storage;

namespace Vault.Desktop.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Uygulama ayarları. Gizli bilgi içermez; settings.json olarak düz kaydedilir.</summary>
public sealed class AppSettings
{
    public string? VaultPath { get; set; }
    /// <summary>Bilgisayar bu kadar dakika boşta kalınca kasa kilitlenir (0 = kapalı).</summary>
    public int AutoLockMinutes { get; set; } = 5;
    /// <summary>Kopyalanan parola/OTP bu kadar saniye sonra panodan silinir (0 = silinmez).</summary>
    public int ClipboardClearSeconds { get; set; } = 20;
    public bool LockOnSessionLock { get; set; } = true;
    public bool LockOnMinimize { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>Tarayıcı eklentisinin native host üzerinden bağlanmasına izin ver.</summary>
    public bool BrowserIntegrationEnabled { get; set; } = true;
    /// <summary>Kısayolla etkin pencereye kullanıcı adı/parola yazma (yalnızca Windows).</summary>
    public bool AutoTypeEnabled { get; set; } = true;
    /// <summary>Auto-Type kısayolu, ör. "Ctrl+Alt+A" (bkz. <see cref="HotkeyGesture"/>).</summary>
    public string AutoTypeHotkey { get; set; } = HotkeyGesture.DefaultText;

    [JsonIgnore]
    public string EffectiveVaultPath => string.IsNullOrWhiteSpace(VaultPath) ? VaultPaths.DefaultVaultPath : VaultPath;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public sealed class SettingsStore(string filePath)
{
    public string FilePath { get; } = filePath;
    public AppSettings Current { get; private set; } = new();

    /// <summary>LOCALVAULT_SETTINGS ortam değişkeni (yalıtılmış deneme/geliştirme örnekleri için), yoksa kasa klasörü.</summary>
    public static string DefaultFilePath =>
        Environment.GetEnvironmentVariable("LOCALVAULT_SETTINGS") is { Length: > 0 } fromEnv
            ? fromEnv
            : Path.Combine(VaultPaths.DataDirectory, "settings.json");

    public static SettingsStore Load(string? filePath = null)
    {
        var store = new SettingsStore(filePath ?? DefaultFilePath);
        try
        {
            if (File.Exists(store.FilePath))
                store.Current = JsonSerializer.Deserialize(File.ReadAllText(store.FilePath), SettingsJsonContext.Default.AppSettings) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Bozuk ayar dosyası uygulamayı açılmaz hâle getirmesin; varsayılanlarla devam et.
            store.Current = new AppSettings();
        }
        return store;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings));
    }
}
