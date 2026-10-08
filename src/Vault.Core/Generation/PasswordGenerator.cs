using System.Security.Cryptography;

namespace Vault.Core.Generation;

public sealed record PasswordOptions
{
    public int Length { get; init; } = 20;
    public bool Uppercase { get; init; } = true;
    public bool Lowercase { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Symbols { get; init; } = true;
    /// <summary>Birbirine benzeyen karakterleri (0/O, 1/l/I vb.) dışarıda bırakır.</summary>
    public bool ExcludeAmbiguous { get; init; }
}

public static class PasswordGenerator
{
    public const int MinLength = 4;
    public const int MaxLength = 512;

    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.<>/?~";
    private const string AmbiguousChars = "0O1lI|";

    /// <summary>Kriptografik rastgele parola üretir; seçilen her karakter grubundan en az bir tane içerir.</summary>
    public static string Generate(PasswordOptions options)
    {
        var groups = GetGroups(options);
        if (groups.Count == 0)
            throw new ArgumentException("En az bir karakter grubu seçilmeli.");
        if (options.Length < Math.Max(MinLength, groups.Count) || options.Length > MaxLength)
            throw new ArgumentException($"Parola uzunluğu {Math.Max(MinLength, groups.Count)}-{MaxLength} arasında olmalı.");

        var pool = string.Concat(groups);
        var chars = new char[options.Length];

        for (var i = 0; i < groups.Count; i++)
            chars[i] = Pick(groups[i]);
        for (var i = groups.Count; i < chars.Length; i++)
            chars[i] = Pick(pool);

        // Zorunlu karakterler başta kalmasın diye Fisher-Yates karıştırması
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    /// <summary>Yaklaşık entropi (bit).</summary>
    public static double EstimateEntropyBits(PasswordOptions options)
    {
        var poolSize = GetGroups(options).Sum(g => g.Length);
        return poolSize == 0 ? 0 : options.Length * Math.Log2(poolSize);
    }

    private static List<string> GetGroups(PasswordOptions o)
    {
        var groups = new List<string>(4);
        if (o.Uppercase) groups.Add(UpperChars);
        if (o.Lowercase) groups.Add(LowerChars);
        if (o.Digits) groups.Add(DigitChars);
        if (o.Symbols) groups.Add(SymbolChars);

        if (o.ExcludeAmbiguous)
            groups = groups.Select(g => new string(g.Where(c => !AmbiguousChars.Contains(c)).ToArray())).ToList();

        return groups;
    }

    private static char Pick(string chars) => chars[RandomNumberGenerator.GetInt32(chars.Length)];
}
