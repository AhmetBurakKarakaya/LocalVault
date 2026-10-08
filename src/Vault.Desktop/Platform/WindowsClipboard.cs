using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vault.Desktop.Platform;

/// <summary>
/// Win32 panosuna doğrudan erişim. Avalonia panosundan farkı: hassas veriler için Windows'un
/// pano geçmişi (Win+V) ve bulut panosu eşitlemesinin dışında tutulmasını sağlayan biçimleri ekler.
/// https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public static void SetText(string text, bool excludeFromHistory)
    {
        OpenWithRetry();
        try
        {
            if (!EmptyClipboard())
                throw new IOException("Pano temizlenemedi.");

            SetUnicodeText(text);

            if (excludeFromHistory)
            {
                SetDword("ExcludeClipboardContentFromMonitorProcessing", 0);
                SetDword("CanIncludeInClipboardHistory", 0);
                SetDword("CanUploadToCloudClipboard", 0);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText))
            return null;

        OpenWithRetry();
        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint CfDib = 8;

    /// <summary>Panodaki görüntüyü (ör. Win+Shift+S ekran alıntısı) BGRA olarak döndürür; yoksa null.</summary>
    public static CapturedImage? GetImage()
    {
        if (!IsClipboardFormatAvailable(CfDib))
            return null;

        OpenWithRetry();
        try
        {
            var handle = GetClipboardData(CfDib);
            if (handle == 0)
                return null;
            var pointer = GlobalLock(handle);
            if (pointer == 0)
                return null;
            try
            {
                var size = (int)GlobalSize(handle);
                unsafe
                {
                    return ScreenCapture.FromDib(new ReadOnlySpan<byte>((void*)pointer, size));
                }
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Panoda adı verilen (kayıtlı) biçim var mı?</summary>
    public static bool HasFormat(string formatName)
    {
        var format = RegisterClipboardFormatW(formatName);
        return format != 0 && IsClipboardFormatAvailable(format);
    }

    public static void Clear()
    {
        OpenWithRetry();
        try
        {
            EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void SetUnicodeText(string text)
    {
        var byteCount = (nuint)((text.Length + 1) * sizeof(char));
        var handle = GlobalAlloc(GmemMoveable, byteCount);
        if (handle == 0)
            throw new OutOfMemoryException();

        var pointer = GlobalLock(handle);
        unsafe
        {
            var destination = new Span<char>((void*)pointer, text.Length + 1);
            text.AsSpan().CopyTo(destination);
            destination[text.Length] = '\0';
        }
        GlobalUnlock(handle);

        if (SetClipboardData(CfUnicodeText, handle) == 0)
        {
            GlobalFree(handle);
            throw new IOException("Panoya yazılamadı.");
        }
    }

    private static void SetDword(string formatName, int value)
    {
        var format = RegisterClipboardFormatW(formatName);
        if (format == 0)
            return;

        var handle = GlobalAlloc(GmemMoveable, sizeof(int));
        if (handle == 0)
            return;

        Marshal.WriteInt32(GlobalLock(handle), value);
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == 0)
            GlobalFree(handle);
    }

    private static void OpenWithRetry()
    {
        // Pano aynı anda tek bir uygulama tarafından açılabilir; kısa süre bekleyip yeniden dene.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(0))
                return;
            Thread.Sleep(25);
        }
        throw new IOException("Pano başka bir uygulama tarafından kullanılıyor.");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(nint hMem);
}
