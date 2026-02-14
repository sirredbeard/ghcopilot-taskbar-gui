using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace CopilotTaskbarApp.Native.Efficiency;

internal static class InteropUtilities
{
    public static BOOL EnsureNonZero(this BOOL value)
    {
        if (value.Value == 0)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        return value;
    }
}
