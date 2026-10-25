using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DailyPlan.Services;
using DailyPlan.ViewModels;
using DailyPlan.Models;
using Velopack;

namespace DailyPlan;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private TrayService? _tray;
    private MainWindow? _window;
    private HistoryWindow? _historyWindow;
    private UpdateWindow? _updateWindow;
    private AppUpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private MainViewModel? _viewModel;
    private AppSettings? _currentSettings;
    private readonly SettingsService _settingsService = new();

    [STAThread]
    public static void Main(string[] args)
    {
        // Installer/update hooks must exit before WPF and the single-instance mutex start.
        VelopackApp.Build().Run();
        var app = new App();
        var diagnostic = Array.IndexOf(args, "--verify-history");
        if (diagnostic >= 0 && diagnostic + 1 < args.Length)
        {
            var folder = args[diagnostic + 1];
            app.DispatcherUnhandledException += (_, error) =>
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "dispatcher-error.txt"), error.Exception.ToString());
                error.Handled = true;
                app.ExitApplication();
            };
        }
        app.InitializeComponent();
        app.Run();
    }

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
            var updateTest = Array.IndexOf(e.Args, "--verify-update");
            var updateCompleted = Array.IndexOf(e.Args, "--verify-update-completed");
            var updateTestIndex = updateTest >= 0 ? updateTest : updateCompleted;
            if (updateTestIndex >= 0 && updateTestIndex + 1 < e.Args.Length)
            {
                _ = Infrastructure.UpdaterIntegrationVerification.RunAsync(e.Args[updateTestIndex + 1], updateCompleted >= 0, ExitApplication);
                return;
            }
            // This tiny vector widget uses substantially less memory with software composition on this PC.
            // A hardware comparison remains available for diagnostics on other machines.
            if (!e.Args.Contains("--hardware-render"))
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            // Keep the font outside the managed assembly so Windows can map it from disk.
            var fontFolder = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
            Resources["AppFont"] = new System.Windows.Media.FontFamily(fontFolder, "./#Plan Rounded");
            var settings = _settingsService.Load();
            _currentSettings = settings;
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
            _viewModel = viewModel;
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
                showHistory: () => ShowHistory(viewModel, 0),
                showUpdates: ShowUpdates);
            _window.SettingsRequested += (_, _) => _tray.ShowMenu();
            _window.HasOpenSettingsMenu = () => _tray?.IsMenuOpen == true;
            _window.SettingsChanged += (_, _) =>
            {
                try { _settingsService.Save(settings); }
                catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "位置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            MainWindow = _window;
            _window.Show();
            if (verificationOutput == null)
            {
                var backend = new AppUpdateBackend();
                if (backend.IsInstalled) StartupService.MigrateExistingRegistration();
                _updates = new AppUpdateService(backend, lastAutomaticAttempt: UpdateCheckStore.Load(), rememberCheck: UpdateCheckStore.Save);
                _updates.PropertyChanged += (_, _) =>
                {
                    _window.SetUpdateAvailable(_updates.HasUpdate);
                    _tray?.SetUpdateText(_updates.MenuText, _updates.HasUpdate);
                };
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
                _updateTimer.Tick += async (_, _) => { if (_updates != null) await _updates.CheckAsync(automatic: true); };
                _updateTimer.Start();
                _ = CheckAfterStartupAsync();
            }
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

    private async Task CheckAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (_updates != null) await _updates.CheckAsync(automatic: true);
    }

    private void ShowUpdates()
    {
        if (_updates == null) return;
        if (_updateWindow != null) { _updateWindow.Activate(); return; }
        _window?.SetExpanded(false);
        var window = new UpdateWindow(_updates, PrepareUpdate, ExitApplication);
        _updateWindow = window;
        window.Closed += (_, _) => _updateWindow = null;
        window.Show();
        window.Activate();
        _ = _updates.CheckAsync();
    }

    private bool PrepareUpdate()
    {
        if (_viewModel != null && (!string.IsNullOrWhiteSpace(_viewModel.NewTaskText) || _viewModel.Items.Any(task => task.IsEditing)))
        {
            MessageBox.Show("请先保存正在新增或编辑的任务，再点击重启更新。", "还有未保存内容", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_viewModel != null && !_viewModel.SaveBeforeUpdate())
        {
            MessageBox.Show(_viewModel.StatusMessage, "任务尚未保存，暂不更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try { if (_currentSettings != null) _settingsService.Save(_currentSettings); return true; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "暂时无法保存设置", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
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
        _updateTimer?.Stop();
        _updates?.Dispose();
        _updates = null;
        _updateWindow?.Close();
        _historyWindow?.Close();
        _window?.AllowExit();
        _tray?.Dispose();
        _tray = null;
        _window?.Close();
        Shutdown();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _updates?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
    }
}
