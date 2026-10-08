namespace Vault.Cli;

internal sealed class UsageException(string message) : Exception(message);

/// <summary>Basit argüman ayrıştırıcı: komut, konumsal argümanlar, "--ad değer", "--ad=değer" ve bayraklar.</summary>
internal sealed class CliArgs
{
    // Değer almayan seçenekler
    private static readonly HashSet<string> FlagNames =
    [
        "reveal", "generate", "yes", "force", "watch", "password-stdin", "clear-totp", "help",
        "no-symbols", "no-digits", "no-upper", "no-lower", "exclude-ambiguous",
    ];

    // Değer alan seçenekler
    private static readonly HashSet<string> ValueOptionNames =
    [
        "vault", "title", "url", "username", "password", "length", "totp", "totp-digits", "totp-period",
        "totp-algorithm", "notes", "tag", "match", "format",
    ];

    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }
    public List<string> Positionals { get; } = [];

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "/?")
                arg = "--help";

            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg == "--")
            {
                if (result.Command is null)
                    result.Command = arg.ToLowerInvariant();
                else
                    result.Positionals.Add(arg);
                continue;
            }

            var name = arg[2..];
            string? value = null;
            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                value = name[(eq + 1)..];
                name = name[..eq];
            }

            if (FlagNames.Contains(name))
            {
                if (value is not null)
                    throw new UsageException($"--{name} bir değer almaz.");
                result._flags.Add(name);
                continue;
            }

            if (!ValueOptionNames.Contains(name))
                throw new UsageException($"Bilinmeyen seçenek: --{name}. Yardım için: localvault-cli help");

            if (value is null)
            {
                if (i + 1 >= args.Length)
                    throw new UsageException($"--{name} için bir değer gerekli.");
                value = args[++i];
            }

            if (!result._options.TryGetValue(name, out var list))
                result._options[name] = list = [];
            list.Add(value);
        }
        return result;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public bool HasOption(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> GetAll(string name) => _options.TryGetValue(name, out var list) ? list : [];

    public int? GetInt(string name)
    {
        var value = Get(name);
        if (value is null)
            return null;
        return int.TryParse(value, out var n) ? n : throw new UsageException($"--{name} bir sayı olmalı.");
    }

    /// <summary>Komutun tanımadığı seçenek/bayrak verilmişse hata verir (yazım hatalarını yakalamak için).</summary>
    public void AllowOnly(params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) { "vault", "password-stdin", "help" };
        var unknown = _options.Keys.Concat(_flags).FirstOrDefault(n => !allowed.Contains(n));
        if (unknown is not null)
            throw new UsageException($"'{Command}' komutu --{unknown} seçeneğini desteklemiyor.");
    }

    public string RequirePositional(int index, string what) =>
        index < Positionals.Count ? Positionals[index] : throw new UsageException($"{what} belirtilmeli.");
}
