using DailyPlan.Infrastructure;

namespace DailyPlan.Services;

public enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed, Unsupported }

public sealed class AppUpdateService : ObservableObject, IDisposable
{
    private readonly IAppUpdateBackend _backend;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<DateTimeOffset>? _rememberCheck;
    private readonly CancellationTokenSource _shutdown = new();
    private UpdateStage _stage;
    private string _message = "检查是否有新版本";
    private int _progress;
    private DateTimeOffset? _lastAutomaticAttempt;
    private bool _disposed;
    public UpdateOffer? Offer { get; private set; }
    public UpdateStage Stage => _stage;
    public string Message => _message;
    public string CurrentVersion => _backend.CurrentVersion;
    public string VersionText => Offer == null ? $"当前版本 {CurrentVersion}" : $"{CurrentVersion} → {Offer.Version}";
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading;
    public bool HasUpdate => Offer != null;
    public bool IsDownloading => Stage == UpdateStage.Downloading;
    public bool CanAct => !IsBusy;
    public int Progress => _progress;
    public string ProgressText => $"下载中 {_progress}%";
    public string ActionText => Stage switch
    {
        UpdateStage.Checking => "正在检查…", UpdateStage.Downloading => "正在下载…",
        UpdateStage.Available => "下载更新", UpdateStage.Ready => "重启并更新",
        UpdateStage.Unsupported => "打开下载页面", UpdateStage.Failed when Offer != null => "重试下载",
        _ => "检查更新"
    };
    public string MenuText => Stage switch
    {
        UpdateStage.Available => $"发现新版本 {Offer!.Version}", UpdateStage.Ready => "重启并更新",
        UpdateStage.Downloading => ProgressText, _ => "检查更新"
    };

    public AppUpdateService(IAppUpdateBackend backend, Func<DateTimeOffset>? clock = null,
        DateTimeOffset? lastAutomaticAttempt = null, Action<DateTimeOffset>? rememberCheck = null)
    {
        _backend = backend; _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastAutomaticAttempt = lastAutomaticAttempt; _rememberCheck = rememberCheck;
        if (!backend.IsInstalled) SetState(UpdateStage.Unsupported, "此开发版或旧便携版未启用更新。请安装带自动更新的新版，原有任务会保留。" );
    }

    public async Task CheckAsync(bool automatic = false)
    {
        if (_disposed || IsBusy || Stage is UpdateStage.Available or UpdateStage.Ready or UpdateStage.Unsupported) return;
        if (automatic && _lastAutomaticAttempt is { } last && _clock() - last < TimeSpan.FromHours(24)) return;
        _lastAutomaticAttempt = _clock();
        try { _rememberCheck?.Invoke(_lastAutomaticAttempt.Value); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        SetState(UpdateStage.Checking, "正在连接更新服务…");
        try
        {
            Offer = await _backend.CheckAsync();
            if (_disposed) return;
            SetState(Offer == null ? UpdateStage.UpToDate : UpdateStage.Available,
                Offer == null ? "已经是最新版本" : $"发现 {Offer.Version}。下载时可以继续使用清单。");
        }
        catch (Exception) when (!_disposed)
        {
            SetState(UpdateStage.Failed, "暂时无法连接更新服务，请检查网络后重试。清单仍可正常使用。" );
        }
        catch (Exception) when (_disposed) { }
    }

    public async Task DownloadAsync()
    {
        if (_disposed || IsBusy || Offer == null || Stage == UpdateStage.Ready) return;
        _progress = 0;
        SetState(UpdateStage.Downloading, "正在下载，可关闭此窗口稍后再看。");
        var progress = new Progress<int>(value =>
        {
            if (_disposed || Stage != UpdateStage.Downloading) return;
            _progress = Math.Clamp(value, 0, 100); Notify();
        });
        try
        {
            await _backend.DownloadAsync(((IProgress<int>)progress).Report, _shutdown.Token);
            if (!_disposed) { _progress = 100; SetState(UpdateStage.Ready, "下载完成。保存好正在编辑的内容后，点击重启并更新。" ); }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception) when (!_disposed)
        {
            SetState(UpdateStage.Failed, "下载未完成，当前版本仍可正常使用。可以重试下载。" );
        }
        catch (Exception) when (_disposed) { }
    }

    public bool PrepareRestart(Func<bool> saveAndConfirm)
    {
        if (_disposed || Stage != UpdateStage.Ready || !saveAndConfirm()) return false;
        try { _backend.PrepareRestart(); return true; }
        catch (Exception) { SetState(UpdateStage.Ready, "暂时无法应用更新，请稍后再试。当前版本和任务未更改。" ); return false; }
    }

    private void SetState(UpdateStage stage, string message) { _stage = stage; _message = message; Notify(); }
    private void Notify()
    {
        foreach (var property in new[] { nameof(Stage), nameof(Message), nameof(VersionText), nameof(IsBusy), nameof(HasUpdate), nameof(IsDownloading), nameof(CanAct), nameof(Progress), nameof(ProgressText), nameof(ActionText), nameof(MenuText) }) OnPropertyChanged(property);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _shutdown.Cancel(); _shutdown.Dispose(); }
}

public static class UpdateCheckStore
{
    private static string FilePath => Path.Combine(TaskStore.DataFolder, "update-check.txt");
    public static DateTimeOffset? Load()
    {
        try { return DateTimeOffset.TryParse(File.ReadAllText(FilePath), out var value) ? value : null; }
        catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
    }
    public static void Save(DateTimeOffset value)
    {
        Directory.CreateDirectory(TaskStore.DataFolder);
        File.WriteAllText(FilePath, value.ToUniversalTime().ToString("O"));
    }
}
