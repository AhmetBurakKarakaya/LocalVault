namespace Vault.Desktop.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Genel kısayol (ör. "Ctrl+Alt+A"). Ayarlarda metin olarak saklanır; Windows'ta RegisterHotKey'e
/// verilecek değiştirici ve sanal tuş koduna çevrilir. Harf, rakam ve F1–F24 tuşlarını destekler.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public const string DefaultText = "Ctrl+Alt+A";
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "A");

    // Windows'un kendine ayırdığı ya da çok yaygın kullanılan birleşimler.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+F4", "Win+L", "Win+D", "Win+E", "Win+R", "Win+V", "Win+X", "Win+I", "Win+S", "Win+A", "Win+N", "Win+Tab",
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Ctrl+S", "Ctrl+F", "Ctrl+P", "Ctrl+N", "Ctrl+T", "Ctrl+W",
    };

    // Türkçe Q klavyede AltGr (= Ctrl+Alt) ile yazılan karakterler.
    private static readonly Dictionary<string, string> AltGrCharacters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Q"] = "@", ["E"] = "€", ["1"] = ">", ["2"] = "£", ["3"] = "#", ["4"] = "$", ["5"] = "½",
        ["7"] = "{", ["8"] = "[", ["9"] = "]", ["0"] = "}",
    };

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>"Ctrl+Alt+A" biçimini çözer; geçersizse null.</summary>
    public static HotkeyGesture? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = raw.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "META" or "WINDOWS" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
                continue;
            }
            if (key is not null || NormalizeKey(raw) is not { } normalized)
                return null;
            key = normalized;
        }
        return key is null ? null : new HotkeyGesture(modifiers, key);
    }

    /// <summary>Ayarlardaki değeri çözer; boş veya bozuksa varsayılanı döndürür.</summary>
    public static HotkeyGesture FromSettings(string? text) => TryParse(text) ?? Default;

    /// <summary>Kısayol olarak kullanılamıyorsa nedenini döndürür (null = uygun).</summary>
    public string? Validate()
    {
        if (!Modifiers.HasFlag(HotkeyModifiers.Ctrl) && !Modifiers.HasFlag(HotkeyModifiers.Alt) && !Modifiers.HasFlag(HotkeyModifiers.Win))
            return "Kısayolda Ctrl, Alt veya Win tuşlarından en az biri olmalı; yoksa normal yazmayı engeller.";
        if (Reserved.Contains(ToString()))
            return $"{this} Windows'ta veya uygulamalarda zaten kullanılıyor; başka bir birleşim seçin.";
        return null;
    }

    /// <summary>Kullanılabilir ama dikkat edilmesi gereken durumlar (null = uyarı yok).</summary>
    public string? Warning() =>
        Modifiers == (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt) && AltGrCharacters.TryGetValue(Key, out var character)
            ? $"Türkçe klavyede AltGr+{Key} ile yazılan \"{character}\" karakteri bu kısayol yüzünden yazılamayabilir."
            : null;

    /// <summary>Windows RegisterHotKey değiştiricileri (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8).</summary>
    public uint Win32Modifiers =>
        (Modifiers.HasFlag(HotkeyModifiers.Alt) ? 0x1u : 0) |
        (Modifiers.HasFlag(HotkeyModifiers.Ctrl) ? 0x2u : 0) |
        (Modifiers.HasFlag(HotkeyModifiers.Shift) ? 0x4u : 0) |
        (Modifiers.HasFlag(HotkeyModifiers.Win) ? 0x8u : 0);

    /// <summary>Windows sanal tuş kodu: harf/rakam ASCII kodudur, F1 = 0x70 … F24 = 0x87.</summary>
    public uint Win32VirtualKey => Key.Length == 1 ? Key[0] : 0x70u + uint.Parse(Key.AsSpan(1)) - 1;

    /// <summary>Tuş adını "A", "7", "F8" biçimine getirir; desteklenmiyorsa null.</summary>
    public static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var upper = key.Trim().ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            return upper;
        if (upper.Length == 2 && upper[0] == 'D' && char.IsAsciiDigit(upper[1]))
            return upper[1..];   // Avalonia'nın rakam tuşu adları: D0 … D9
        if (upper.StartsWith('F') && int.TryParse(upper.AsSpan(1), out var number) && number is >= 1 and <= 24)
            return $"F{number}";
        return null;
    }
}
