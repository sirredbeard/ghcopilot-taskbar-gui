using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using WinForms = System.Windows.Forms;

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
            WinForms.MessageBox.Show($"App constructor error: {ex.Message}\n\n{ex.StackTrace}", 
                "App Error", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.Exception}");
        WinForms.MessageBox.Show($"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", 
            "Unhandled Error", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
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
            WinForms.MessageBox.Show($"OnLaunched error: {ex.Message}\n\n{ex.StackTrace}", 
                "Launch Error", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            throw;
        }
    }
}
