using DailyPlan.Services;
using System.IO;

var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks.Add("PASS: " + label); Console.WriteLine(checks[^1]); }
var clock = DateTimeOffset.UtcNow;
var fake = new FakeBackend();
using (var updates = new AppUpdateService(fake, () => clock))
{
    await updates.CheckAsync(automatic: true);
    Check(updates.Stage == UpdateStage.UpToDate && fake.Checks == 1, "No release means up-to-date without a notification");
    await updates.CheckAsync(automatic: true);
    Check(fake.Checks == 1, "Automatic check is throttled for 24 hours");
    await updates.CheckAsync();
    Check(fake.Checks == 2, "Manual check bypasses the daily throttle");
    clock = clock.AddHours(25); await updates.CheckAsync(automatic: true);
    Check(fake.Checks == 3, "Daily check becomes eligible again");
    fake.Offer = new UpdateOffer("1.2.0"); await updates.CheckAsync();
    Check(updates.HasUpdate && updates.Stage == UpdateStage.Available && updates.ActionText == "下载更新", "A new release creates a download action and badge");
    fake.FailDownload = true; await updates.DownloadAsync();
    Check(updates.Stage == UpdateStage.Failed && updates.HasUpdate && updates.ActionText == "重试下载", "Failed download retains the offer and allows retry");
    fake.FailDownload = false; await updates.DownloadAsync();
    Check(updates.Stage == UpdateStage.Ready && fake.Restarts == 0, "Successful download waits for an explicit restart");
    Check(!updates.PrepareRestart(() => false) && fake.Restarts == 0, "Unsaved editing blocks the restart");
    fake.FailApply = true;
    Check(!updates.PrepareRestart(() => true) && updates.Stage == UpdateStage.Ready, "Apply failure leaves the current application usable and retryable");
    fake.FailApply = false;
    Check(updates.PrepareRestart(() => true) && fake.Restarts == 1, "Restart is scheduled only after successful preparation");
}
using (var unsupported = new AppUpdateService(new FakeBackend { IsInstalled = false }))
{
    await unsupported.CheckAsync();
    Check(unsupported.Stage == UpdateStage.Unsupported && unsupported.ActionText == "打开下载页面", "Unmanaged builds explain the one-time installer migration");
}
var fail = new FakeBackend { FailCheck = true };
using (var offline = new AppUpdateService(fail))
{
    await offline.CheckAsync();
    Check(offline.Stage == UpdateStage.Failed && !offline.HasUpdate && offline.CanAct, "Offline errors do not claim a new version or block retry");
    fail.FailCheck = false; await offline.CheckAsync();
    Check(offline.Stage == UpdateStage.UpToDate, "Manual retry recovers from a connection error");
}
var pending = new TaskCompletionSource<UpdateOffer?>();
var concurrent = new FakeBackend { CheckGate = pending.Task };
using (var updates = new AppUpdateService(concurrent))
{
    var one = updates.CheckAsync();
    await updates.CheckAsync();
    Check(updates.IsBusy && concurrent.Checks == 1, "Concurrent checks share a single in-flight request");
    pending.SetResult(new UpdateOffer("1.2.0")); await one;
    concurrent.DownloadGate = new TaskCompletionSource();
    var download = updates.DownloadAsync(); await updates.DownloadAsync();
    Check(concurrent.Downloads == 1 && updates.IsDownloading, "Repeated download clicks do not create competing downloads");
    concurrent.DownloadGate.SetResult(); await download;
    Check(updates.Stage == UpdateStage.Ready, "The single download transitions to ready");
}
Console.WriteLine($"PASS: {checks.Count} update checks");
if (args.Length > 0) File.WriteAllLines(args[0], checks);

sealed class FakeBackend : IAppUpdateBackend
{
    public bool IsInstalled { get; set; } = true;
    public string CurrentVersion => "1.1.0";
    public UpdateOffer? Offer;
    public bool FailCheck, FailDownload, FailApply;
    public int Checks, Downloads, Restarts;
    public Task<UpdateOffer?>? CheckGate;
    public TaskCompletionSource? DownloadGate;
    public Task<UpdateOffer?> CheckAsync()
    {
        Checks++;
        if (FailCheck) throw new IOException("offline");
        return CheckGate ?? Task.FromResult(Offer);
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
    {
        Downloads++;
        if (FailDownload) throw new IOException("interrupted");
        progress(50);
        return DownloadGate?.Task ?? Task.CompletedTask;
    }
    public void PrepareRestart() { if (FailApply) throw new IOException("locked"); Restarts++; }
}
