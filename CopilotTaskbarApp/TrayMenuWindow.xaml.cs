using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;

namespace CopilotTaskbarApp;

public sealed partial class TrayMenuWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const nint WS_CAPTION = 0x00C00000;
    private const nint WS_THICKFRAME = 0x00040000;
    private const nint WS_SYSMENU = 0x00080000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_TOPMOST = 0x00000008;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private static readonly nint WS_POPUP = unchecked((nint)0x80000000L);

    private readonly AppWindow _appWindow;
    private bool _isAlwaysOnTopChecked;
    private bool _showTimestampsChecked;

    public event Action? ShowChatRequested;
    public event Action? HideChatRequested;
    public event Action<bool>? AlwaysOnTopToggled;
    public event Action<bool>? ShowTimestampsToggled;
    public event Action? ExitRequested;

    public TrayMenuWindow()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Borderless popup style via AppWindow
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Strip window chrome via Win32 — this removes the "weird box" border
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_SYSMENU) | WS_POPUP;
        SetWindowLongPtr(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);

        // DWM rounded corners (Win11 native)
        int cornerPref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Acrylic backdrop like native Win11 flyouts
        if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        ExtendsContentIntoTitleBar = true;
        _appWindow.IsShownInSwitchers = false;

        this.Activated += OnActivated;

        BuildMenu();
        _appWindow.Hide();
    }

    private void BuildMenu()
    {
        MenuItems.Children.Clear();

        AddMenuItem("\uE8A7", "Show Chat", () => { Hide(); ShowChatRequested?.Invoke(); });
        AddMenuItem("\uE76B", "Hide Chat", () => { Hide(); HideChatRequested?.Invoke(); });
        AddSeparator();

        AddToggleMenuItem("\uE718", "Always on Top", _isAlwaysOnTopChecked, isChecked =>
        {
            _isAlwaysOnTopChecked = isChecked;
            AlwaysOnTopToggled?.Invoke(isChecked);
        });

        AddToggleMenuItem("\uE823", "Show Timestamps", _showTimestampsChecked, isChecked =>
        {
            _showTimestampsChecked = isChecked;
            ShowTimestampsToggled?.Invoke(isChecked);
        });

        AddSeparator();
        AddMenuItem("\uE7E8", "Exit", () => { Hide(); ExitRequested?.Invoke(); });
    }

    private void AddMenuItem(string glyph, string label, Action action)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Padding = new Thickness(10, 6, 10, 6),
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sp.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = (double)Application.Current.Resources["ControlContentThemeFontSize"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });

        btn.Content = sp;
        btn.Click += (s, e) => action();
        MenuItems.Children.Add(btn);
    }

    private void AddToggleMenuItem(string glyph, string label, bool isChecked, Action<bool> onToggle)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Padding = new Thickness(10, 6, 10, 6),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = (double)Application.Current.Resources["ControlContentThemeFontSize"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(icon, 0);

        var text = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);

        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed
        };
        Grid.SetColumn(checkIcon, 2);

        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(checkIcon);

        btn.Content = grid;
        btn.Click += (s, e) =>
        {
            var newState = checkIcon.Visibility == Visibility.Collapsed;
            checkIcon.Visibility = newState ? Visibility.Visible : Visibility.Collapsed;
            onToggle(newState);
        };

        MenuItems.Children.Add(btn);
    }

    private void AddSeparator()
    {
        MenuItems.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(8, 3, 8, 3),
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        });
    }

    public void ShowAtCursor()
    {
        BuildMenu();

        GetCursorPos(out var cursor);

        // Get DPI from the monitor the cursor is on (not the hidden window)
        const uint MONITOR_DEFAULTTONEAREST = 2;
        var hMonitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
        double scale = dpiX / 96.0;

        // Menu size in physical pixels
        int menuWidth = (int)(210 * scale);
        int menuHeight = (int)(GetMenuHeight() * scale);

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref monitorInfo);
        var workArea = monitorInfo.rcWork;

        // Position above and to the left of cursor (taskbar-style), staying in work area
        int x = cursor.X - menuWidth;
        int y = cursor.Y - menuHeight;

        if (x < workArea.Left) x = workArea.Left;
        if (y < workArea.Top) y = cursor.Y;
        if (x + menuWidth > workArea.Right) x = workArea.Right - menuWidth;
        if (y + menuHeight > workArea.Bottom) y = workArea.Bottom - menuHeight;

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, menuWidth, menuHeight));
        _appWindow.Show();
        this.Activate();
    }

    private int GetMenuHeight()
    {
        // Grid padding: 4px top + 4px bottom = 8
        // Each button: padding 6+6 + ~20px content = ~32px
        // Each separator (Border): 1px + 3+3 margin = 7px
        // StackPanel Spacing=1 between each child
        int height = 8; // Grid padding
        int childCount = MenuItems.Children.Count;
        for (int i = 0; i < childCount; i++)
        {
            var child = MenuItems.Children[i];
            if (child is Button) height += 32;
            else if (child is Border) height += 7;
            if (i < childCount - 1) height += 1; // StackPanel.Spacing
        }
        return height;
    }

    public void Hide()
    {
        _appWindow.Hide();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Hide();
        }
    }
}
