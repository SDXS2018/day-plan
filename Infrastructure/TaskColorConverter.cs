using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DailyPlan.Models;

namespace DailyPlan.Infrastructure;

public sealed class TaskColorConverter : IValueConverter
{
    // Shared immutable brushes avoid creating a new brush every time a row refreshes.
    private static readonly Dictionary<string, SolidColorBrush> Brushes = TaskPalette.Colors.ToDictionary(
        color => color, color =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        });

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string color && Brushes.TryGetValue(color, out var brush) ? brush : Brushes[TaskPalette.Colors[0]];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
