using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.System;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT;

namespace CopilotTaskbarApp.Native;

public class NativeWindow : IDisposable
{
    private readonly HWND _hwnd;
    private bool disposedValue;
    private SafeFileHandle? _hIcon;
    private const uint TRAY_ICON_ID = 101;

    private const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 1;

    public NativeWindow(IWinRTObject window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _hwnd = (Windows.Win32.Foundation.HWND)hwnd;
    }

    public void SetupIcon(string iconFilePath)
    {
        if (!Path.Exists(iconFilePath))
        {
            throw new FileNotFoundException("Icon file not found", iconFilePath);
        }

        _hIcon = Windows.Win32.PInvoke.LoadImage(null, iconFilePath,
           GDI_IMAGE_TYPE.IMAGE_ICON, 16, 16, IMAGE_FLAGS.LR_LOADFROMFILE);

        var data = new NOTIFYICONDATAW()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = TRAY_ICON_ID,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = new HICON(_hIcon.DangerousGetHandle()),
            szTip = "Chat AI"
        };

        Windows.Win32.PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, data);
    }

    public void BringToFront()
    {
        Windows.Win32.PInvoke.ShowWindow(_hwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_SHOW);
        Windows.Win32.PInvoke.ShowWindow(_hwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_RESTORE);

        _ = Windows.Win32.PInvoke.SetForegroundWindow(_hwnd);
    }

    public void MinimizeToTray()
    {
        Windows.Win32.PInvoke.ShowWindow(_hwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_MINIMIZE);
        Windows.Win32.PInvoke.ShowWindow(_hwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_HIDE);
    }

    public void Hide()
    {
        Windows.Win32.PInvoke.ShowWindow(_hwnd, Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_HIDE);
    }

    public bool IsVisible()
    {
        return Windows.Win32.PInvoke.IsWindowVisible(_hwnd);
    }

    public void SetAlwaysOnTop(bool enable)
    {
        Windows.Win32.PInvoke.SetWindowPos(_hwnd,
            enable ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST,
            0, 0, 0, 0,
            Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOMOVE | Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    protected void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            var data = new NOTIFYICONDATAW()
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID
            };

            Windows.Win32.PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, data);

            //if (_hIcon != default)
            //{
            //    _hIcon.Dispose();
            //}

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private const int HOT_KEY_ID = 1;
    [SupportedOSPlatform("windows6.0.6000")]
    internal void RegisterHotKey(Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS modKey, VirtualKey key)
    {
        Windows.Win32.PInvoke.RegisterHotKey(_hwnd, HOT_KEY_ID, modKey, (uint)key);
    }

    [SupportedOSPlatform("windows5.0")]
    public void UnregisterHotKey()
    {
        Windows.Win32.PInvoke.UnregisterHotKey(_hwnd, HOT_KEY_ID);
    }
}
