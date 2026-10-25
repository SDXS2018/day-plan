using System.ComponentModel;
using System.Windows.Input;
using DailyPlan.Infrastructure;
using DailyPlan.Models;

namespace DailyPlan.ViewModels;

public sealed class CalendarDay : ObservableObject
{
    private bool _isSelected;
    public DateOnly Date { get; }
    public string Number => Date.Day.ToString();
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }
    public bool IsFuture { get; }
    public bool IsInRange { get; }
    public IReadOnlyList<HistoryTask> Tasks { get; }
    public IEnumerable<HistoryTask> Preview => Tasks.Take(2);
    public int TotalCount => Tasks.Count;
    public bool HasTasks => TotalCount > 0;
    public string CountText => HasTasks ? $"{Tasks.Count(t => t.IsCompleted)}/{TotalCount}" : string.Empty;
    public string AccessibleName => $"{Date:yyyy年M月d日}，{Tasks.Count(t => t.IsCompleted)}项完成，共{TotalCount}项";
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public CalendarDay(DateOnly date, DateOnly month, DateOnly today, DateOnly start, DateOnly end, IReadOnlyList<HistoryTask> tasks, bool rangeMode)
    {
        Date = date;
        IsCurrentMonth = rangeMode ? date >= start && date <= end : date.Year == month.Year && date.Month == month.Month;
        IsToday = date == today; IsFuture = date > today;
        IsInRange = date >= start && date <= end;
        Tasks = tasks;
    }
}

public sealed class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _source;
    private int _periodDays;
    private DateOnly _displayMonth, _selectedDate;
    public HistoryReport Report { get; private set; } = null!;
    public IReadOnlyList<CalendarDay> CalendarDays { get; private set; } = Array.Empty<CalendarDay>();
    public IReadOnlyList<HistoryTask> SelectedTasks { get; private set; } = Array.Empty<HistoryTask>();
    public int PeriodDays => _periodDays;
    public bool IsCalendarMonth => _periodDays == 0;
    public bool IsWeek => _periodDays == 7;
    public bool IsMonth => _periodDays == 30;
    public bool IsSelectedEmpty => SelectedTasks.Count == 0;
    public DateOnly SelectedDate => _selectedDate;
    public string MonthTitle => IsCalendarMonth || Report.Start.Month == Report.End.Month
        ? $"{_displayMonth:yyyy} 年 {_displayMonth.Month} 月"
        : Report.Start.Year == Report.End.Year
            ? $"{Report.End:yyyy} 年 {Report.Start.Month}–{Report.End.Month} 月"
            : $"{Report.Start:yyyy.MM}–{Report.End:yyyy.MM}";
    public string SelectedDateText => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("M月d日 dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
    public string SelectedSummary => $"完成 {SelectedTasks.Count(t => t.IsCompleted)} · 待完成 {SelectedTasks.Count(t => !t.IsCompleted)}";
    public string PeriodLabel => IsCalendarMonth ? "所选月份" : $"近{_periodDays}天";
    public ICommand CalendarCommand { get; }
    public ICommand WeekCommand { get; }
    public ICommand MonthCommand { get; }
    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand TodayCommand { get; }

    public HistoryViewModel(MainViewModel source, int days = 0)
    {
        _source = source;
        _selectedDate = source.Today;
        _displayMonth = new DateOnly(source.Today.Year, source.Today.Month, 1);
        CalendarCommand = new RelayCommand(() => SelectPeriod(0));
        WeekCommand = new RelayCommand(() => SelectPeriod(7));
        MonthCommand = new RelayCommand(() => SelectPeriod(30));
        PreviousMonthCommand = new RelayCommand(() => ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(() => ChangeMonth(1));
        TodayCommand = new RelayCommand(() => SelectPeriod(0));
        SelectPeriod(days);
        _source.PropertyChanged += SourceChanged;
    }

    public void SelectPeriod(int days)
    {
        if (days is not (0 or 7 or 30)) throw new ArgumentOutOfRangeException(nameof(days));
        _periodDays = days;
        _displayMonth = new DateOnly(_source.Today.Year, _source.Today.Month, 1);
        _selectedDate = _source.Today;
        Refresh();
    }

    private void ChangeMonth(int delta)
    {
        _periodDays = 0;
        _displayMonth = _displayMonth.AddMonths(delta);
        _selectedDate = _source.Today.Year == _displayMonth.Year && _source.Today.Month == _displayMonth.Month
            ? _source.Today : _displayMonth;
        Refresh();
    }

    public void SelectDay(CalendarDay day)
    {
        var changedRange = !day.IsInRange && _periodDays != 0;
        if (changedRange) _periodDays = 0;
        _selectedDate = day.Date;
        if (!day.IsCurrentMonth)
        {
            _periodDays = 0;
            _displayMonth = new DateOnly(day.Date.Year, day.Date.Month, 1);
            Refresh();
            return;
        }
        if (changedRange) { Refresh(); return; }
        foreach (var cell in CalendarDays) cell.IsSelected = cell.Date == _selectedDate;
        SelectedTasks = day.Tasks;
        NotifySelection();
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TodaySummary)) Refresh();
    }

    private void Refresh()
    {
        var monthEnd = _displayMonth.AddMonths(1).AddDays(-1);
        var start = IsCalendarMonth ? _displayMonth : _source.Today.AddDays(1 - _periodDays);
        var end = IsCalendarMonth ? monthEnd : _source.Today;
        Report = _source.BuildHistory(start, end);
        // Monday-first six-week grid prevents month navigation from shifting the layout.
        var anchor = IsCalendarMonth ? _displayMonth : start;
        var first = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
        var daily = _source.BuildHistory(first, first.AddDays(41)).Days.ToDictionary(day => day.Date, day => day.Tasks);
        CalendarDays = Enumerable.Range(0, 42).Select(index =>
        {
            var date = first.AddDays(index);
            var tasks = daily.GetValueOrDefault(date) ?? Array.Empty<HistoryTask>();
            return new CalendarDay(date, _displayMonth, _source.Today, start, end, tasks, !IsCalendarMonth) { IsSelected = date == _selectedDate };
        }).ToArray();
        SelectedTasks = daily.GetValueOrDefault(_selectedDate) ?? Array.Empty<HistoryTask>();
        foreach (var name in new[] { nameof(Report), nameof(PeriodDays), nameof(IsCalendarMonth), nameof(IsWeek), nameof(IsMonth), nameof(PeriodLabel), nameof(MonthTitle), nameof(CalendarDays) }) OnPropertyChanged(name);
        NotifySelection();
    }

    private void NotifySelection()
    {
        foreach (var name in new[] { nameof(SelectedDate), nameof(SelectedDateText), nameof(SelectedTasks), nameof(SelectedSummary), nameof(IsSelectedEmpty) }) OnPropertyChanged(name);
    }

    public void Dispose() => _source.PropertyChanged -= SourceChanged;
}
