namespace DailyPlan.Models;

/// <summary>A task keeps its original date and identity; unfinished tasks remain visible on later days.</summary>
public static class TaskDayRules
{
    public static DateOnly CompletionDay(DailyTask task)
    {
        var day = task.CompletedAt.HasValue ? DateOnly.FromDateTime(task.CompletedAt.Value.LocalDateTime) : task.Date;
        return day < task.Date ? task.Date : day;
    }

    public static bool AppearsOn(DailyTask task, DateOnly day, DateOnly today)
    {
        if (day < task.Date) return false;
        if (day > today) return day == task.Date;
        return !task.IsCompleted || day <= CompletionDay(task);
    }

    public static bool IsCompletedOn(DailyTask task, DateOnly day) => task.IsCompleted && CompletionDay(task) <= day;
}
