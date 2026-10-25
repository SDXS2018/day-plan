using System.Threading;
using System.Windows;
using DailyPlan.Services;
using DailyPlan.ViewModels;

namespace DailyPlan;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private TrayService? _tray;
    private MainWindow? _window;
    private HistoryWindow? _historyWindow;
    private readonly SettingsService _settingsService = new();

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, name: @"Local\DailyPlan_OnlyOneInstance", createdNew: out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("DailyPlan 已在运行，请点击右下角托盘中的图标。", "DailyPlan",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            // This tiny vector widget uses substantially less memory with software composition on this PC.
            // A hardware comparison remains available for diagnostics on other machines.
            if (!e.Args.Contains("--hardware-render"))
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            // Keep the font outside the managed assembly so Windows can map it from disk.
            var fontFolder = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
            Resources["AppFont"] = new System.Windows.Media.FontFamily(fontFolder, "./#Plan Rounded");
            var settings = _settingsService.Load();
            var verification = Array.IndexOf(e.Args, "--verify-motion");
            var preview = Array.IndexOf(e.Args, "--verify-preview");
            var historyCheck = Array.IndexOf(e.Args, "--verify-history");
            var outputIndex = verification >= 0 ? verification : preview >= 0 ? preview : historyCheck;
            var verificationOutput = outputIndex >= 0 && outputIndex + 1 < e.Args.Length ? e.Args[outputIndex + 1] : null;
            var store = new TaskStore(verificationOutput == null ? null : Path.Combine(verificationOutput, "data"));
            if (verificationOutput != null)
                store.Save(new DailyPlan.Models.DailyTask[]
                {
                    new() { Title = "阅读十页文献" }, new() { Title = "核对临床试验数据" },
                    new() { Title = "学习 Python", IsCompleted = true, CompletedAt = DateTimeOffset.Now }, new() { Title = "散步二十分钟" },
                    new() { Title = "整理试验数据记录", Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-2), CreatedAt = DateTimeOffset.Now.AddDays(-2), IsCompleted = true, CompletedAt = DateTimeOffset.Now.AddDays(-1) },
                    new() { Title = "准备文献分享", Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-3), CreatedAt = DateTimeOffset.Now.AddDays(-3) },
                    new() { Title = "完成上周复习", Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-10), CreatedAt = DateTimeOffset.Now.AddDays(-10), IsCompleted = true, CompletedAt = DateTimeOffset.Now.AddDays(-8) }
                });
            var viewModel = new MainViewModel(store);
            _window = new MainWindow(viewModel, settings);
            // Expose the floating window to accessibility test tools when requested.
            _window.ShowInTaskbar = e.Args.Contains("--ui-test");
            _tray = new TrayService(
                owner: _window,
                show: () => _window.ShowAndExpand(),
                exit: ExitApplication,
                getAutoCollapse: () => _window.AutoCollapseOnDeactivated,
                setAutoCollapse: enabled =>
                {
                    _window.AutoCollapseOnDeactivated = enabled;
                    try { _settingsService.Save(settings); }
                    catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
                },
                showHistory: () => ShowHistory(viewModel, 0));
            _window.SettingsRequested += (_, _) => _tray.ShowMenu();
            _window.HasOpenSettingsMenu = () => _tray?.IsMenuOpen == true;
            _window.SettingsChanged += (_, _) =>
            {
                try { _settingsService.Save(settings); }
                catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "位置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            MainWindow = _window;
            _window.Show();
            if (verificationOutput != null)
                _ = historyCheck >= 0 ? DailyPlan.Infrastructure.HistoryVerification.RunAsync(_window, viewModel, _tray, days => ShowHistory(viewModel, days), verificationOutput, ExitApplication)
                    : preview >= 0 ? DailyPlan.Infrastructure.MotionVerification.PreviewAsync(_window, verificationOutput, ExitApplication)
                    : DailyPlan.Infrastructure.MotionVerification.RunAsync(_window, verificationOutput, ExitApplication);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("DailyPlan 启动失败：\n" + ex.Message, "DailyPlan",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private HistoryWindow ShowHistory(MainViewModel source, int days)
    {
        source.CheckDayRollover();
        if (_historyWindow != null)
        {
            _historyWindow.Model.SelectPeriod(days);
            _historyWindow.Show();
            _historyWindow.Activate();
            return _historyWindow;
        }
        _window!.SetExpanded(false);
        _window.Topmost = false;
        var history = new HistoryWindow(source, days) { Owner = _window };
        _historyWindow = history;
        history.Closed += (_, _) =>
        {
            _historyWindow = null;
            _window.Topmost = true;
        };
        history.Show();
        history.Activate();
        return history;
    }

    private void ExitApplication()
    {
        _historyWindow?.Close();
        _window?.AllowExit();
        _tray?.Dispose();
        _tray = null;
        _window?.Close();
        Shutdown();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
    }
}
