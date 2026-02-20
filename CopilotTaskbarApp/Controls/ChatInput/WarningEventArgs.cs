namespace CopilotTaskbarApp.Controls.ChatInput;

public class WarningEventArgs(string title, string mesage) : EventArgs
{
    public string Message { get; } = mesage;
    public string Title { get; } = title;
}
