using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace DailyPlan.Services;

/// <summary>A native notification icon; no Windows Forms or System.Drawing runtime is loaded.</summary>
public sealed class TrayService : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private readonly Window _owner;
    private readonly HwndSource _messages;
    private readonly ContextMenu _menu = new();
    private readonly MenuItem _startupItem = new() { Header = "开机自动启动", IsCheckable = true };
    private readonly MenuItem _autoCollapseItem = new() { Header = "点击别处自动收起", IsCheckable = true };
    private readonly MenuItem _updateItem;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _show;
    private readonly Func<bool> _getAutoCollapse;
    private readonly IntPtr _icon;
    private NotifyIconData _data;
    private bool _disposed;
    private bool _openedFromTray, _wasExpanded;

    public bool IsMenuOpen => _menu.IsOpen;
    internal ContextMenu MenuForVerification => _menu;

    public TrayService(Window owner, Action show, Action exit, Func<bool> getAutoCollapse,
        Action<bool> setAutoCollapse, Action showHistory, Action showUpdates)
    {
        _owner = owner;
        _show = show;
        _getAutoCollapse = getAutoCollapse;
        _messages = new HwndSource(new HwndSourceParameters("plan notification")
        {
            WindowStyle = 0, Width = 0, Height = 0, PositionX = -10000, PositionY = -10000
        });
        _messages.AddHook(MessageHook);
        var icons = new IntPtr[1];
        ExtractIconEx(Environment.ProcessPath!, 0, null, icons, 1);
        _icon = icons[0];
        if (_icon == IntPtr.Zero) throw new InvalidOperationException("无法加载托盘图标。");
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _messages.Handle,
            Id = 1, Flags = 1 | 2 | 4, Callback = CallbackMessage, Icon = _icon,
            Tip = "plan · 每日计划", Info = string.Empty, InfoTitle = string.Empty
        };
        AddIcon();

        var history = Item("工作回顾", showHistory);
        history.Icon = MenuIcon("M2 4 L14 4 L14 15 L2 15 Z M2 7 L14 7 M5 2 L5 5 M11 2 L11 5 M5 10 L7 10 M9 10 L11 10");
        _startupItem.Icon = MenuIcon("M8 1 L8 7 M4 4 C-1 9 3 15 8 15 C13 15 17 9 12 4");
        _autoCollapseItem.Icon = MenuIcon("M2 3 L14 3 L14 13 L2 13 Z M6 7 L8 9 L10 7");
        _menu.Items.Add(history);
        _updateItem = Item("检查更新", showUpdates);
        _updateItem.Icon = MenuIcon("M13 6 A6 6 0 1 0 13 11 M13 2 L13 6 L9 6");
        _menu.Items.Add(_updateItem);
        _menu.Items.Add(new Separator());
        _startupItem.Click += (_, _) =>
        {
            try { StartupService.SetEnabled(_startupItem.IsChecked); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "无法更改开机启动", MessageBoxButton.OK, MessageBoxImage.Warning); }
            Refresh();
        };
        _autoCollapseItem.Click += (_, _) => { setAutoCollapse(_autoCollapseItem.IsChecked); Refresh(); };
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(_autoCollapseItem);
        _menu.Items.Add(new Separator());
        var quit = Item("退出 plan", exit);
        quit.Icon = MenuIcon("M7 2 L2 2 L2 14 L7 14 M7 8 L15 8 M12 5 L15 8 L12 11");
        _menu.Items.Add(quit);
        _menu.Closed += (_, _) =>
        {
            if (_disposed) return;
            var inside = _owner is MainWindow main && main.IsPointerInsidePanel;
            if (_owner is MainWindow current) current.SettingsMenuClosed(inside, _wasExpanded);
            if (_openedFromTray && !inside) ShellNotifyIcon(3, ref _data);
        };
    }

    public void SetUpdateText(string text, bool available)
    {
        _updateItem.Header = text;
        _updateItem.Foreground = new System.Windows.Media.SolidColorBrush(available
            ? System.Windows.Media.Color.FromRgb(219, 77, 140) : System.Windows.Media.Color.FromRgb(89, 107, 139));
    }

    private static System.Windows.Shapes.Path MenuIcon(string geometry) => new()
    {
        Data = System.Windows.Media.Geometry.Parse(geometry), Width = 15, Height = 15,
        Stretch = System.Windows.Media.Stretch.Uniform, StrokeThickness = 1.35,
        Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(115, 137, 175)),
        StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
        StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
    };

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
        return item;
    }

    public void ShowMenu() => ShowMenuCore(false);
    private void ShowFromTray() => ShowMenuCore(true);

    private void ShowMenuCore(bool fromTray)
    {
        _openedFromTray = fromTray;
        _wasExpanded = _owner is MainWindow main && main.IsExpanded;
        if (!_owner.IsVisible) _owner.Show();
        _owner.Activate();
        Refresh();
        _menu.PlacementTarget = _owner;
        _menu.ApplyTemplate();
        _menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (_owner.ActualWidth >= 300 && _owner.ActualHeight >= _menu.DesiredSize.Height + 16)
        {
            var pointer = System.Windows.Input.Mouse.GetPosition(_owner);
            _menu.Placement = PlacementMode.RelativePoint;
            _menu.HorizontalOffset = Math.Clamp(pointer.X - _menu.DesiredSize.Width + 12, 8,
                Math.Max(8, _owner.ActualWidth - _menu.DesiredSize.Width - 8));
            _menu.VerticalOffset = Math.Clamp(pointer.Y + 6, 8,
                Math.Max(8, _owner.ActualHeight - _menu.DesiredSize.Height - 8));
        }
        else
        {
            _menu.Placement = PlacementMode.MousePoint;
            _menu.HorizontalOffset = _menu.VerticalOffset = 0;
        }
        _menu.IsOpen = true;
    }

    private void Refresh()
    {
        try { _startupItem.IsChecked = StartupService.IsEnabled; }
        catch { _startupItem.IsChecked = false; }
        _autoCollapseItem.IsChecked = _getAutoCollapse();
    }

    private void AddIcon() => ShellNotifyIcon(0, ref _data);

    private IntPtr MessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated && !_disposed) AddIcon();
        if (message != CallbackMessage || _disposed) return IntPtr.Zero;
        var notification = (int)lParam & 0xFFFF;
        if (notification is 0x202 or 0x203 or 0x400 or 0x401)
            Application.Current.Dispatcher.BeginInvoke(_show);
        else if (notification is 0x205 or 0x7B)
            Application.Current.Dispatcher.BeginInvoke(ShowFromTray);
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _menu.IsOpen = false;
        ShellNotifyIcon(2, ref _data);
        _messages.RemoveHook(MessageHook);
        _messages.Dispose();
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
