using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NHibernaut.App.Converters;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public static readonly SeverityToBrushConverter Instance = new();

    private static readonly IImmutableSolidColorBrush Error = new ImmutableSolidColorBrush(Color.Parse("#f48771"));
    private static readonly IImmutableSolidColorBrush Warning = new ImmutableSolidColorBrush(Color.Parse("#d7ba7d"));
    private static readonly IImmutableSolidColorBrush Info = new ImmutableSolidColorBrush(Color.Parse("#569cd6"));
    private static readonly IImmutableSolidColorBrush None = new ImmutableSolidColorBrush(Color.Parse("#555555"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Error" => Error,
        "Warning" => Warning,
        "Info" => Info,
        _ => None,
    };
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
