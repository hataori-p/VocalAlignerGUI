using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Frontend.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.Green;
    public IBrush FalseBrush { get; set; } = Brushes.Gray;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool flag && flag ? TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
