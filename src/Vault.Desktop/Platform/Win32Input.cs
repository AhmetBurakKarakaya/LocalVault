using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vault.Core.AutoType;

namespace Vault.Desktop.Platform;

public sealed class AutoTypeAbortedException(string message) : Exception(message);

/// <summary>
/// Auto-Type adımlarını Win32 klavye girdisine (SendInput) çevirir ve gönderir.
/// Metin KEYEVENTF_UNICODE ile yazılır; böylece klavye düzeninden (TR-Q/F, EN) bağımsızdır.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32Input
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    /// <summary>Tek bir gönderim adımı: ya tuş girdileri ya da bekleme.</summary>
    internal readonly record struct Step(Input[]? Inputs, int DelayMs);

    /// <summary>Adımları SendInput yapılarına çevirir (saf fonksiyon; test edilebilir).</summary>
    internal static List<Step> Build(IEnumerable<AutoTypeAction> actions)
    {
        var steps = new List<Step>();
        foreach (var action in actions)
        {
            switch (action)
            {
                case AutoTypeAction.Text text:
                    foreach (var c in text.Value)
                    {
                        // Satır sonu ve sekme metin olarak değil tuş olarak gönderilir (uygulamalar bunu bekler).
                        if (c == '\n') steps.Add(new Step(KeyPress(AutoTypeKey.Enter), 0));
                        else if (c == '\t') steps.Add(new Step(KeyPress(AutoTypeKey.Tab), 0));
                        else if (c != '\r') steps.Add(new Step(UnicodePress(c), 0));
                    }
                    break;
                case AutoTypeAction.Key key:
                    for (var i = 0; i < key.Count; i++)
                        steps.Add(new Step(KeyPress(key.Value), 0));
                    break;
                case AutoTypeAction.Delay delay:
                    steps.Add(new Step(null, delay.Milliseconds));
                    break;
            }
        }
        return steps;
    }

    /// <summary>
    /// Gönderir. Her adımdan önce hedef pencerenin hâlâ önde olduğu kontrol edilir; kullanıcı başka
    /// bir pencereye geçerse parola yanlış yere yazılmasın diye işlem durdurulur.
    /// </summary>
    public static void Type(IReadOnlyList<AutoTypeAction> actions, nint targetWindow, int keyDelayMs = 8, CancellationToken ct = default)
    {
        WaitForModifiersReleased(TimeSpan.FromSeconds(3));
        foreach (var step in Build(actions))
        {
            ct.ThrowIfCancellationRequested();
            if (step.Inputs is null)
            {
                Thread.Sleep(step.DelayMs);
                continue;
            }
            if (GetForegroundWindow() != targetWindow)
                throw new AutoTypeAbortedException("Hedef pencere değişti; Auto-Type durduruldu.");

            var sent = SendInput((uint)step.Inputs.Length, step.Inputs, Marshal.SizeOf<Input>());
            if (sent != step.Inputs.Length)
                throw new AutoTypeAbortedException("Klavye girdisi gönderilemedi (pencere yönetici olarak çalışıyor olabilir).");
            Thread.Sleep(keyDelayMs);
        }
    }

    /// <summary>Kısayol tuşları (Ctrl/Alt/Shift/Win) basılıyken yazmak karakterleri kısayola çevirir; bırakılmasını bekle.</summary>
    private static void WaitForModifiersReleased(TimeSpan timeout)
    {
        int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];   // Shift, Ctrl, Alt, LWin, RWin
        var deadline = DateTime.UtcNow + timeout;
        while (modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0))
        {
            if (DateTime.UtcNow > deadline)
                throw new AutoTypeAbortedException("Kısayol tuşları bırakılmadı; Auto-Type iptal edildi.");
            Thread.Sleep(20);
        }
        Thread.Sleep(50);
    }

    private static Input[] UnicodePress(char c) =>
    [
        Keyboard(0, c, KeyEventUnicode),
        Keyboard(0, c, KeyEventUnicode | KeyEventKeyUp),
    ];

    private static Input[] KeyPress(AutoTypeKey key)
    {
        var (vk, extended) = VirtualKey(key);
        var flags = extended ? KeyEventExtendedKey : 0;
        return [Keyboard(vk, 0, flags), Keyboard(vk, 0, flags | KeyEventKeyUp)];
    }

    internal static (ushort Vk, bool Extended) VirtualKey(AutoTypeKey key) => key switch
    {
        AutoTypeKey.Tab => (0x09, false),
        AutoTypeKey.Enter => (0x0D, false),
        AutoTypeKey.Space => (0x20, false),
        AutoTypeKey.Backspace => (0x08, false),
        AutoTypeKey.Escape => (0x1B, false),
        AutoTypeKey.Delete => (0x2E, true),
        AutoTypeKey.Up => (0x26, true),
        AutoTypeKey.Down => (0x28, true),
        AutoTypeKey.Left => (0x25, true),
        AutoTypeKey.Right => (0x27, true),
        AutoTypeKey.Home => (0x24, true),
        AutoTypeKey.End => (0x23, true),
        >= AutoTypeKey.F1 and <= AutoTypeKey.F12 => ((ushort)(0x70 + (key - AutoTypeKey.F1)), false),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static Input Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = vk, ScanCode = scan, Flags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // SendInput, INPUT boyutunun en büyük üye (MOUSEINPUT) ile hesaplanmasını bekler.
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vk);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();
}
