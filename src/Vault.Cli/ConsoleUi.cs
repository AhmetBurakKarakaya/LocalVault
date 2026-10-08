using System.Text;

namespace Vault.Cli;

internal static class ConsoleUi
{
    private static TextReader? _stdin;

    /// <summary>Yönlendirilmiş stdin'den bir satır okur (--password-stdin için).</summary>
    public static string ReadStdinLine(string what)
    {
        _stdin ??= Console.In;
        return _stdin.ReadLine() ?? throw new UsageException($"stdin'den {what} okunamadı.");
    }

    /// <summary>Girilen karakterleri ekrana yazmadan parola okur.</summary>
    public static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
            throw new UsageException("Etkileşimli olmayan modda parola sorulamaz; --password-stdin kullanın.");

        Console.Error.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                    Console.Error.Write("\b \b");
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Error.Write('*');
            }
        }
        Console.Error.WriteLine();
        return sb.ToString();
    }

    public static bool Confirm(string question)
    {
        if (Console.IsInputRedirected)
            return false;
        Console.Error.Write($"{question} [e/H]: ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "e" or "evet" or "y" or "yes";
    }

    public static void Error(string message) => WriteColored(Console.Error, ConsoleColor.Red, message);

    public static void Warning(string message) => WriteColored(Console.Error, ConsoleColor.Yellow, message);

    public static void Success(string message) => WriteColored(Console.Out, ConsoleColor.Green, message);

    public static void Info(string message) => Console.Error.WriteLine(message);

    private static void WriteColored(TextWriter writer, ConsoleColor color, string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        writer.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
