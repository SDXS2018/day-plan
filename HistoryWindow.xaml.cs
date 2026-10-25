using System.Windows;
using System.Windows.Input;
using DailyPlan.ViewModels;

namespace DailyPlan;

public partial class HistoryWindow : Window
{
    public HistoryViewModel Model { get; }
    public HistoryWindow(MainViewModel source, int days)
    {
        InitializeComponent();
        Model = new HistoryViewModel(source, days);
        DataContext = Model;
        Closed += (_, _) => { Model.Dispose(); DataContext = null; };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDay day }) Model.SelectDay(day);
    }
    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void ToggleFullScreen() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleFullScreen();
        else if (WindowState == WindowState.Normal) DragMove();
    }
}
