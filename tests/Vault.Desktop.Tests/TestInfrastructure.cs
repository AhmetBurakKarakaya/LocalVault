using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Vault.Core.Models;
using Vault.Desktop;
using Vault.Desktop.Services;
using Vault.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Vault.Desktop.Tests;

public static class TestAppBuilder
{
    // Gerçek Skia çizimi: ekran görüntüleri alınabilsin diye headless çizim kapalı.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .AfterSetup(_ => Logger.Sink = BindingErrorSink.Instance);
}

/// <summary>XAML bağlama hatalarını toplar; testler bunların olmadığını doğrular.</summary>
public sealed class BindingErrorSink : ILogSink
{
    public static BindingErrorSink Instance { get; } = new();
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors
    {
        get { lock (_errors) return [.. _errors]; }
    }

    public void Clear()
    {
        lock (_errors) _errors.Clear();
    }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Add(area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Add(area, source, messageTemplate, propertyValues);

    private void Add(string area, object? source, string template, object?[] values)
    {
        var text = $"[{area}] {source?.GetType().Name}: {template} | {string.Join(", ", values)}";
        lock (_errors) _errors.Add(text);
    }
}

public sealed class FakeClipboard : IClipboardService
{
    public List<(string Text, bool Sensitive)> Copies { get; } = [];
    public string? Current { get; private set; }
    public int ClearCalls { get; private set; }

    public Task CopyAsync(string text, bool sensitive = true)
    {
        Copies.Add((text, sensitive));
        Current = text;
        return Task.CompletedTask;
    }

    public Task ClearIfOwnedAsync()
    {
        ClearCalls++;
        Current = null;
        return Task.CompletedTask;
    }
}

public sealed class FakePlatform : IPlatformService
{
    public string? NextExistingVault { get; set; }
    public string? NextNewVaultPath { get; set; }
    public List<string> OpenedUrls { get; } = [];

    public Task<string?> PickExistingVaultAsync() => Task.FromResult(NextExistingVault);
    public Task<string?> PickNewVaultPathAsync(string suggestedPath) => Task.FromResult(NextNewVaultPath);

    public Task OpenUrlAsync(string url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    public string? NextFile { get; set; }
    public Vault.Desktop.Platform.CapturedImage? NextImage { get; set; }
    public Task<string?> PickFileAsync(string title, string typeName, string[] patterns) => Task.FromResult(NextFile);
    public Task<Vault.Desktop.Platform.CapturedImage?> LoadImageAsync(string path) => Task.FromResult(NextImage);
    public Task<Vault.Desktop.Platform.CapturedImage?> CaptureScreenAsync() => Task.FromResult(NextImage);
    public Vault.Desktop.Platform.CapturedImage? GetClipboardImage() => NextImage;
}

/// <summary>Her test için geçici bir klasörde ayar + kasa dosyası ve sahte servisler.</summary>
public sealed class TestEnvironment : IDisposable
{
    public const string MasterPassword = "Test-Ana-Parola-1";

    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "LocalVaultDesktopTests", Guid.NewGuid().ToString("N"));
    public string VaultPath => Path.Combine(Directory, "vault.json");
    public FakeClipboard Clipboard { get; } = new();
    public FakePlatform Platform { get; } = new();
    public AppServices Services { get; }

    public TestEnvironment()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var settings = SettingsStore.Load(Path.Combine(Directory, "settings.json"));
        settings.Current.VaultPath = VaultPath;

        Services = new AppServices
        {
            SettingsStore = settings,
            Vault = new VaultService { KdfFactoryForTests = FastKdf },
            Clipboard = Clipboard,
            Platform = Platform,
        };
    }

    public static KdfParameters FastKdf() => new()
    {
        Salt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
        MemoryKb = 64,
        Iterations = 1,
        Parallelism = 1,
    };

    /// <summary>Verilen kayıtlarla diskte bir kasa oluşturur (uygulama kilitli başlar).</summary>
    public void SeedVault(params VaultEntry[] entries)
    {
        using var session = Vault.Core.Storage.VaultSession.Create(VaultPath, MasterPassword, FastKdf());
        session.Data.Entries.AddRange(entries);
        session.Save();
    }

    public static VaultEntry[] SampleEntries() =>
    [
        new()
        {
            Title = "GitHub", Username = "ahmet@example.com", Password = "Gh!7xQ2#pLm9vR4t",
            Urls = ["https://github.com"], Totp = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP" },
            Notes = "Kurtarma kodları kasada değil, yazıcıdan çıktı alındı.", Tags = ["iş", "geliştirme"],
        },
        new()
        {
            Title = "Şirket Bankası", Username = "12345678", Password = "Bnk-2026!",
            Urls = ["https://internet.bank.com.tr", "https://mobil.bank.com.tr"], MatchMode = UrlMatchMode.Host,
            Totp = new TotpSettings { Secret = "GEZDGNBVGY3TQOJQ", Digits = 8 },
        },
        new() { Title = "Gmail", Username = "ahmet.karakaya@example.com", Password = "x", Urls = ["https://accounts.google.com"] },
        new() { Title = "Netflix", Username = "aile@example.com", Password = "y", Urls = ["netflix.com"] },
        new() { Title = "AWS Konsol", Username = "admin", Password = "z", Urls = ["https://console.aws.amazon.com"], Tags = ["iş"] },
        new() { Title = "Wi-Fi (ev)", Password = "ev-agi-parolasi", Notes = "Router: 192.168.1.1" },
    ];

    public void Dispose()
    {
        Services.Vault.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
