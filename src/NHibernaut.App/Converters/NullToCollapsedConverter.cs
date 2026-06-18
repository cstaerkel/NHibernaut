using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NHibernaut.App.Converters;

/// <summary>Converts non-null value to true (for IsVisible). For strings, also requires non-empty.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? !string.IsNullOrEmpty(s) : value is not null;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
