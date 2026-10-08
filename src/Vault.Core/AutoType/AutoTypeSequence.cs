using System.Globalization;
using System.Text;
using Vault.Core.Models;
using Vault.Core.Otp;

namespace Vault.Core.AutoType;

public enum AutoTypeKey
{
    Tab, Enter, Space, Backspace, Escape, Delete, Up, Down, Left, Right, Home, End,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

/// <summary>Auto-Type'ın gönderdiği tek bir adım: metin, özel tuş veya bekleme.</summary>
public abstract record AutoTypeAction
{
    public sealed record Text(string Value) : AutoTypeAction
    {
        // Parolalar hata ayıklama çıktısına veya günlüklere sızmasın.
        public override string ToString() => $"Text({Value.Length} karakter)";
    }

    public sealed record Key(AutoTypeKey Value, int Count = 1) : AutoTypeAction;

    public sealed record Delay(int Milliseconds) : AutoTypeAction;
}

/// <summary>
/// KeePass benzeri Auto-Type dizisi: <c>{USERNAME}{TAB}{PASSWORD}{ENTER}</c>.
/// Yer tutucular: {USERNAME} {PASSWORD} {TOTP} {TITLE} {URL}.
/// Tuşlar: {TAB} {ENTER} {SPACE} {BACKSPACE}/{BS} {ESC} {DELETE}/{DEL} {UP} {DOWN} {LEFT} {RIGHT} {HOME} {END} {F1}…{F12}.
/// Tekrar: {TAB 3}. Bekleme: {DELAY 500} (ms). Süslü parantez: {{} ve {}}.
/// Diğer her şey olduğu gibi yazılır.
/// </summary>
public static class AutoTypeSequence
{
    public const string Default = "{USERNAME}{TAB}{PASSWORD}{ENTER}";
    private const int MaxDelayMs = 10_000;
    private const int MaxRepeat = 100;

    private static readonly Dictionary<string, AutoTypeKey> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TAB"] = AutoTypeKey.Tab, ["ENTER"] = AutoTypeKey.Enter, ["SPACE"] = AutoTypeKey.Space,
        ["BACKSPACE"] = AutoTypeKey.Backspace, ["BS"] = AutoTypeKey.Backspace, ["ESC"] = AutoTypeKey.Escape,
        ["DELETE"] = AutoTypeKey.Delete, ["DEL"] = AutoTypeKey.Delete, ["UP"] = AutoTypeKey.Up,
        ["DOWN"] = AutoTypeKey.Down, ["LEFT"] = AutoTypeKey.Left, ["RIGHT"] = AutoTypeKey.Right,
        ["HOME"] = AutoTypeKey.Home, ["END"] = AutoTypeKey.End,
        ["F1"] = AutoTypeKey.F1, ["F2"] = AutoTypeKey.F2, ["F3"] = AutoTypeKey.F3, ["F4"] = AutoTypeKey.F4,
        ["F5"] = AutoTypeKey.F5, ["F6"] = AutoTypeKey.F6, ["F7"] = AutoTypeKey.F7, ["F8"] = AutoTypeKey.F8,
        ["F9"] = AutoTypeKey.F9, ["F10"] = AutoTypeKey.F10, ["F11"] = AutoTypeKey.F11, ["F12"] = AutoTypeKey.F12,
    };

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "USERNAME", "PASSWORD", "TOTP", "TITLE", "URL",
    };

    /// <summary>Diziyi doğrular; hatalıysa açıklama, değilse null döner.</summary>
    public static string? Validate(string? sequence)
    {
        try
        {
            Compile(sequence, _ => "");
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Kayıt için diziyi derler; {TOTP} anlık kodla doldurulur.</summary>
    public static IReadOnlyList<AutoTypeAction> Compile(VaultEntry entry, DateTimeOffset? now = null) =>
        Compile(string.IsNullOrWhiteSpace(entry.AutoTypeSequence) ? Default : entry.AutoTypeSequence, name =>
            name.ToUpperInvariant() switch
            {
                "USERNAME" => entry.Username,
                "PASSWORD" => entry.Password,
                "TITLE" => entry.Title,
                "URL" => entry.Urls.FirstOrDefault() ?? "",
                "TOTP" => entry.Totp is null
                    ? throw new FormatException("Dizide {TOTP} var ama kayıtta doğrulama kodu tanımlı değil.")
                    : OtpGenerator.ComputeTotp(entry.Totp, now ?? DateTimeOffset.UtcNow).Code,
                _ => throw new FormatException($"Bilinmeyen yer tutucu: {{{name}}}"),
            });

    public static IReadOnlyList<AutoTypeAction> Compile(string? sequence, Func<string, string> placeholder)
    {
        sequence = string.IsNullOrWhiteSpace(sequence) ? Default : sequence;
        var actions = new List<AutoTypeAction>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0)
                return;
            actions.Add(new AutoTypeAction.Text(text.ToString()));
            text.Clear();
        }

        for (var i = 0; i < sequence.Length; i++)
        {
            var c = sequence[i];
            if (c == '}')
                throw new FormatException($"Konum {i + 1}: eşleşmeyen '}}'. Süslü parantez yazmak için {{}} kullanın.");
            if (c != '{')
            {
                text.Append(c);
                continue;
            }

            // {{} ve {}} kaçışları
            if (i + 2 < sequence.Length && sequence[i + 2] == '}' && sequence[i + 1] is '{' or '}')
            {
                text.Append(sequence[i + 1]);
                i += 2;
                continue;
            }

            var end = sequence.IndexOf('}', i + 1);
            if (end < 0)
                throw new FormatException($"Konum {i + 1}: kapanmayan '{{'.");
            var token = sequence[(i + 1)..end].Trim();
            i = end;

            var space = token.IndexOf(' ');
            var name = space < 0 ? token : token[..space];
            var argument = space < 0 ? null : token[(space + 1)..].Trim();

            if (name.Equals("DELAY", StringComparison.OrdinalIgnoreCase))
            {
                FlushText();
                actions.Add(new AutoTypeAction.Delay(ParseNumber(argument, name, 0, MaxDelayMs)));
            }
            else if (Keys.TryGetValue(name, out var key))
            {
                FlushText();
                actions.Add(new AutoTypeAction.Key(key, argument is null ? 1 : ParseNumber(argument, name, 1, MaxRepeat)));
            }
            else if (Placeholders.Contains(name) && argument is null)
            {
                text.Append(placeholder(name));
            }
            else
            {
                throw new FormatException($"Bilinmeyen komut: {{{token}}}");
            }
        }

        FlushText();
        return actions;
    }

    private static int ParseNumber(string? value, string name, int min, int max)
    {
        if (value is null || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new FormatException($"{{{name}}} için {min}-{max} arasında bir sayı gerekli.");
        return n;
    }
}
