using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using DailyPlan.Infrastructure;
using DailyPlan.Models;
using DailyPlan.ViewModels;

namespace DailyPlan;

public partial class MainWindow : Window
{
    private const double CompactWidth = 120, CompactHeight = 52, MinimumOpenWidth = 332;
    private double _openWidth = MinimumOpenWidth;
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _dayTimer;
    private readonly DampedSpring _motion = new(), _hover = new(), _press = new();
    private readonly RectangleGeometry _contentClip = new();
    private readonly TranslateTransform _contentTranslation = new();
    private readonly ScaleTransform _capsuleScale = new();
    private readonly TranslateTransform _inputTranslation = new();
    private bool _adding;
    private readonly Stopwatch _clock = new();
    private double _lastFrame, _compactLeft, _compactTop, _openHeight;
    private bool _rendering, _hostExpanded, _allowExit, _pointerDown, _dragging, _contextMenuOpen;
    private bool _layoutDirty = true;
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;

    public event EventHandler? SettingsRequested;
    public event EventHandler? SettingsChanged;
    public Func<bool>? HasOpenSettingsMenu { get; set; }
    internal bool IsAnimating => _rendering;
    internal bool IsExpanded => _motion.Target == 1;
    internal bool IsPointerInsidePanel => Surface.ContainsPoint(Mouse.GetPosition(this));
    internal double MotionProgress => _motion.Position;
    internal bool IsMotionSettled => _motion.IsSettled;
    internal int ContentMeasurePasses => ContentRoot.MeasurePasses;
    internal bool IgnoreDeactivationForVerification { get; set; }
    internal List<double>? FrameIntervals { get; set; }

    internal object InspectHeaderLayout()
    {
        var text = new FormattedText(DateHeading.Text, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight, new Typeface(DateHeading.FontFamily, DateHeading.FontStyle,
                DateHeading.FontWeight, DateHeading.FontStretch), DateHeading.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var center = DateHeading.TranslatePoint(new Point(DateHeading.ActualWidth / 2, 0), this).X;
        return new
        {
            text = DateHeading.Text, fontSize = DateHeading.FontSize, weight = DateHeading.FontWeight.ToString(),
            textWidth = text.WidthIncludingTrailingWhitespace, availableWidth = DateHeading.ActualWidth,
            fits = text.WidthIncludingTrailingWhitespace <= DateHeading.ActualWidth + 0.5,
            centerX = center, windowCenterX = ActualWidth / 2, centered = Math.Abs(center - ActualWidth / 2) < 1
        };
    }

    public MainWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        ContentRoot.Clip = _contentClip;
        ContentRoot.RenderTransform = _contentTranslation;
        CapsuleHitTarget.RenderTransform = _capsuleScale;
        InputBackdrop.RenderTransform = _inputTranslation;
        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dayTimer.Tick += (_, _) => _viewModel.CheckDayRollover();
        _viewModel.PropertyChanged += ViewModelChanged;
        SystemParameters.StaticPropertyChanged += SystemParametersChanged;
    }

    public bool AutoCollapseOnDeactivated
    {
        get => _settings.AutoCollapseOnDeactivated;
        set => _settings.AutoCollapseOnDeactivated = value;
    }

