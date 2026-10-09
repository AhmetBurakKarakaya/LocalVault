using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Vault.Desktop.Views;

public static class Converters
{
    private static readonly IBrush[] AvatarBrushes =
    [
        Brush("#2563EB"), Brush("#7C3AED"), Brush("#DB2777"), Brush("#EA580C"),
        Brush("#059669"), Brush("#0891B2"), Brush("#CA8A04"), Brush("#4F46E5"),
    ];

    private static readonly IBrush[] StrengthBrushes =
    [
        Brush("#9CA3AF"), Brush("#DC2626"), Brush("#EA580C"), Brush("#CA8A04"), Brush("#16A34A"), Brush("#15803D"),
    ];

    /// <summary>Avatar renk indeksi (0-7) → fırça.</summary>
    public static IValueConverter AvatarBrush { get; } =
        new FuncValueConverter<int, IBrush>(i => AvatarBrushes[Math.Clamp(i, 0, AvatarBrushes.Length - 1)]);

    /// <summary>Parola gücü (0-5) → fırça.</summary>
    public static IValueConverter StrengthBrush { get; } =
        new FuncValueConverter<int, IBrush>(i => StrengthBrushes[Math.Clamp(i, 0, StrengthBrushes.Length - 1)]);

    public static IValueConverter EyeIcon { get; } = new BoolToResourceConverter("IconEyeOff", "IconEye");

    /// <summary>Kaynak adı (ör. "IconTag") → uygulama kaynağındaki geometri.</summary>
    public static IValueConverter ResourceIcon { get; } =
        new FuncValueConverter<string?, object?>(key =>
            key is not null && Avalonia.Application.Current?.TryGetResource(key, null, out var resource) == true ? resource : null);

    private static IBrush Brush(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));

    /// <summary>bool → uygulama kaynağındaki iki geometriden biri.</summary>
    private sealed class BoolToResourceConverter(string whenTrue, string whenFalse) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var key = value is true ? whenTrue : whenFalse;
            return Avalonia.Application.Current?.TryGetResource(key, null, out var resource) == true ? resource : null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
