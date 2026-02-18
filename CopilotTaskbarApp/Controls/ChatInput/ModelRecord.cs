namespace CopilotTaskbarApp.Controls.ChatInput;

public record ModelRecord
{
    public ModelRecord(string id, string name, string hint)
    {
        Id = id;
        Name = name;
        Hint = hint;
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string Hint { get; set; }
}
