namespace CopilotTaskbarApp.Controls.ChatInput;

public class MessageEventArgs : EventArgs
{
    public string Message { get; set; } = default!;
    public FileAttachment? Attachment { get; set; }
    public string Model { get; internal set; } = default!;
}
