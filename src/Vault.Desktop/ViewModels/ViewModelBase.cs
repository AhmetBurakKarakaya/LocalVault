using CommunityToolkit.Mvvm.ComponentModel;
using Vault.Core.Generation;

namespace Vault.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject;

/// <summary>ComboBox'larda değer + Türkçe etiket göstermek için.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal static class Display
{
    public static string StrengthLabel(StrengthLevel level) => level switch
    {
        StrengthLevel.VeryWeak => "Çok zayıf",
        StrengthLevel.Weak => "Zayıf",
        StrengthLevel.Fair => "Orta",
        StrengthLevel.Strong => "Güçlü",
        _ => "Çok güçlü",
    };

    /// <summary>"123456" → "123 456", "12345678" → "1234 5678"</summary>
    public static string FormatOtp(string code) =>
        code.Length >= 6 ? $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]}" : code;

    public static string Initial(string title) =>
        string.IsNullOrWhiteSpace(title) ? "?" : char.ToUpper(title.Trim()[0], System.Globalization.CultureInfo.CurrentCulture).ToString();

    /// <summary>Başlığa göre sabit bir avatar renk indeksi (0-7).</summary>
    public static int ColorIndex(string title)
    {
        // FNV-1a: süreçler arasında kararlı (string.GetHashCode rastgeledir) ve iyi dağılır.
        var hash = 2166136261u;
        foreach (var c in title)
            hash = unchecked((hash ^ c) * 16777619u);
        return (int)(hash % 8);
    }
}
