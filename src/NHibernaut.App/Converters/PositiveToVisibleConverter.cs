using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NHibernaut.App.Converters;

/// <summary>Converts int > 0 to true (for IsVisible bindings on collection counts).</summary>
public sealed class PositiveToVisibleConverter : IValueConverter
{
    public static readonly PositiveToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
