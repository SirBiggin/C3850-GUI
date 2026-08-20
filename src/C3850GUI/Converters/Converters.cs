using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace C3850GUI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility.Visible;
}
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not Visibility.Visible;
}
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value == null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#2E8BFF")); }
        catch { return Brushes.DodgerBlue; }
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
public class PortStatusBrushConverter : IValueConverter
{
    public static readonly Brush Up = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
    public static readonly Brush Down = new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
    public static readonly Brush Disabled = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30));
    public static readonly Brush Err = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x48));
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var s = (value?.ToString() ?? "").ToLowerInvariant();
        if (s.Contains("err")) return Err;
        if (s.Contains("connected") && !s.Contains("not")) return Up;
        if (s.Contains("disabled")) return Disabled;
        return Down;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
