using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using DailyPlan.Services;

namespace DailyPlan;

public partial class UpdateWindow : Window
{
    private readonly AppUpdateService _updates;
    private readonly Func<bool> _prepare;
    private readonly Action _exit;
    public UpdateWindow(AppUpdateService updates, Func<bool> prepare, Action exit)
    {
        InitializeComponent(); _updates = updates; _prepare = prepare; _exit = exit;
        DataContext = updates;
        Closed += (_, _) => DataContext = null;
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Stage == UpdateStage.Unsupported)
            Process.Start(new ProcessStartInfo(AppUpdateBackend.RepositoryUrl + "/releases/latest") { UseShellExecute = true });
        else if (_updates.Stage == UpdateStage.Ready)
        {
            if (_updates.PrepareRestart(_prepare)) _exit();
        }
        else if (_updates.HasUpdate) await _updates.DownloadAsync();
        else await _updates.CheckAsync();
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
