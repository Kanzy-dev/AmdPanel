using System.Globalization;
using System.Windows.Data;

namespace AmdPanel.App.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v ? v : System.Convert.ToBoolean(value);
        // Параметр "invert" инвертирует логику
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>
/// Convert: bool IsSupported → bool IsEnabled для контролов.
/// Parameter: зависимость от GpuCapabilities.IsSupported("FeatureName").
/// На UI передаётся напрямую boolean из VM, поэтому converter только пробрасывает.
/// </summary>
public sealed class CapabilityToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b;
        if (value is int i) return i != 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? b : false;
}

/// <summary>
/// Конвертация "если значение не null и больше 0 → виден warning".
/// Используется для отображения warning'а о превышении bandwidth порта.
/// </summary>
public sealed class BandwidthWarningConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b ? b : (value is int i && i != 0);
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Convert: null → false, non-null → true.
/// Используется для IsEnabled редактора Program Settings: пока не выбрано
/// приложение в ListBox, кнопки и поля редактирования недоступны.
/// </summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
