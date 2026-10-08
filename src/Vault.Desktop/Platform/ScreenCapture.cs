using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vault.Desktop.Platform;

/// <summary>BGRA, üstten alta 32 bit görüntü.</summary>
public sealed record CapturedImage(byte[] Pixels, int Width, int Height)
{
    public int Stride => Width * 4;
}

/// <summary>Tüm monitörleri kapsayan sanal ekranın görüntüsünü alır (QR kod taraması için).</summary>
[SupportedOSPlatform("windows")]
internal static partial class ScreenCapture
{
    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public static CapturedImage CaptureVirtualScreen()
    {
        int x = GetSystemMetrics(SmXVirtualScreen), y = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen), height = GetSystemMetrics(SmCyVirtualScreen);

        var screenDc = GetDC(0);
        var memoryDc = CreateCompatibleDC(screenDc);
        var info = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,   // negatif: üstten alta
            Planes = 1,
            BitCount = 32,
        };
        var bitmap = CreateDIBSection(screenDc, ref info, 0, out var bits, 0, 0);
        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt))
                throw new IOException("Ekran görüntüsü alınamadı.");
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new CapturedImage(pixels, width, height);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// Panodaki DIB görüntüsünü (ör. Win+Shift+S ekran alıntısı) BGRA'ya çevirir.
    /// 24 ve 32 bit, alttan üste/üstten alta, BI_RGB ve BI_BITFIELDS desteklenir.
    /// </summary>
    internal static CapturedImage? FromDib(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40)
            return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        if (width <= 0 || rawHeight == 0 || bitCount is not (24 or 32) || compression is not (0 or 3))
            return null;

        var height = Math.Abs(rawHeight);
        var topDown = rawHeight < 0;
        var offset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + colorsUsed * 4;
        var bytesPerPixel = bitCount / 8;
        var stride = (width * bytesPerPixel + 3) & ~3;
        if (offset + (long)stride * height > dib.Length)
            return null;

        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var source = dib.Slice(offset + (topDown ? row : height - 1 - row) * stride, width * bytesPerPixel);
            for (var col = 0; col < width; col++)
            {
                var target = (row * width + col) * 4;
                pixels[target] = source[col * bytesPerPixel];
                pixels[target + 1] = source[col * bytesPerPixel + 1];
                pixels[target + 2] = source[col * bytesPerPixel + 2];
                pixels[target + 3] = 255;
            }
        }
        return new CapturedImage(pixels, width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
}
