using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NHibernaut.App.Converters;

/// <summary>Inverts a boolean value; used to toggle visibility of mutually-exclusive panels.</summary>
public sealed class BoolInverseConverter : IValueConverter
{
    public static readonly BoolInverseConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
