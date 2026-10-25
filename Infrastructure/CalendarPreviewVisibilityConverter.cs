using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DailyPlan.Infrastructure;

public sealed class CalendarPreviewVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double height && height >= 80 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
