using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NHibernaut.App.Converters;

/// <summary>Converts bool IsSlow → error brush (slow), or the themed default foreground (normal).</summary>
public sealed class SlowToBrushConverter : IValueConverter
{
    public static readonly SlowToBrushConverter Instance = new();

    private static readonly IImmutableSolidColorBrush Slow = new ImmutableSolidColorBrush(Color.Parse("#f48771"));

    // Normal text returns UnsetValue so Foreground falls back to the theme's default
    // (dark on light, light on dark) instead of a fixed grey that washes out on a light background.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Slow : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
