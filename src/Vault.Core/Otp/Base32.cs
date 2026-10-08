using System.Text;

namespace Vault.Core.Otp;

/// <summary>RFC 4648 Base32. Çözerken boşluk, tire, büyük/küçük harf ve eksik dolguya toleranslıdır.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data, bool padding = true)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);

        if (padding)
            while (sb.Length % 8 != 0)
                sb.Append('=');

        return sb.ToString();
    }

    public static byte[] Decode(string input) =>
        TryDecode(input, out var result)
            ? result
            : throw new FormatException("Geçersiz Base32 değeri.");

    public static bool TryDecode(string? input, out byte[] result)
    {
        result = [];
        if (input is null)
            return false;

        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;
        var paddingStarted = false;

        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '\t')
                continue;
            if (raw == '=')
            {
                paddingStarted = true;
                continue;
            }
            if (paddingStarted)
                return false;

            var index = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0)
                return false;

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        result = [.. output];
        return true;
    }
}
