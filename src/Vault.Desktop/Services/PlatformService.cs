using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Vault.Desktop.Platform;

namespace Vault.Desktop.Services;

/// <summary>Dosya seçme ve bağlantı açma gibi pencereye bağlı işlemler (testlerde sahte uygulanır).</summary>
public interface IPlatformService
{
    Task<string?> PickExistingVaultAsync();
    Task<string?> PickNewVaultPathAsync(string suggestedPath);
    Task OpenUrlAsync(string url);

    /// <summary>Genel dosya seçici; iptal edilirse null.</summary>
    Task<string?> PickFileAsync(string title, string typeName, string[] patterns);

    /// <summary>Görüntü dosyasını BGRA piksellerine çevirir.</summary>
    Task<CapturedImage?> LoadImageAsync(string path);

    /// <summary>Pencereyi kısa süre gizleyip tüm ekranın görüntüsünü alır (yalnızca Windows).</summary>
    Task<CapturedImage?> CaptureScreenAsync();

    /// <summary>Panodaki görüntü (yalnızca Windows); yoksa null.</summary>
    CapturedImage? GetClipboardImage();
}

public sealed class PlatformService(Func<TopLevel?> topLevel) : IPlatformService
{
    private static readonly FilePickerFileType VaultFileType = new("LocalVault kasası (*.json)")
    {
        Patterns = ["*.json"],
    };

    public async Task<string?> PickExistingVaultAsync()
    {
        if (topLevel()?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Kasa dosyasını seçin",
            AllowMultiple = false,
            FileTypeFilter = [VaultFileType, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewVaultPathAsync(string suggestedPath)
    {
        if (topLevel()?.StorageProvider is not { CanSave: true } storage)
            return null;

        var directory = Path.GetDirectoryName(suggestedPath);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Yeni kasa konumu",
            SuggestedFileName = Path.GetFileName(suggestedPath),
            DefaultExtension = "json",
            FileTypeChoices = [VaultFileType],
            SuggestedStartLocation = directory is not null && Directory.Exists(directory)
                ? await storage.TryGetFolderFromPathAsync(directory)
                : null,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title, string typeName, string[] patterns)
    {
        if (topLevel()?.StorageProvider is not { CanOpen: true } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task<CapturedImage?> LoadImageAsync(string path) => Task.Run(() =>
    {
        using var stream = File.OpenRead(path);
        using var bitmap = new Bitmap(stream);
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        unsafe
        {
            fixed (byte* buffer = pixels)
                bitmap.CopyPixels(new PixelRect(size), (nint)buffer, pixels.Length, stride);
        }
        // QR çözücü BGRA bekler; RGBA ise kırmızı ve maviyi değiştir.
        if (bitmap.Format == PixelFormats.Rgba8888)
        {
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
        return (CapturedImage?)new CapturedImage(pixels, size.Width, size.Height);
    });

    public async Task<CapturedImage?> CaptureScreenAsync()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // LocalVault penceresi QR kodunu kapatmasın diye kısa süre küçült.
        var window = topLevel() as Window;
        var previous = window?.WindowState;
        if (window is not null)
        {
            window.WindowState = WindowState.Minimized;
            await Task.Delay(350);
        }
        try
        {
            return await Task.Run(ScreenCapture.CaptureVirtualScreen);
        }
        finally
        {
            if (window is not null && previous is { } state)
            {
                window.WindowState = state;
                window.Activate();
            }
        }
    }

    public CapturedImage? GetClipboardImage() => OperatingSystem.IsWindows() ? WindowsClipboard.GetImage() : null;

    public async Task OpenUrlAsync(string url)
    {
        if (!Vault.Core.Matching.UrlMatcher.TryParse(url, out var uri))
            return;
        if (topLevel()?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(uri);
    }
}
