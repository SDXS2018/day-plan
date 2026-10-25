using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyPlan.ViewModels;
using DailyPlan.Services;
using System.Windows.Controls;

namespace DailyPlan.Infrastructure;

/// <summary>Opt-in component checks with an isolated store, never ordinary user data.</summary>
internal static class HistoryVerification
{
    public static async Task RunAsync(MainWindow mini, MainViewModel source, TrayService tray, Func<int, HistoryWindow> show,
        string output, Action exit)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        mini.IgnoreDeactivationForVerification = true;
        try
        {
            await Task.Delay(400);
            mini.ShowAndExpand(); await Task.Delay(450);
            var header = JsonSerializer.SerializeToElement(mini.InspectHeaderLayout());
            Check(header.GetProperty("fits").GetBoolean() && header.GetProperty("centered").GetBoolean(),
                "Full Chinese date fits and is centered inside the highlighted heading");
            File.WriteAllText(Path.Combine(output, "header-fit.json"), header.ToString());
            Capture(mini, Path.Combine(output, "today.png"));
            source.PreviousDayCommand.Execute(null); await Task.Delay(200);
            Capture(mini, Path.Combine(output, "past.png"));
            source.NextDayCommand.Execute(null); source.NextDayCommand.Execute(null); await Task.Delay(200);
            Capture(mini, Path.Combine(output, "future.png"));
            mini.SetExpanded(false); await Task.Delay(300);
            mini.ShowAndExpand(); await Task.Delay(400);
            Check(source.IsToday && mini.IsExpanded, "Opening the capsule always returns to today");
            mini.SettingsMenuClosed(true, true); await Task.Delay(100);
            Check(mini.IsExpanded, "Returning from settings inside the panel keeps it open");
            mini.SettingsMenuClosed(false, true); await Task.Delay(300);
            Check(!mini.IsExpanded, "Closing settings outside the panel collapses normally");
            mini.ShowAndExpand(); await Task.Delay(350);
            tray.ShowMenu(); await Task.Delay(200);
            Check(tray.MenuForVerification.Items.OfType<MenuItem>().Count() == 4,
                "Settings has four focused actions without redundant date or expand entries");
            Capture(tray.MenuForVerification, Path.Combine(output, "settings.png"));
            tray.MenuForVerification.IsOpen = false;
            var history = show(0); await Task.Delay(550);
            Check(history.WindowState == WindowState.Maximized && history.IsVisible && !mini.Topmost,
                "Work review opens full screen and floating capsule stops overlapping it");
            Check(history.CalendarGrid.Items.Count == 42 && history.Model.IsCalendarMonth, "Calendar opens at the current month with six complete weeks");
            Capture(history, Path.Combine(output, "review-calendar.png"));
            var day = history.Model.CalendarDays.Single(day => day.Date == source.Today.AddDays(-1));
            history.Model.SelectDay(day); await Task.Delay(150);
            Check(history.Model.SelectedDate == day.Date && history.DayTaskList.Items.Count == day.Tasks.Count,
                "Selecting a calendar date updates the task detail panel");
            Capture(history, Path.Combine(output, "review-selected-day.png"));
            history.Model.WeekCommand.Execute(null); await Task.Delay(150);
            Check(history.Model.IsWeek && history.Model.CalendarDays.Count(day => day.IsInRange) == 7,
                "Seven-day range highlights exactly seven days");
            Capture(history, Path.Combine(output, "review-week.png"));
            var reused = show(30); await Task.Delay(250);
            Check(ReferenceEquals(history, reused) && history.Model.IsMonth && history.Model.Report.CompletedCount > 0,
                "Thirty-day selection reuses one review window with valid totals");
            Capture(history, Path.Combine(output, "review-month.png"));
            history.WindowState = WindowState.Normal; history.Width = 1000; history.Height = 720;
            await Task.Delay(350);
            Check(history.CalendarGrid.ActualHeight >= 348 && history.DayTaskList.ActualWidth > 150,
                "Windowed calendar retains room for the grid and day details");
            Capture(history, Path.Combine(output, "review-windowed.png"));
            history.Close(); await Task.Delay(250);
            Check(mini.Topmost && !mini.IsExpanded && history.DataContext == null,
                "Closing review restores the capsule and releases its view data");
            await VerifyAdaptiveEmphasis(mini, source, output, Check);
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            File.WriteAllText(Path.Combine(output, "complete.txt"), "PASS");
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); }
        finally { mini.IgnoreDeactivationForVerification = false; exit(); }
    }

    private static async Task VerifyAdaptiveEmphasis(MainWindow mini, MainViewModel source, string output, Action<bool, string> check)
    {
        source.NewTaskText = "复核临床试验数据并完成药剂学文献阅读记录";
        source.AddTaskCommand.Execute(null);
        mini.ShowAndExpand(); await Task.Delay(450);
        var task = source.Items.Single(item => item.Title == "复核临床试验数据并完成药剂学文献阅读记录");
        var label = Descendants<TextBlock>(mini.TaskList).Single(label => label.Text == task.Title);
        var row = Descendants<Grid>(mini.TaskList).Single(element => element.Name == "TaskRow" && ReferenceEquals(element.DataContext, task));
        var tools = Descendants<Border>(row).Single(element => element.Name == "RowActions");
        check(row.IsMouseOver || tools.Visibility == Visibility.Hidden, "All task tools stay hidden while the row is not hovered");
        var text = new FormattedText(label.Text, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
            label.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(mini).PixelsPerDip);
        check(mini.Width > 332 && label.ActualWidth >= text.WidthIncludingTrailingWhitespace && label.ActualHeight < 30,
            "Panel grows to fit a long task on one line within the screen");
        task.ToggleImportanceCommand.Execute(null); await Task.Delay(180);
        var surface = Descendants<TaskEmphasisSurface>(mini.TaskList).Single(element => ReferenceEquals(element.DataContext, task));
        var pixels = SurfacePixels(surface);
        await Task.Delay(400);
        check(surface.IsEmphasized && pixels.SequenceEqual(SurfacePixels(surface)),
            "Important task has a stable static background with no breathing changes");
        Capture(mini, Path.Combine(output, "static-emphasis.png"));
        mini.SetExpanded(false); await Task.Delay(400);
        check(!surface.IsVisible, "Collapsed panel hides the static emphasis surface");
        mini.ShowAndExpand(); await Task.Delay(450);
        task.IsCompleted = true; await Task.Delay(100);
        check(!surface.IsEmphasized && !task.EmphasisActive && source.Items.Last().Id == task.Id,
            "Completed important task loses emphasis and moves below unfinished work");
        task.IsCompleted = false; task.ToggleImportanceCommand.Execute(null); await Task.Delay(100);
        check(!surface.IsEmphasized, "Turning importance off clears the static background");
        task.StartEdit(); task.EditText = new string('长', 160); task.SaveEdit(); await Task.Delay(300);
        check(mini.Width <= 900 && mini.Width <= SystemParameters.WorkArea.Width && label.ActualHeight > 30,
            "Extreme titles stay within the width limit and wrap without truncation");
        var center = tools.TranslatePoint(new Point(0, tools.ActualHeight / 2), row).Y;
        check(Math.Abs(center - row.ActualHeight / 2) < 1 && tools.ActualHeight <= row.ActualHeight,
            "Tool group stays vertically centered and contained in a multiline task row");
        var wide = mini.Width;
        task.DeleteCommand.Execute(null);
        await Task.Delay(300);
        check(mini.Width < wide, "Panel narrows again after the long task is removed");
        mini.SetExpanded(false);
    }

    private static byte[] SurfacePixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
    }
}