    internal void SetUpdateAvailable(bool available)
    {
        UpdateDot.Visibility = CapsuleUpdateDot.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowAndExpand()
    {
        _viewModel.CheckDayRollover();
        _viewModel.GoToTodayCommand.Execute(null);
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        SetExpanded(true);
    }

    public void AllowExit()
    {
        _allowExit = true;
        StopRendering();
        _dayTimer.Stop();
        _viewModel.PropertyChanged -= ViewModelChanged;
        SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        _compactLeft = _settings.CapsuleLeft ?? area.Right - CompactWidth - _settings.WindowMargin;
        _compactTop = _settings.CapsuleTop ?? area.Top + _settings.WindowMargin;
        ClampCompactPosition();
        RestoreCompactHost();
        _dayTimer.Start();
    }

    internal void SetExpanded(bool expanded)
    {
        var target = expanded ? 1d : 0d;
        if (!expanded && _adding) SetAdding(false, false);
        if (_motion.Target == target && _motion.IsSettled) return;
        if (expanded)
        {
            if (!_hostExpanded || _layoutDirty) PrepareExpandedHost();
            ContentRoot.Visibility = Visibility.Visible;
        }
        _motion.Target = target;
        _hover.Target = 0;
        _press.Target = 0;
        if (!SystemParameters.ClientAreaAnimation)
        {
            _motion.Reset(target);
            ApplyVisualState();
            if (!expanded) RestoreCompactHost();
            return;
        }
        StartRendering();
    }

    private void PrepareExpandedHost()
    {
        ContentRoot.Visibility = Visibility.Visible;
        if (_layoutDirty || _openHeight == 0)
        {
            var typeface = new Typeface(FontFamily, FontStyle, FontWeights.Bold, FontStretch);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var longest = _viewModel.Items.Select(item => new FormattedText(item.Title,
                System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                typeface, 17, Brushes.Black, dpi).WidthIncludingTrailingWhitespace).DefaultIfEmpty(0).Max();
            var maximumWidth = Math.Min(900, SystemParameters.WorkArea.Width - 24);
            _openWidth = Math.Clamp(Math.Ceiling(longest + 194), Math.Min(MinimumOpenWidth, maximumWidth), maximumWidth);
            ContentRoot.Width = _openWidth - 20;
            TaskList.Measure(new Size(_openWidth - 50, double.PositiveInfinity));
            var maximum = Math.Min(500, SystemParameters.WorkArea.Height - 24);
            _openHeight = Math.Clamp(162 + Math.Max(64, TaskList.DesiredSize.Height), Math.Min(228, maximum), maximum);
            _layoutDirty = false;
        }
        ContentRoot.Height = _openHeight - 20;
        var area = SystemParameters.WorkArea;
        ContentRoot.Width = _openWidth - 20;
        var left = Math.Clamp(_compactLeft + CompactWidth - _openWidth, area.Left, Math.Max(area.Left, area.Right - _openWidth));
        var top = Math.Clamp(_compactTop, area.Top, Math.Max(area.Top, area.Bottom - _openHeight));
        _hostExpanded = true;
        using (Dispatcher.DisableProcessing())
        {
            Width = _openWidth; Height = _openHeight; Left = left; Top = top;
            Surface.Width = _openWidth; Surface.Height = _openHeight;
            Surface.CompactBounds = new Rect(_compactLeft - left + 10, _compactTop - top + 10, 100, 32);
            Surface.ExpandedBounds = new Rect(10, 10, _openWidth - 20, _openHeight - 20);
            System.Windows.Controls.Canvas.SetLeft(CapsuleHitTarget, Surface.CompactBounds.X);
            System.Windows.Controls.Canvas.SetTop(CapsuleHitTarget, Surface.CompactBounds.Y);
            ApplyVisualState();
        }
    }

    private void RestoreCompactHost()
    {
        ContentRoot.Visibility = Visibility.Hidden;
        ContentRoot.IsHitTestVisible = false;
        // Keep the small pre-laid-out surface after first use. Alpha-zero space passes mouse input through.
        // Reusing it avoids reallocating a layered-window backing buffer on every open/close.
        if (_hostExpanded && !_dragging)
        {
            ApplyVisualState();
            return;
        }
        _hostExpanded = false;
        using (Dispatcher.DisableProcessing())
        {
            Width = CompactWidth; Height = CompactHeight; Left = _compactLeft; Top = _compactTop;
            Surface.Width = CompactWidth; Surface.Height = CompactHeight;
            Surface.CompactBounds = new Rect(10, 10, 100, 32);
            Surface.ExpandedBounds = Surface.CompactBounds;
            System.Windows.Controls.Canvas.SetLeft(CapsuleHitTarget, 10);
            System.Windows.Controls.Canvas.SetTop(CapsuleHitTarget, 10);
            ApplyVisualState();
        }
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _clock.Restart();
        _lastFrame = 0;
        CompositionTarget.Rendering += RenderFrame;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= RenderFrame;
        _rendering = false;
        _clock.Stop();
    }

    private void RenderFrame(object? sender, EventArgs e)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        var dt = elapsed - _lastFrame;
        if (dt < 0.001) return;
        _lastFrame = elapsed;
        FrameIntervals?.Add(dt * 1000);
        _motion.Advance(dt, _motion.Target > _motion.Position ? 36 : 46);
        _hover.Advance(dt, 70);
        _press.Advance(dt, 80);
        ApplyVisualState();
        if (!_motion.IsSettled || !_hover.IsSettled || !_press.IsSettled) return;
        StopRendering();
        if (_motion.Target == 0 && _hostExpanded) RestoreCompactHost();
        else if (_motion.Target == 1 && _layoutDirty) PrepareExpandedHost();
    }

