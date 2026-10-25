using System.Windows.Input;
using DailyPlan.Infrastructure;
using DailyPlan.Models;

namespace DailyPlan.ViewModels;

public sealed class TaskItemViewModel : ObservableObject
{
    private readonly DailyTask _task;
    private readonly Action _onChange;
    private readonly Action<DailyTask> _onDelete;
    private readonly Action<DailyTask> _onMoveToToday;
    private bool _isEditing;
    private string _editText;
    private DateOnly _displayDate;
    private readonly Func<DateTimeOffset> _clock;

    public TaskItemViewModel(DailyTask task, Action onChange, Action<DailyTask> onDelete,
        Action<DailyTask> onMoveToToday, DateOnly? displayDate = null, Func<DateTimeOffset>? clock = null)
    {
        _task = task;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _displayDate = displayDate ?? DateOnly.FromDateTime(_clock().LocalDateTime);
        _onChange = onChange;
        _onDelete = onDelete;
        _onMoveToToday = onMoveToToday;
        _editText = task.Title;

        EditCommand = new RelayCommand(StartEdit);
        SaveEditCommand = new RelayCommand(SaveEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);
        DeleteCommand = new RelayCommand(() => _onDelete(_task));
        MoveToTodayCommand = new RelayCommand(() => _onMoveToToday(_task));
        ToggleImportanceCommand = new RelayCommand(() => IsImportant = !IsImportant);
    }

    public string Title => _task.Title;
    public Guid Id => _task.Id;
    public string Color => _task.Color;
    public bool IsImportant
    {
        get => _task.IsImportant;
        set
        {
            if (_task.IsImportant == value) return;
            _task.IsImportant = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EmphasisActive));
            OnPropertyChanged(nameof(ImportanceHint));
            _onChange();
        }
    }
    public bool EmphasisActive => IsImportant && !IsCompleted;
    public string ImportanceHint => IsImportant ? "取消重点突出" : "突出重要任务";
    public string Detail => _task.Date < _displayDate
        ? $"最初安排 {_task.Date:yyyy.MM.dd} · 未完成事项跨日延续" : $"安排于 {_task.Date:yyyy.MM.dd}";

    public void SetDisplayDate(DateOnly date)
    {
        _displayDate = date;
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(EmphasisActive));
        OnPropertyChanged(nameof(CanMoveToToday));
        OnPropertyChanged(nameof(Detail));
    }

    public bool IsCompleted
    {
        get => TaskDayRules.IsCompletedOn(_task, _displayDate);
        set
        {
            if (IsCompleted == value) return;
            _task.IsCompleted = value;
            var now = _clock();
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            _task.CompletedAt = value ? (_displayDate < today
                ? new DateTimeOffset(_displayDate.ToDateTime(TimeOnly.FromDateTime(now.LocalDateTime)), now.Offset)
                : now) : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EmphasisActive));
            _onChange();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value);
    }

    public bool CanMoveToToday => _displayDate != DateOnly.FromDateTime(_clock().LocalDateTime);

    public ICommand EditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveToTodayCommand { get; }
    public ICommand ToggleImportanceCommand { get; }

    public void StartEdit()
    {
        EditText = _task.Title;
        IsEditing = true;
    }

    public void SaveEdit()
    {
        var text = EditText.Trim();
        if (text.Length == 0) return; // Keep the editor open until valid text is supplied.
        if (text.Length > 160) text = text[..160];
        _task.Title = text;
        OnPropertyChanged(nameof(Title));
        IsEditing = false;
        _onChange();
    }

    public void CancelEdit()
    {
        EditText = _task.Title;
        IsEditing = false;
    }
}
