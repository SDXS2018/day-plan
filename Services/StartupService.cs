using Microsoft.Win32;

namespace DailyPlan.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "DailyPlan";
    private static string ExePath => Path.Combine(AppContext.BaseDirectory, "DailyPlan.exe");

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var actual = key?.GetValue(EntryName) as string;
            return string.Equals(actual, $"\"{ExePath}\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled && !File.Exists(ExePath))
            throw new FileNotFoundException("请先发布或编译 DailyPlan.exe，再开启自启动。", ExePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey)
                        ?? throw new InvalidOperationException("无法访问 Windows 当前用户启动项。 ");
        if (enabled) key.SetValue(EntryName, $"\"{ExePath}\"", RegistryValueKind.String);
        else key.DeleteValue(EntryName, throwOnMissingValue: false);
    }
}
