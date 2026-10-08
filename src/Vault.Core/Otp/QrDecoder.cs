using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using ZXing.QrCode;

namespace Vault.Core.Otp;

public enum PixelLayout
{
    Bgra32,
    Rgba32,
}

/// <summary>
/// Ham piksel verisindeki QR kodlarını çözer (ekran görüntüsü, pano görüntüsü veya dosya).
/// Platformdan bağımsızdır; pikselleri elde etmek çağıranın işidir.
/// </summary>
public static class QrDecoder
{
    private static readonly Dictionary<DecodeHintType, object> Hints = new()
    {
        [DecodeHintType.TRY_HARDER] = true,
        [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
    };

    /// <summary>Görüntüdeki tüm QR kodlarının metinlerini döndürür (bulunamazsa boş liste).</summary>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> pixels, int width, int height, int stride, PixelLayout layout)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || pixels.Length < stride * height)
            throw new ArgumentException("Piksel arabelleği boyutları geçersiz.");

        // 32 bit renkli → 8 bit gri (ITU-R BT.601 ağırlıkları)
        var (r, b) = layout == PixelLayout.Bgra32 ? (2, 0) : (0, 2);
        var luminance = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = 0; x < width; x++)
            {
                var p = row.Slice(x * 4, 4);
                luminance[y * width + x] = (byte)((p[r] * 299 + p[1] * 587 + p[b] * 114) / 1000);
            }
        }

        var source = new RGBLuminanceSource(luminance, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));

        try
        {
            var results = new QRCodeMultiReader().decodeMultiple(bitmap, Hints);
            if (results is { Length: > 0 })
                return results.Select(r => r.Text).Distinct().ToList();
        }
        catch (ReaderException)
        {
        }

        // Çoklu okuyucu bulamazsa tek kod okuyucuyla bir kez daha dene.
        try
        {
            var single = new QRCodeReader().decode(bitmap, Hints);
            return single is null ? [] : [single.Text];
        }
        catch (ReaderException)
        {
            return [];
        }
    }
}