    private void ApplyVisualState()
    {
        var progress = Math.Clamp(_motion.Position, 0, 1);
        Surface.Update(progress, _hover.Position, _press.Position);
        // Reveal text only once the growing shell contains the inner text area; never cut through letters.
        var fade = Math.Clamp((progress - 0.92) / 0.08, 0, 1);
        ContentRoot.Opacity = fade * fade * (3 - 2 * fade);
        _contentTranslation.Y = 5 * (1 - progress);
        var visible = Surface.CurrentBounds;
        _contentClip.Rect = new Rect(visible.X - 10, visible.Y - 10 - _contentTranslation.Y, visible.Width, visible.Height);
        _contentClip.RadiusX = _contentClip.RadiusY = Surface.CurrentRadius;
        ContentRoot.IsHitTestVisible = fade > 0.55 && _motion.Target == 1;
        var capsuleFade = Math.Clamp(progress / 0.36, 0, 1);
        CapsuleHitTarget.Opacity = 1 - capsuleFade * capsuleFade * (3 - 2 * capsuleFade);
        CapsuleHitTarget.IsHitTestVisible = progress < 0.2;
        CapsuleHitTarget.Visibility = progress >= 0.999 && _motion.Target == 1 ? Visibility.Collapsed : Visibility.Visible;
        _capsuleScale.ScaleX = _capsuleScale.ScaleY = 1 + 0.018 * _hover.Position - 0.035 * _press.Position;
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.TotalCount)) return;
        _layoutDirty = true;
        if (_motion.Target != 1) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_motion.Target == 1 && _motion.IsSettled) PrepareExpandedHost();
        }, DispatcherPriority.Loaded);
    }

    private void Capsule_MouseEnter(object sender, MouseEventArgs e) { _hover.Target = 1; StartRendering(); }
    private void Capsule_MouseLeave(object sender, MouseEventArgs e) { _hover.Target = 0; StartRendering(); }

    private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = true; _dragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = _compactLeft; _dragStartTop = _compactTop;
        _press.Reset(1);
        CapsuleHitTarget.CaptureMouse();
        ApplyVisualState();
        e.Handled = true;
    }

    private void Capsule_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var point = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (point.X - _dragStartScreen.X) / dpi.DpiScaleX;
        var dy = (point.Y - _dragStartScreen.Y) / dpi.DpiScaleY;
        if (!_dragging && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
        _dragging = true;
        _motion.Reset(0);
        _compactLeft = _dragStartLeft + dx; _compactTop = _dragStartTop + dy;
        ClampCompactPosition();
        RestoreCompactHost();
    }

    private void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;
        var moved = _dragging;
        _pointerDown = _dragging = false;
        CapsuleHitTarget.ReleaseMouseCapture();
        _press.Target = 0;
        if (moved) { SavePosition(); StartRendering(); }
        else
        {
            if (_motion.Target == 0)
            {
                _viewModel.CheckDayRollover();
                _viewModel.GoToTodayCommand.Execute(null);
            }
            SetExpanded(_motion.Target == 0);
        }
        e.Handled = true;
    }

    private void Capsule_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_pointerDown && _dragging) SavePosition();
        _pointerDown = _dragging = false;
        _press.Target = 0;
        StartRendering();
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_motion.IsSettled) { SetExpanded(_motion.Target == 0); e.Handled = true; }
    }

    private void Surface_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    internal void SettingsMenuClosed(bool pointerInside, bool wasExpanded)
    {
        if (!IsVisible) return;
        if (pointerInside)
        {
            Activate();
            if (wasExpanded) SetExpanded(true);
        }
        else if (AutoCollapseOnDeactivated) SetExpanded(false);
    }

    private void TaskContextMenu_Opened(object sender, RoutedEventArgs e) => _contextMenuOpen = true;
    private void TaskContextMenu_Closed(object sender, RoutedEventArgs e) => _contextMenuOpen = false;

    private void SavePosition()
    {
        _settings.CapsuleLeft = _compactLeft; _settings.CapsuleTop = _compactTop;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IgnoreDeactivationForVerification) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive && _settings.AutoCollapseOnDeactivated && !_contextMenuOpen && HasOpenSettingsMenu?.Invoke() != true)
                SetExpanded(false);
        }, DispatcherPriority.Background);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        _motion.Reset(0); StopRendering(); RestoreCompactHost(); Hide();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _motion.Target == 0) return;
        if (e.OriginalSource is FrameworkElement { DataContext: TaskItemViewModel { IsEditing: true } }) return;
        if (_adding) { SetAdding(false); e.Handled = true; return; }
        SetExpanded(false); e.Handled = true;
    }

    private void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_viewModel.AddTaskCommand.CanExecute(null))
        {
            _viewModel.AddTaskCommand.Execute(null);
            SetAdding(false);
        }
        e.Handled = true;
    }

    private void AddToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_adding) { SetAdding(true); return; }
        if (_viewModel.AddTaskCommand.CanExecute(null)) _viewModel.AddTaskCommand.Execute(null);
        SetAdding(false);
    }

    private void SetAdding(bool open, bool animate = true)
    {
        _adding = open;
        var opacity = InputBackdrop.Opacity;
        var offset = _inputTranslation.X;
        InputBackdrop.BeginAnimation(OpacityProperty, null);
        _inputTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        InputBackdrop.Visibility = Visibility.Visible;
        InputBackdrop.IsHitTestVisible = open;
        InputBackdrop.Opacity = open ? 1 : 0;
        _inputTranslation.X = open ? 0 : 5;
        if (open)
            Dispatcher.BeginInvoke(() => { if (_adding) { NewTaskBox.Focus(); NewTaskBox.CaretIndex = NewTaskBox.Text.Length; } }, DispatcherPriority.Input);
        else
        {
            if (NewTaskBox.IsKeyboardFocusWithin && IsActive) AddToggleButton.Focus();
            FocusManager.SetFocusedElement(this, AddToggleButton);
            if (string.IsNullOrWhiteSpace(_viewModel.NewTaskText)) _viewModel.NewTaskText = string.Empty;
        }
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            if (!open) InputBackdrop.Visibility = Visibility.Hidden;
            return;
        }
        var fade = new DoubleAnimation(opacity, open ? 1 : 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => { if (!_adding) InputBackdrop.Visibility = Visibility.Hidden; };
        InputBackdrop.BeginAnimation(OpacityProperty, fade);
        _inputTranslation.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(open && opacity < 0.01 ? 5 : offset, open ? 0 : 5, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void TaskEditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskItemViewModel item }) return;
        if (e.Key == Key.Enter) { item.SaveEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { item.CancelEdit(); e.Handled = true; }
    }

    private void TaskEditTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox editor && e.NewValue is true)
            Dispatcher.BeginInvoke(() => { editor.Focus(); editor.SelectAll(); }, DispatcherPriority.Input);
    }

    private void SystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.WorkArea) or nameof(SystemParameters.PrimaryScreenWidth))) return;
        ClampCompactPosition();
        if (_hostExpanded) PrepareExpandedHost(); else RestoreCompactHost();
    }

    private void ClampCompactPosition()
    {
        var area = SystemParameters.WorkArea;
        _compactLeft = Math.Clamp(_compactLeft, area.Left, Math.Max(area.Left, area.Right - CompactWidth));
        _compactTop = Math.Clamp(_compactTop, area.Top, Math.Max(area.Top, area.Bottom - CompactHeight));
    }
}
