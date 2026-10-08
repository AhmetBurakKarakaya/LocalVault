using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Vault.Bridge;

public enum BrowserKind
{
    Chrome,
    Edge,
    Firefox,
}

/// <summary>Native host kaydının yazıldığı yer (Windows'ta HKCU kayıt defteri; testlerde bellek içi).</summary>
public interface INativeHostRegistry
{
    string? GetManifestPath(BrowserKind browser);
    void SetManifestPath(BrowserKind browser, string manifestPath);
    void Remove(BrowserKind browser);
}

/// <summary>
/// Tarayıcılara LocalVault native host'unu tanıtır: her tarayıcı için bir manifest JSON yazar ve
/// konumunu kaydeder. Yönetici yetkisi gerekmez (yalnızca geçerli kullanıcı).
/// https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging#native-messaging-host-location
/// </summary>
public sealed class NativeMessagingRegistrar(string manifestDirectory, INativeHostRegistry registry)
{
    public static readonly BrowserKind[] AllBrowsers = [BrowserKind.Chrome, BrowserKind.Edge, BrowserKind.Firefox];

    public static string DefaultManifestDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalVault", "native-messaging");

    /// <summary>Platforma uygun varsayılan kayıt (Windows: kayıt defteri, diğerleri: tarayıcı klasörleri).</summary>
    public static NativeMessagingRegistrar CreateDefault() => OperatingSystem.IsWindows()
        ? new NativeMessagingRegistrar(DefaultManifestDirectory, new WindowsNativeHostRegistry())
        : new NativeMessagingRegistrar(DefaultManifestDirectory, new UnixNativeHostRegistry());

    public void Register(string hostExecutablePath)
    {
        if (!File.Exists(hostExecutablePath))
            throw new FileNotFoundException("Native host bulunamadı.", hostExecutablePath);

        Directory.CreateDirectory(manifestDirectory);

        var chromium = CreateManifest(hostExecutablePath);
        chromium["allowed_origins"] = new JsonArray($"chrome-extension://{BridgeProtocol.ChromeExtensionId}/");
        var chromiumPath = Path.Combine(manifestDirectory, "chromium.json");
        File.WriteAllText(chromiumPath, chromium.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var firefox = CreateManifest(hostExecutablePath);
        firefox["allowed_extensions"] = new JsonArray(BridgeProtocol.FirefoxExtensionId);
        var firefoxPath = Path.Combine(manifestDirectory, "firefox.json");
        File.WriteAllText(firefoxPath, firefox.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        registry.SetManifestPath(BrowserKind.Chrome, chromiumPath);
        registry.SetManifestPath(BrowserKind.Edge, chromiumPath);
        registry.SetManifestPath(BrowserKind.Firefox, firefoxPath);
    }

    public void Unregister()
    {
        foreach (var browser in AllBrowsers)
            registry.Remove(browser);
        foreach (var file in new[] { "chromium.json", "firefox.json" })
        {
            var path = Path.Combine(manifestDirectory, file);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>Tarayıcının kayıtlı manifest'i bu host'u gösteriyor mu?</summary>
    public bool IsRegistered(BrowserKind browser, string hostExecutablePath)
    {
        var manifestPath = registry.GetManifestPath(browser);
        if (manifestPath is null || !File.Exists(manifestPath))
            return false;
        try
        {
            var path = JsonNode.Parse(File.ReadAllText(manifestPath))?["path"]?.GetValue<string>();
            return path is not null && Path.GetFullPath(path).Equals(Path.GetFullPath(hostExecutablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static JsonObject CreateManifest(string hostExecutablePath) => new()
    {
        ["name"] = BridgeProtocol.NativeHostName,
        ["description"] = "LocalVault parola yöneticisi köprüsü",
        ["path"] = Path.GetFullPath(hostExecutablePath),
        ["type"] = "stdio",
    };
}

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeHostRegistry : INativeHostRegistry
{
    private static string KeyPath(BrowserKind browser) => browser switch
    {
        BrowserKind.Chrome => @"Software\Google\Chrome\NativeMessagingHosts\" + BridgeProtocol.NativeHostName,
        BrowserKind.Edge => @"Software\Microsoft\Edge\NativeMessagingHosts\" + BridgeProtocol.NativeHostName,
        _ => @"Software\Mozilla\NativeMessagingHosts\" + BridgeProtocol.NativeHostName,
    };

    public string? GetManifestPath(BrowserKind browser)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath(browser));
        return key?.GetValue(null) as string;
    }

    public void SetManifestPath(BrowserKind browser, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath(browser));
        key.SetValue(null, manifestPath);
    }

    public void Remove(BrowserKind browser) => Registry.CurrentUser.DeleteSubKeyTree(KeyPath(browser), throwOnMissingSubKey: false);
}

/// <summary>Linux/macOS: manifest, tarayıcının NativeMessagingHosts klasörüne kopyalanır.</summary>
public sealed class UnixNativeHostRegistry : INativeHostRegistry
{
    private static string TargetPath(BrowserKind browser)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = (OperatingSystem.IsMacOS(), browser) switch
        {
            (true, BrowserKind.Chrome) => "Library/Application Support/Google/Chrome/NativeMessagingHosts",
            (true, BrowserKind.Edge) => "Library/Application Support/Microsoft Edge/NativeMessagingHosts",
            (true, _) => "Library/Application Support/Mozilla/NativeMessagingHosts",
            (false, BrowserKind.Chrome) => ".config/google-chrome/NativeMessagingHosts",
            (false, BrowserKind.Edge) => ".config/microsoft-edge/NativeMessagingHosts",
            _ => ".mozilla/native-messaging-hosts",
        };
        return Path.Combine(home, dir, BridgeProtocol.NativeHostName + ".json");
    }

    public string? GetManifestPath(BrowserKind browser) =>
        File.Exists(TargetPath(browser)) ? TargetPath(browser) : null;

    public void SetManifestPath(BrowserKind browser, string manifestPath)
    {
        var target = TargetPath(browser);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(manifestPath, target, overwrite: true);
    }

    public void Remove(BrowserKind browser)
    {
        if (File.Exists(TargetPath(browser)))
            File.Delete(TargetPath(browser));
    }
}
