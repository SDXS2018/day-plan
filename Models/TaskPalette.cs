namespace DailyPlan.Models;

public static class TaskPalette
{
    public static readonly string[] Colors =
    ["#EA3D70", "#3478ED", "#009C87", "#9754DF", "#DA6B18", "#D943A0", "#529731", "#1392BD"];
    private static readonly string[] LegacyColors =
    ["#D32F53", "#3264DC", "#008573", "#8550CD", "#BE5900", "#C1388A", "#527D16", "#087CA4"];

    public static string Next(string? previous = null)
    {
        var choices = Colors.Where(color => color != previous).ToArray();
        return choices[Random.Shared.Next(choices.Length)];
    }

    public static bool FillMissing(IEnumerable<DailyTask> tasks)
    {
        var changed = false;
        string? previous = null;
        foreach (var task in tasks.OrderBy(task => task.CreatedAt))
        {
            var legacy = Array.IndexOf(LegacyColors, task.Color);
            if (legacy >= 0) { task.Color = Colors[legacy]; changed = true; }
            if (!Colors.Contains(task.Color))
            {
                task.Color = Next(previous);
                changed = true;
            }
            previous = task.Color;
        }
        return changed;
    }
}
