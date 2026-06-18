using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NHibernaut.App.Converters;

public sealed class KindToBrushConverter : IValueConverter
{
    public static readonly KindToBrushConverter Instance = new();

    private static readonly IImmutableSolidColorBrush Slow   = new ImmutableSolidColorBrush(Color.Parse("#f48771"));
    private static readonly IImmutableSolidColorBrush Warn   = new ImmutableSolidColorBrush(Color.Parse("#d7ba7d"));
    private static readonly IImmutableSolidColorBrush Info   = new ImmutableSolidColorBrush(Color.Parse("#569cd6"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Slow"   => Slow,
        "Insert" => Warn,
        "Update" => Warn,
        "Delete" => Warn,
        _        => Info,   // Select, Other, unknown
    };

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
