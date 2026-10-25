using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace DailyPlan.Services;

public sealed record UpdateOffer(string Version);

public interface IAppUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<UpdateOffer?> CheckAsync();
    Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
    void PrepareRestart();
}

public sealed class AppUpdateBackend : IAppUpdateBackend
{
    public const string RepositoryUrl = "https://github.com/SDXS2018/day-plan";
    private readonly UpdateManager _manager;
    private UpdateInfo? _offer;
    private readonly string[]? _restartArgs;
    public AppUpdateBackend() : this(new UpdateManager(new GithubSource(RepositoryUrl, null, false), new UpdateOptions { ExplicitChannel = "win" })) { }
    internal AppUpdateBackend(UpdateManager manager, string[]? restartArgs = null) { _manager = manager; _restartArgs = restartArgs; }
    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";
    public async Task<UpdateOffer?> CheckAsync()
    {
        _offer = await _manager.CheckForUpdatesAsync();
        return _offer == null ? null : new UpdateOffer(_offer.TargetFullRelease.Version.ToString());
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
        => _manager.DownloadUpdatesAsync(_offer ?? throw new InvalidOperationException("请先检查更新。"), progress, cancellation);
    public void PrepareRestart()
    {
        var ready = _manager.UpdatePendingRestart ?? throw new InvalidOperationException("请先下载更新。" );
        _manager.WaitExitThenApplyUpdates(ready, silent: true, restart: true, restartArgs: _restartArgs);
    }
}
