using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vault.Desktop.Platform;

public readonly record struct ForegroundWindowInfo(nint Handle, string Title, int ProcessId);

/// <summary>Önde olan pencere bilgisi ve odak değiştirme.</summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32Windows
{
    public static ForegroundWindowInfo GetForeground()
    {
        var handle = Win32Input.GetForegroundWindow();
        if (handle == 0)
            return default;

        var length = GetWindowTextLengthW(handle);
        var buffer = new char[Math.Max(length + 1, 1)];
        var copied = GetWindowTextW(handle, buffer, buffer.Length);
        GetWindowThreadProcessId(handle, out var processId);
        return new ForegroundWindowInfo(handle, new string(buffer, 0, Math.Max(copied, 0)), (int)processId);
    }

    public static bool Activate(nint handle)
    {
        if (IsIconic(handle))
            ShowWindow(handle, 9);   // SW_RESTORE
        return SetForegroundWindow(handle);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLengthW(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(nint hWnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int command);
}

/// <summary>
/// Sistem genelinde kısayol (RegisterHotKey). Pencereye ihtiyaç duymamak için kendi ileti döngüsü olan
/// ayrı bir iş parçacığında kaydedilir; WM_HOTKEY o iş parçacığının kuyruğuna gelir.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class GlobalHotkey : IDisposable
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;

    private readonly Thread _thread;
    private uint _threadId;

    public bool IsRegistered { get; private set; }
    public event Action? Pressed;

    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IsRegistered = RegisterHotKey(0, 1, modifiers | ModNoRepeat, virtualKey);
            ready.Set();
            if (!IsRegistered)
                return;

            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == WmHotkey)
                    Pressed?.Invoke();
            }
            UnregisterHotKey(0, 1);
        })
        {
            IsBackground = true,
            Name = "LocalVault.Hotkey",
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (IsRegistered)
            PostThreadMessageW(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessageW(out Msg message, nint hWnd, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
