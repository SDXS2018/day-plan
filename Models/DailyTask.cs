namespace DailyPlan.Models;

public sealed class DailyTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool IsCompleted { get; set; }
    public bool IsImportant { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TaskDocument
{
    public int Version { get; set; } = 1;
    public List<DailyTask> Tasks { get; set; } = new();
}

public sealed class TaskLoadResult
{
    public List<DailyTask> Tasks { get; init; } = new();
    public string? Warning { get; init; }
    public bool CanSave { get; init; } = true;
}
