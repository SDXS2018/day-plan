using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using DailyPlan.Infrastructure;
using DailyPlan.Models;
using DailyPlan.Services;

namespace DailyPlan.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly TaskStore _store;
    private readonly List<DailyTask> _allTasks;
    private readonly Func<DateTimeOffset> _clock;
    private DateOnly _selectedDate;
    private DateOnly _knownToday;
    private string _newTaskText = string.Empty;
    private string _statusMessage = string.Empty;
    private readonly bool _canSave;
    private readonly RelayCommand _addTaskCommand;
    private static readonly CultureInfo ZhCn = CultureInfo.GetCultureInfo("zh-CN");

    public MainViewModel(TaskStore store, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.Now);
        var loaded = store.Load();
        _allTasks = loaded.Tasks;
        _canSave = loaded.CanSave;
        _statusMessage = loaded.Warning ?? string.Empty;
        var colorsChanged = TaskPalette.FillMissing(_allTasks);
        _knownToday = DateOnly.FromDateTime(_clock().LocalDateTime);
        _selectedDate = _knownToday;

        _addTaskCommand = new RelayCommand(AddTask, () => _newTaskText.Trim().Length > 0 && _canSave);
        AddTaskCommand = _addTaskCommand;
        PreviousDayCommand = new RelayCommand(() => ChangeDate(-1));
        NextDayCommand = new RelayCommand(() => ChangeDate(1));
        GoToTodayCommand = new RelayCommand(() =>
        {
            if (_selectedDate == _knownToday) return;
            _selectedDate = _knownToday;
            Refresh();
        });
        Refresh();
        if (colorsChanged && _canSave) Persist();
    }

    public ObservableCollection<TaskItemViewModel> Items { get; } = new();

    public ICommand AddTaskCommand { get; }
    public ICommand PreviousDayCommand { get; }
    public ICommand NextDayCommand { get; }
    public ICommand GoToTodayCommand { get; }
    public DateOnly Today => _knownToday;
    public HistoryReport BuildHistory(int days) => HistoryReport.Build(_allTasks, _knownToday, days);
    public HistoryReport BuildHistory(DateOnly start, DateOnly end) => HistoryReport.Build(_allTasks, _knownToday, start, end);
    public bool IsToday => _selectedDate == _knownToday;
    public bool IsPast => _selectedDate < _knownToday;
    public bool IsFuture => _selectedDate > _knownToday;
    public string DateHint => IsToday ? "今天的清单" : IsPast ? "过去的清单" : "未来安排";

    public string NewTaskText
    {
        get => _newTaskText;
        set
        {
            if (SetProperty(ref _newTaskText, value)) _addTaskCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string DateText
    {
        get
        {
            var dateTime = _selectedDate.ToDateTime(TimeOnly.MinValue);
            var prefix = _selectedDate == _knownToday ? "今天" :
                         _selectedDate == _knownToday.AddDays(-1) ? "昨天" :
                         _selectedDate == _knownToday.AddDays(1) ? "明天" : "计划";
            return $"{prefix} · {dateTime.ToString("M月d日 dddd", ZhCn)}";
        }
    }

    public string CurrentSummary => $"已完成 {CompletedCount} / {TotalCount}";
    public string DayLabel => _selectedDate == _knownToday ? "今天" :
        _selectedDate == _knownToday.AddDays(-1) ? "昨天" :
        _selectedDate == _knownToday.AddDays(1) ? "明天" : "每日计划";
    public string DateDetail => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("M月d日 · dddd", ZhCn);
    public string DateTitle => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy.MM.dd dddd", ZhCn);
    public string CompactProgress => $"{CompletedCount} / {TotalCount} 已完成";
    public string TodaySummary
    {
        get
        {
            var today = _allTasks.Where(t => TaskDayRules.AppearsOn(t, _knownToday, _knownToday)).ToList();
            return $"{today.Count(t => TaskDayRules.IsCompletedOn(t, _knownToday))}/{today.Count}";
        }
    }

    public int TotalCount => Items.Count;
    public int CompletedCount => Items.Count(t => t.IsCompleted);
    public double ProgressPercent => TotalCount == 0 ? 0d : 100d * CompletedCount / TotalCount;
    public bool IsEmpty => TotalCount == 0;
    public bool CanSave => _canSave;
    public bool IsNotToday => _selectedDate != _knownToday;

    private void AddTask()
    {
        var text = NewTaskText.Trim();
        if (text.Length == 0 || !_canSave) return;
        if (text.Length > 160) text = text[..160];

        _allTasks.Add(new DailyTask
        {
            Title = text,
            Color = TaskPalette.Next(_allTasks.LastOrDefault(task => task.Date == _selectedDate)?.Color),
            Date = _selectedDate,
            CreatedAt = _clock()
        });
        NewTaskText = string.Empty;
        Refresh();
        Persist();
    }

    private void ChangeDate(int delta)
    {
        _selectedDate = _selectedDate.AddDays(delta);
        Refresh();
    }

    private void DeleteTask(DailyTask task)
    {
        _allTasks.Remove(task);
        Refresh();
        Persist();
    }

    private void MoveToToday(DailyTask task)
    {
        if (task.Date > _knownToday) task.Date = _knownToday;
        _selectedDate = _knownToday;
        Refresh();
        Persist();
    }

    private void Refresh()
    {
        var selected = _allTasks.Where(task => TaskDayRules.AppearsOn(task, _selectedDate, _knownToday))
            .OrderBy(task => TaskDayRules.IsCompletedOn(task, _selectedDate)).ThenBy(task => task.CreatedAt).ToList();
        var ids = selected.Select(task => task.Id).ToHashSet();
        for (var index = Items.Count - 1; index >= 0; index--)
            if (!ids.Contains(Items[index].Id)) Items.RemoveAt(index);
        for (var index = 0; index < selected.Count; index++)
        {
            var task = selected[index];
            if (index < Items.Count && Items[index].Id == task.Id)
            {
                Items[index].SetDisplayDate(_selectedDate);
                continue;
            }
            var existing = Items.FirstOrDefault(item => item.Id == task.Id);
            if (existing != null) { existing.SetDisplayDate(_selectedDate); Items.Move(Items.IndexOf(existing), index); }
            else Items.Insert(index, new TaskItemViewModel(task, OnTaskChanged, DeleteTask, MoveToToday, _selectedDate, _clock));
        }
        RefreshSummary();
    }

    private void OnTaskChanged()
    {
        Refresh();
        Persist();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(DateTitle));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsPast));
        OnPropertyChanged(nameof(IsFuture));
        OnPropertyChanged(nameof(DateHint));
        OnPropertyChanged(nameof(DayLabel));
        OnPropertyChanged(nameof(DateDetail));
        OnPropertyChanged(nameof(CompactProgress));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(CurrentSummary));
        OnPropertyChanged(nameof(TodaySummary));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNotToday));
    }

    private void Persist()
    {
        if (!_canSave)
        {
            StatusMessage = "保存已锁定：请先备份并修复数据文件。";
            return;
        }
        try
        {
            _store.Save(_allTasks);
            StatusMessage = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "保存失败：" + ex.Message;
        }
    }

    public void CheckDayRollover()
    {
        var today = DateOnly.FromDateTime(_clock().LocalDateTime);
        if (today == _knownToday) return;
        var wasViewingToday = _selectedDate == _knownToday;
        _knownToday = today;
        if (wasViewingToday) _selectedDate = today;
        Refresh();
    }
}
