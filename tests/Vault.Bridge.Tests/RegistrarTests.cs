using System.Text.Json.Nodes;

namespace Vault.Bridge.Tests;

public sealed class RegistrarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LocalVaultRegistrar", Guid.NewGuid().ToString("N"));
    private readonly MemoryRegistry _registry = new();
    private readonly string _hostPath;

    public RegistrarTests()
    {
        Directory.CreateDirectory(_dir);
        _hostPath = Path.Combine(_dir, "LocalVault.NativeHost.exe");
        File.WriteAllText(_hostPath, "");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class MemoryRegistry : INativeHostRegistry
    {
        public Dictionary<BrowserKind, string> Paths { get; } = [];
        public string? GetManifestPath(BrowserKind browser) => Paths.GetValueOrDefault(browser);
        public void SetManifestPath(BrowserKind browser, string manifestPath) => Paths[browser] = manifestPath;
        public void Remove(BrowserKind browser) => Paths.Remove(browser);
    }

    [Fact]
    public void RegisterWritesManifestsForAllBrowsers()
    {
        var registrar = new NativeMessagingRegistrar(Path.Combine(_dir, "manifests"), _registry);
        registrar.Register(_hostPath);

        Assert.Equal(3, _registry.Paths.Count);
        Assert.Equal(_registry.Paths[BrowserKind.Chrome], _registry.Paths[BrowserKind.Edge]);

        var chromium = JsonNode.Parse(File.ReadAllText(_registry.Paths[BrowserKind.Chrome]))!;
        Assert.Equal("com.localvault.bridge", chromium["name"]!.GetValue<string>());
        Assert.Equal("stdio", chromium["type"]!.GetValue<string>());
        Assert.Equal(_hostPath, chromium["path"]!.GetValue<string>());
        Assert.Equal($"chrome-extension://{BridgeProtocol.ChromeExtensionId}/",
            chromium["allowed_origins"]![0]!.GetValue<string>());

        var firefox = JsonNode.Parse(File.ReadAllText(_registry.Paths[BrowserKind.Firefox]))!;
        Assert.Equal(BridgeProtocol.FirefoxExtensionId, firefox["allowed_extensions"]![0]!.GetValue<string>());
        Assert.Null(firefox["allowed_origins"]);

        foreach (var browser in NativeMessagingRegistrar.AllBrowsers)
            Assert.True(registrar.IsRegistered(browser, _hostPath));
    }

    [Fact]
    public void IsRegisteredDetectsStaleHostPath()
    {
        var registrar = new NativeMessagingRegistrar(Path.Combine(_dir, "manifests"), _registry);
        registrar.Register(_hostPath);

        var movedHost = Path.Combine(_dir, "taşındı", "LocalVault.NativeHost.exe");
        Assert.False(registrar.IsRegistered(BrowserKind.Chrome, movedHost));
    }

    [Fact]
    public void UnregisterRemovesEverything()
    {
        var registrar = new NativeMessagingRegistrar(Path.Combine(_dir, "manifests"), _registry);
        registrar.Register(_hostPath);
        registrar.Unregister();

        Assert.Empty(_registry.Paths);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "manifests")));
        Assert.False(registrar.IsRegistered(BrowserKind.Firefox, _hostPath));
    }

    [Fact]
    public void ChromeExtensionIdMatchesManifestKey()
    {
        // Chrome kimliği = SHA-256(manifest "key") ilk 16 baytının a-p alfabesiyle yazılışı.
        var manifest = JsonNode.Parse(File.ReadAllText(FindRepoFile("extension/manifest.base.json")))!;
        var key = Convert.FromBase64String(manifest["key"]!.GetValue<string>());
        var hash = System.Security.Cryptography.SHA256.HashData(key);
        var id = string.Concat(hash.Take(16).SelectMany(b => new[] { b >> 4, b & 0xF }).Select(n => (char)('a' + n)));
        Assert.Equal(BridgeProtocol.ChromeExtensionId, id);
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
