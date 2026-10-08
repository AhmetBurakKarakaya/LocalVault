using Vault.Core.Otp;
using Vault.Desktop.Platform;

namespace Vault.Desktop.Services;

public enum QrSource
{
    Screen,
    Clipboard,
    File,
}

public sealed record QrScanResult(IReadOnlyList<OtpAuthInfo> Accounts, IReadOnlyList<string> Warnings, string? Error, bool Cancelled = false);

/// <summary>Ekran, pano veya dosyadaki QR kodlarından TOTP hesaplarını okur.</summary>
public sealed class QrScanService(IPlatformService platform)
{
    public async Task<QrScanResult> ScanAsync(QrSource source)
    {
        CapturedImage? image;
        try
        {
            switch (source)
            {
                case QrSource.Screen:
                    image = await platform.CaptureScreenAsync();
                    if (image is null)
                        return Failure("Ekran görüntüsü bu platformda alınamıyor; görüntü dosyası veya pano kullanın.");
                    break;
                case QrSource.Clipboard:
                    image = platform.GetClipboardImage();
                    if (image is null)
                        return Failure("Panoda görüntü yok. QR kodunun ekran alıntısını alın (Win+Shift+S) ve tekrar deneyin.");
                    break;
                default:
                    var path = await platform.PickFileAsync("QR kodu içeren görüntüyü seçin", "Görüntüler", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]);
                    if (path is null)
                        return new QrScanResult([], [], null, Cancelled: true);
                    image = await platform.LoadImageAsync(path);
                    if (image is null)
                        return Failure("Görüntü açılamadı.");
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(ex.Message);
        }

        var codes = await Task.Run(() => QrDecoder.Decode(image.Pixels, image.Width, image.Height, image.Stride, PixelLayout.Bgra32));
        if (codes.Count == 0)
        {
            return Failure(source == QrSource.Screen
                ? "Ekranda QR kodu bulunamadı. QR kodunun görünür olduğundan emin olun."
                : "Görüntüde QR kodu bulunamadı.");
        }

        var result = OtpImport.FromQrTexts(codes);
        return result.Accounts.Count == 0
            ? new QrScanResult([], result.Warnings, result.Warnings.FirstOrDefault() ?? "QR kodunda doğrulama hesabı yok.")
            : new QrScanResult(result.Accounts, result.Warnings, null);
    }

    private static QrScanResult Failure(string message) => new([], [], message);
}
