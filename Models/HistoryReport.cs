namespace DailyPlan.Models;

public sealed record HistoryTask(Guid Id, string Title, string Color, bool IsCompleted, bool IsCarried)
{
    public string StateText => IsCompleted ? "当日完成" : IsCarried ? "延续待办" : "待完成";
}

public sealed record HistoryDay(DateOnly Date, IReadOnlyList<HistoryTask> Tasks)
{
    public string DateText => Date.ToDateTime(TimeOnly.MinValue).ToString("yyyy.MM.dd dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
    public int CompletedCount => Tasks.Count(task => task.IsCompleted);
    public string Summary => $"完成 {CompletedCount} · 待完成 {Tasks.Count - CompletedCount}";
}

public sealed record HistoryReport(DateOnly Start, DateOnly End, int CompletedCount, int PendingCount,
    int CreatedCount, IReadOnlyList<HistoryDay> Days)
{
    public string RangeText => $"{Start:yyyy.MM.dd} — {End:yyyy.MM.dd}";

    public static HistoryReport Build(IEnumerable<DailyTask> source, DateOnly today, int days)
    {
        if (days is not (7 or 30)) throw new ArgumentOutOfRangeException(nameof(days));
        return Build(source, today, today.AddDays(1 - days), today);
    }

    public static HistoryReport Build(IEnumerable<DailyTask> source, DateOnly today, DateOnly start, DateOnly end)
    {
        if (end < start || end.DayNumber - start.DayNumber > 62) throw new ArgumentOutOfRangeException(nameof(end));
        var tasks = source.ToList();
        var result = new List<HistoryDay>();
        for (var date = end; date >= start; date = date.AddDays(-1))
        {
            var items = tasks.Where(task => TaskDayRules.AppearsOn(task, date, today))
                .OrderBy(task => TaskDayRules.IsCompletedOn(task, date)).ThenBy(task => task.CreatedAt)
                .Select(task => new HistoryTask(task.Id, task.Title, task.Color,
                    TaskDayRules.IsCompletedOn(task, date), task.Date < date)).ToList();
            if (items.Count > 0) result.Add(new HistoryDay(date, items));
        }
        return new HistoryReport(start, end,
            tasks.Count(task => task.IsCompleted && TaskDayRules.CompletionDay(task) >= start && TaskDayRules.CompletionDay(task) <= end && TaskDayRules.CompletionDay(task) <= today),
            tasks.Count(task => !task.IsCompleted && task.Date <= today),
            tasks.Count(task => DateOnly.FromDateTime(task.CreatedAt.LocalDateTime) >= start && DateOnly.FromDateTime(task.CreatedAt.LocalDateTime) <= end && DateOnly.FromDateTime(task.CreatedAt.LocalDateTime) <= today),
            result);
    }
}
