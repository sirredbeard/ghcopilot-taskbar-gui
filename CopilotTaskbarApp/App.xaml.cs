using CopilotTaskbarApp.Native;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace CopilotTaskbarApp;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        try
        {
            InitializeComponent();
            this.UnhandledException += App_UnhandledException;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App constructor error: {ex}");
            NativeHelpers.ShowErrorBox($"App constructor error: {ex.Message}\n\n{ex.StackTrace}", "App Error");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.Exception}");
        NativeHelpers.ShowErrorBox($"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Unhandled Error");
        e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnLaunched error: {ex}");
            NativeHelpers.ShowErrorBox($"OnLaunched error: {ex.Message}\n\n{ex.StackTrace}", "Launch Error");
            throw;
        }
    }


}
