namespace CopilotTaskbarApp.Controls.ChatInput
{
    public record FileAttachment
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public long FileSize { get; set; }
    }
}
