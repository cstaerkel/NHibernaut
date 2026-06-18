using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace NHibernaut.App.Converters;

public sealed class HighlightToThicknessConverter : IValueConverter
{
    public static readonly HighlightToThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new Thickness(2) : new Thickness(0);

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
