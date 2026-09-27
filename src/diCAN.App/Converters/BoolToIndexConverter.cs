using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DiCAN.App.Converters;

// Converts bound UI values.
public sealed class BoolToIndexConverter : IValueConverter
{

    public static readonly BoolToIndexConverter Instance = new();

    // Converts the requested value.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag
            ? (flag ? 1 : 0)

            : BindingOperations.DoNothing;

    // Converts back.
    public object ConvertBack(
        object? value, Type targetType, object? parameter, CultureInfo culture) =>

        value is int index && index >= 0
            ? index == 1
            : BindingOperations.DoNothing;
}
