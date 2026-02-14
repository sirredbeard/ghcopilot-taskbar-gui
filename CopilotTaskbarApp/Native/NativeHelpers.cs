using System.Drawing;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace CopilotTaskbarApp.Native;

internal static class NativeHelpers
{
    public static void ShowErrorBox(string message, string caption)
    {
        PInvoke.MessageBox(default, message, caption, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
    }

    public static Rectangle GetVirtualScreenBounds()
    {
        // Determine the bounds of the virtual screen (all monitors) via GetSystemMetrics
        int left = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        int top = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        int width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        int height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        return new Rectangle(left, top, width, height);
    }
}
