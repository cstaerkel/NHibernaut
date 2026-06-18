using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NHibernaut.App.Converters;

public sealed class DiffStateToBrushConverter : IValueConverter
{
    public static readonly DiffStateToBrushConverter Instance = new();

    private static readonly IImmutableSolidColorBrush Better = new ImmutableSolidColorBrush(Color.Parse("#4ec9b0"));
    private static readonly IImmutableSolidColorBrush Worse = new ImmutableSolidColorBrush(Color.Parse("#f48771"));

    // "Same" returns UnsetValue so Foreground falls back to the theme's default foreground
    // (dark on light, light on dark) rather than a fixed grey that's low-contrast on a light background.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Better" => Better,
        "Worse" => Worse,
        _ => AvaloniaProperty.UnsetValue,
    };

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
