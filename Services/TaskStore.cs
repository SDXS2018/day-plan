using System.Text;
using System.Text.Json;
using DailyPlan.Models;

namespace DailyPlan.Services;

/// <summary>Writes a complete JSON snapshot, then atomically replaces the old file.</summary>
public sealed class TaskStore
{
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyPlan");

    private readonly string _dataFolder;

    public TaskStore(string? dataFolder = null) => _dataFolder = dataFolder ?? DataFolder;

    public string FilePath => Path.Combine(_dataFolder, "tasks.json");
    private string BackupPath => FilePath + ".bak";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TaskLoadResult Load()
    {
        if (!File.Exists(FilePath)) return new TaskLoadResult();

        try
        {
            return new TaskLoadResult { Tasks = Read(FilePath) };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Never quietly overwrite unreadable user data: archive it first.
            try
            {
                Directory.CreateDirectory(_dataFolder);
                var damagedCopy = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(FilePath, damagedCopy, overwrite: false);
            }
            catch (Exception backupError)
            {
                return new TaskLoadResult { CanSave = false, Warning = "任务文件读取失败，且无法保护原文件；本次已禁用保存：" + backupError.Message };
            }

            if (File.Exists(BackupPath))
            {
                try
                {
                    return new TaskLoadResult
                    {
                        Tasks = Read(BackupPath),
                        Warning = "已从备份恢复任务；损坏的原文件另外保存在数据目录。"
                    };
                }
                catch (Exception backupError) when (backupError is IOException or JsonException or UnauthorizedAccessException)
                {
                    // Preserve both corrupt files for manual recovery.
                }
            }

            return new TaskLoadResult
            {
                Warning = "任务文件无法读取，已保留损坏文件副本；目前清单为空。请检查数据目录。"
            };
        }
    }

    public void Save(IReadOnlyCollection<DailyTask> tasks)
    {
        Directory.CreateDirectory(_dataFolder);
        var document = new TaskDocument { Tasks = tasks.ToList() };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temporaryPath = FilePath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(FilePath))
            {
                // On NTFS, File.Replace keeps the previous version as tasks.json.bak.
                File.Replace(temporaryPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, FilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static List<DailyTask> Read(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var doc = JsonSerializer.Deserialize<TaskDocument>(json, JsonOptions)
                  ?? throw new JsonException("内容为空");
        if (doc.Tasks == null) throw new JsonException("tasks 字段缺失");

        return doc.Tasks
            .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => { t.Title = t.Title.Trim(); return t; })
            .ToList();
    }
}
