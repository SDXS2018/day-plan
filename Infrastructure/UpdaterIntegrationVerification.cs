using System.Security.Cryptography;
using System.Text.Json;
using DailyPlan.Models;
using DailyPlan.Services;
using Velopack;

namespace DailyPlan.Infrastructure;

/// <summary>Explicit local-feed update test, with its own tasks/settings and no normal app startup.</summary>
internal static class UpdaterIntegrationVerification
{
    private sealed class Configuration
    {
        public Configuration() { }
        public string FeedDirectory { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
        public string BaselineVersion { get; set; } = "";
        public string TargetVersion { get; set; } = "";
    }
    public static async Task RunAsync(string configurationPath, bool completed, Action exit)
    {
        Configuration? configuration = null;
        try
        {
            configuration = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(configurationPath)) ?? throw new InvalidOperationException("Missing configuration");
            Directory.CreateDirectory(configuration.OutputDirectory);
            var backend = new AppUpdateBackend(new UpdateManager(configuration.FeedDirectory, new UpdateOptions { ExplicitChannel = "win" }),
                new[] { "--verify-update-completed", Path.GetFullPath(configurationPath) });
            var store = new TaskStore(Path.Combine(configuration.OutputDirectory, "data"));
            var settingsPath = Path.Combine(Path.GetDirectoryName(store.FilePath)!, "settings.json");
            var beforePath = Path.Combine(configuration.OutputDirectory, "before.json");
            if (completed)
            {
                var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(beforePath))!;
                if (backend.CurrentVersion != configuration.TargetVersion || !backend.IsInstalled) throw new InvalidOperationException("Updated app version/locator mismatch");
                if (Hash(store.FilePath) != before["tasks"] || Hash(settingsPath) != before["settings"]) throw new InvalidOperationException("Fixture data changed during update");
                File.WriteAllText(Path.Combine(configuration.OutputDirectory, "complete.json"), JsonSerializer.Serialize(new
                {
                    success = true, from = configuration.BaselineVersion, to = backend.CurrentVersion,
                    automaticallyRestarted = true, tasksPreserved = true, settingsPreserved = true,
                    taskCount = store.Load().Tasks.Count, location = AppContext.BaseDirectory
                }, new JsonSerializerOptions { WriteIndented = true }));
                exit(); return;
            }
            if (!backend.IsInstalled || backend.CurrentVersion != configuration.BaselineVersion) throw new InvalidOperationException("Baseline version/locator mismatch");
            store.Save(new[] { new DailyTask { Title = "更新前的待办", IsImportant = true }, new DailyTask { Title = "更新前的完成任务", IsCompleted = true, CompletedAt = DateTimeOffset.Now } });
            File.WriteAllText(settingsPath, "{\"AutoCollapseOnDeactivated\":true,\"CapsuleLeft\":1700,\"CapsuleTop\":18}");
            File.WriteAllText(beforePath, JsonSerializer.Serialize(new { tasks = Hash(store.FilePath), settings = Hash(settingsPath) }));
            using var updates = new AppUpdateService(backend);
            await updates.CheckAsync();
            if (updates.Stage != UpdateStage.Available || updates.Offer?.Version != configuration.TargetVersion) throw new InvalidOperationException("Real local feed check failed: " + updates.Message);
            File.WriteAllText(Path.Combine(configuration.OutputDirectory, "progress.txt"), "New version detected");
            await updates.DownloadAsync();
            if (updates.Stage != UpdateStage.Ready) throw new InvalidOperationException("Real package download failed: " + updates.Message);
            File.AppendAllText(Path.Combine(configuration.OutputDirectory, "progress.txt"), "\nPackage downloaded and verified");
            if (!updates.PrepareRestart(() => true)) throw new InvalidOperationException("Could not schedule real update");
            File.AppendAllText(Path.Combine(configuration.OutputDirectory, "progress.txt"), "\nUpdate scheduled; exiting baseline app");
            exit();
        }
        catch (Exception error)
        {
            if (configuration != null)
            {
                Directory.CreateDirectory(configuration.OutputDirectory);
                File.WriteAllText(Path.Combine(configuration.OutputDirectory, "error.txt"), error.ToString());
            }
            exit();
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
