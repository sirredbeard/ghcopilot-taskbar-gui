using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;

namespace CopilotTaskbarApp.Native;
internal class WindowTrayHandler : WindowSubclassBase
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOT_KEY_ID = 1;

    private const int MENU_SHOW = 1;
    private const int MENU_HIDE = 2;
    private const int MENU_ALWAYS_ON_TOP = 3;
    private const int MENU_EXIT = 4;

    public event Action<MouseEvent>? TrayIconMouseEventReceived;
    public event Action? HotKeyEventReceived;

    // Menu events
    public event Action? MenuShowWindow;
    public event Action? MenuHideWindow;
    public event Action? MenuToggleAlwaysOnTop;
    public event Action? MenuCloseApplication;

    /// <summary>
    /// Queried when building the context menu to show the check state of "Always on Top".
    /// </summary>
    public Func<bool>? IsAlwaysOnTop { get; set; }

    public WindowTrayHandler(IWinRTObject window)
        : this(WinRT.Interop.WindowNative.GetWindowHandle(window), 102)
    {
    }

    public WindowTrayHandler(nint hwnd, uint id) : base(hwnd, id)
    {
        SetupSubclass(NativeWindow.WM_TRAYICON | WM_HOTKEY);
    }

    protected override Windows.Win32.Foundation.LRESULT WindowSubclassProc(
        Windows.Win32.Foundation.HWND hWnd, uint uMsg,
        Windows.Win32.Foundation.WPARAM wParam, Windows.Win32.Foundation.LPARAM lParam,
        nuint uIdSubclass, nuint dwRefData)
    {
        switch (uMsg)
        {
            case NativeWindow.WM_TRAYICON:
                if (lParam == Windows.Win32.PInvoke.WM_LBUTTONDOWN)
                {
                    TrayIconMouseEventReceived?.Invoke(MouseEvent.IconLeftMouseDown);
                }
                else if (lParam == Windows.Win32.PInvoke.WM_RBUTTONDOWN)
                {
                    ShowTrayMenu();
                }
                break;

            case WM_HOTKEY:
                if (wParam.Value == HOT_KEY_ID)
                {
                    HotKeyEventReceived?.Invoke();
                }
                break;
        }

        return Windows.Win32.PInvoke.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [SupportedOSPlatform("windows5.0")]
    private SafeHandle CreateContextMenu()
    {
        var contextMenu = Windows.Win32.PInvoke.CreatePopupMenu_SafeHandle();

        Windows.Win32.PInvoke.AppendMenu(contextMenu, MENU_ITEM_FLAGS.MF_STRING, MENU_SHOW, "Show Chat");
        Windows.Win32.PInvoke.AppendMenu(contextMenu, MENU_ITEM_FLAGS.MF_STRING, MENU_HIDE, "Hide Chat");
        Windows.Win32.PInvoke.AppendMenu(contextMenu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
        var isOnTop = IsAlwaysOnTop?.Invoke() == true;
        Windows.Win32.PInvoke.AppendMenu(contextMenu,
            MENU_ITEM_FLAGS.MF_STRING | (isOnTop ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED),
            MENU_ALWAYS_ON_TOP, "Always on Top");
        Windows.Win32.PInvoke.AppendMenu(contextMenu, MENU_ITEM_FLAGS.MF_STRING, MENU_EXIT, "Exit");

        return contextMenu;
    }

    [SupportedOSPlatform("windows5.0")]
    private void ShowTrayMenu()
    {
        var contextMenu = CreateContextMenu();

        Windows.Win32.PInvoke.GetCursorPos(out var point);

        Windows.Win32.PInvoke.SetForegroundWindow(Hwnd);

        var selected = Windows.Win32.PInvoke.TrackPopupMenu(contextMenu,
            TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
            point.X, point.Y, Hwnd, null);

        switch (selected)
        {
            case MENU_SHOW:
                MenuShowWindow?.Invoke();
                break;
            case MENU_HIDE:
                MenuHideWindow?.Invoke();
                break;
            case MENU_ALWAYS_ON_TOP:
                MenuToggleAlwaysOnTop?.Invoke();
                break;
            case MENU_EXIT:
                MenuCloseApplication?.Invoke();
                break;
        }
    }
}
