namespace CopilotTaskbarApp.Native;

internal abstract class WindowSubclassBase : IDisposable
{
    protected readonly Windows.Win32.UI.Shell.SUBCLASSPROC SubclassProc;
    protected readonly Windows.Win32.Foundation.HWND Hwnd;
    protected readonly uint Id;
    private bool disposedValue;

    protected WindowSubclassBase(nint hwnd, uint id)
    {
        Hwnd = (Windows.Win32.Foundation.HWND)hwnd;
        Id = id;
        SubclassProc = new Windows.Win32.UI.Shell.SUBCLASSPROC(WindowSubclassProc);
    }

    protected abstract Windows.Win32.Foundation.LRESULT WindowSubclassProc(
        Windows.Win32.Foundation.HWND hWnd,
        uint uMsg,
        Windows.Win32.Foundation.WPARAM wParam,
        Windows.Win32.Foundation.LPARAM lParam,
        nuint uIdSubclass,
        nuint dwRefData);

    protected void SetupSubclass(uint message)
    {
        var result = Windows.Win32.PInvoke.SetWindowSubclass(Hwnd, SubclassProc, Id, message);
        if (result.Value == 0)
        {
            throw new InvalidOperationException("Failed to set window subclass");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Windows.Win32.PInvoke.RemoveWindowSubclass(Hwnd, SubclassProc, Id);
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
