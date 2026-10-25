using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using DailyPlan.ViewModels;

namespace DailyPlan.Infrastructure;

/// <summary>Opt-in component verification. Never runs during ordinary use.</summary>
internal static class MotionVerification
{
    public static async Task PreviewAsync(MainWindow window, string output, Action exit)
    {
        Directory.CreateDirectory(output);
        window.IgnoreDeactivationForVerification = true;
        try
        {
            await Task.Delay(300);
            window.SetExpanded(true); await Wait(window);
            var header = JsonSerializer.SerializeToElement(window.InspectHeaderLayout());
            File.WriteAllText(Path.Combine(output, "header-fit.json"), JsonSerializer.Serialize(header, new JsonSerializerOptions { WriteIndented = true }));
            if (!header.GetProperty("fits").GetBoolean() || !header.GetProperty("centered").GetBoolean())
                throw new InvalidOperationException("Date heading does not fit or is not centered.");
            await VerifyInput(window, output);
            window.SetExpanded(false); await Wait(window);
            await Capture(window, output);
            File.WriteAllText(Path.Combine(output, "complete.txt"), "PASS");
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); }
        finally { window.IgnoreDeactivationForVerification = false; exit(); }
    }

    private static async Task VerifyInput(MainWindow window, string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        window.Activate();
        Check(window.InputBackdrop.Visibility == Visibility.Hidden && window.AddToggleButton.IsEnabled,
            "Input is hidden by default and plus remains available");
        var ids = window.TaskList.Items.Cast<TaskItemViewModel>().Select(item => item.Id).ToHashSet();
        window.AddToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(200);
        Check(window.InputBackdrop.Visibility == Visibility.Visible &&
            (window.NewTaskBox.IsKeyboardFocused || FocusManager.GetFocusedElement(window) == window.NewTaskBox),
            "Plus reveals and focuses the input");
        window.NewTaskBox.Text = "输入框验收 Enter";
        window.NewTaskBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
        await Task.Delay(200);
        var entered = window.TaskList.Items.Cast<TaskItemViewModel>().Single(item => !ids.Contains(item.Id));
        Check(window.InputBackdrop.Visibility == Visibility.Hidden && entered.Title == "输入框验收 Enter",
            "Enter adds the task and hides the input");
        entered.DeleteCommand.Execute(null);
        await Task.Delay(100);
        window.AddToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(200);
        window.NewTaskBox.Text = "输入框验收 Plus";
        window.AddToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(200);
        var clicked = window.TaskList.Items.Cast<TaskItemViewModel>().Single(item => !ids.Contains(item.Id));
        Check(window.InputBackdrop.Visibility == Visibility.Hidden && clicked.Title == "输入框验收 Plus",
            "Plus submits entered text and hides the input");
        clicked.DeleteCommand.Execute(null);
        await Task.Delay(100);
        window.AddToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Task.Delay(200);
        window.NewTaskBox.Text = "保留草稿";
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Task.Delay(200);
        Check(window.InputBackdrop.Visibility == Visibility.Hidden && window.NewTaskBox.Text == "保留草稿" && window.MotionProgress > 0.99,
            "Escape folds the editor, retains the draft, and keeps the panel open");
        window.NewTaskBox.Text = string.Empty;
        File.WriteAllLines(Path.Combine(output, "input-checks.txt"), checks);
    }

    public static async Task RunAsync(MainWindow window, string output, Action exit)
    {
        Directory.CreateDirectory(output);
        var samples = new List<object>();
        var openTimes = new List<double>();
        var closeTimes = new List<double>();
        var layoutDuringMotion = 0;
        EventHandler layout = (_, _) =>
        {
            if (window.MotionProgress > 0.002 && window.MotionProgress < 0.998) layoutDuringMotion++;
        };
        window.IgnoreDeactivationForVerification = true;
        try
        {
            await Task.Delay(400);
            for (var index = 0; index < 10; index++) await Cycle(window, openTimes, closeTimes);
            window.FrameIntervals = new List<double>(10000);
            window.LayoutUpdated += layout;
            samples.Add(Sample("warm-start"));
            var initialMeasures = window.ContentMeasurePasses;
            var performanceClock = Stopwatch.StartNew();
            var initialCpu = Process.GetCurrentProcess().TotalProcessorTime;
            for (var index = 1; index <= 100; index++)
            {
                await Cycle(window, openTimes, closeTimes);
                if (index % 10 == 0)
                {
                    samples.Add(Sample("cycle-" + index));
                    File.WriteAllText(Path.Combine(output, "progress.txt"), $"{index}/100 cycles complete");
                }
            }
            window.SetExpanded(true);
            await Task.Delay(50);
            var before = window.MotionProgress;
            window.SetExpanded(false);
            var continuousAtReverse = before == window.MotionProgress;
            await Task.Delay(35);
            before = window.MotionProgress;
            window.SetExpanded(true);
            continuousAtReverse &= before == window.MotionProgress;
            await Wait(window);
            window.SetExpanded(false);
            await Wait(window);
            await Task.Delay(5000);
            samples.Add(Sample("idle-5s"));
            File.WriteAllText(Path.Combine(output, "progress.txt"), "100/100 cycles complete; observing idle memory");
            await Task.Delay(25000);
            samples.Add(Sample("idle-30s"));
            var intervals = window.FrameIntervals.Order().ToArray();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).Order().ToArray();
            var result = new
            {
                cycles = 100, continuousAtReverse, idleRenderingStopped = !window.IsAnimating,
                medianOpenMs = Median(openTimes), medianCloseMs = Median(closeTimes),
                medianFrameMs = Median(intervals), p95FrameMs = intervals[(int)(intervals.Length * 0.95)],
                maxFrameMs = intervals.Max(), layoutEventsDuringMotion = layoutDuringMotion,
                contentMeasurePasses = window.ContentMeasurePasses - initialMeasures,
                renderPreference = RenderOptions.ProcessRenderMode.ToString(), hardwareTier = RenderCapability.Tier >> 16,
                cpuMsPerCycle = (Process.GetCurrentProcess().TotalProcessorTime - initialCpu).TotalMilliseconds / 100,
                elapsedSeconds = performanceClock.Elapsed.TotalSeconds,
                transparentSpacePassesThrough = PassesThrough(window),
                windowsFormsLoaded = assemblies.Any(name => name == "System.Windows.Forms"),
                drawingCommonLoaded = assemblies.Any(name => name == "System.Drawing.Common"),
                samples, assemblies
            };
            File.WriteAllText(Path.Combine(output, "motion-memory.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            window.FrameIntervals = null;
            window.LayoutUpdated -= layout;
            await Capture(window, output);
            File.WriteAllText(Path.Combine(output, "complete.txt"), "PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString());
        }
        finally
        {
            window.FrameIntervals = null;
            window.LayoutUpdated -= layout;
            window.IgnoreDeactivationForVerification = false;
            exit();
        }
    }

    private static async Task Cycle(MainWindow window, List<double> open, List<double> close)
    {
        var clock = Stopwatch.StartNew();
        window.SetExpanded(true); await WaitMotion(window); open.Add(clock.Elapsed.TotalMilliseconds); await Wait(window);
        clock.Restart();
        window.SetExpanded(false); await WaitMotion(window); close.Add(clock.Elapsed.TotalMilliseconds); await Wait(window);
    }

    private static async Task WaitMotion(MainWindow window)
    {
        var clock = Stopwatch.StartNew();
        while (!window.IsMotionSettled)
        {
            if (clock.Elapsed.TotalSeconds > 3) throw new TimeoutException("Shape did not settle.");
            await Task.Delay(8);
        }
    }

    private static async Task Wait(MainWindow window)
    {
        var clock = Stopwatch.StartNew();
        while (window.IsAnimating)
        {
            if (clock.Elapsed.TotalSeconds > 3) throw new TimeoutException("Motion did not settle.");
            await Task.Delay(8);
        }
    }

    private static object Sample(string phase)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new
        {
            phase, privateMB = Math.Round(process.PrivateMemorySize64 / 1048576d, 2),
            workingSetMB = Math.Round(process.WorkingSet64 / 1048576d, 2),
            managedMB = Math.Round(GC.GetTotalMemory(false) / 1048576d, 2), handles = process.HandleCount
        };
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    private static async Task Capture(MainWindow window, string output)
    {
        // Capture the component's own visuals at 25 fps, separately from the measured stress run.
        for (var frame = 0; frame < 32; frame++)
        {
            if (frame == 0) window.SetExpanded(true);
            if (frame == 18) window.SetExpanded(false);
            await Task.Delay(40);
            var previewHeight = (int)Math.Ceiling(window.ActualHeight);
            var bitmap = new RenderTargetBitmap(332, previewHeight, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 332, previewHeight));
                var brush = new VisualBrush((Visual)window.Content)
                {
                    Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, window.ActualWidth, window.ActualHeight)
                };
                context.DrawRectangle(brush, null, new Rect(332 - window.ActualWidth, 0, window.ActualWidth, window.ActualHeight));
            }
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"frame-{frame:D2}.png"));
            encoder.Save(file);
        }
        await Wait(window);
    }

    private static bool PassesThrough(MainWindow window)
    {
        var screen = window.PointToScreen(new Point(20, window.ActualHeight - 20));
        return WindowFromPoint(new NativePoint((int)screen.X, (int)screen.Y)) != new WindowInteropHelper(window).Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x, Y = y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
}
