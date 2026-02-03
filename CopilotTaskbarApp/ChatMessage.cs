using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace CopilotTaskbarApp;

public class ChatMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Context { get; set; }
    
    private string? _userName;
    public string? UserName 
    { 
        get => _userName;
        set 
        {
            if (_userName != value)
            {
                _userName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RoleDisplay));
            }
        }
    }

    public string RoleDisplay
    {
        get
        {
            if (Role == "user" && !string.IsNullOrEmpty(UserName))
            {
                return UserName;
            }
            
            return Role switch
            {
                "user" => "You",
                "assistant" => "GitHub Copilot",
                "system" => "GitHub Copilot",
                _ => Role
            };
        }
    }

    public string AvatarInitial => Role switch
    {
        "user" => "U",
        "assistant" => "C",
        "system" => "C",
        _ => "?"
    };

    public string AvatarGlyph => Role switch
    {
        "user" => "",        // Contact icon (Windows user)
        "assistant" => "",   // Code/AI icon (GitHub Copilot) 
        "system" => "",      // Code/AI icon (GitHub Copilot - same as assistant)
        _ => ""              // Help icon
    };

    private string? _avatarImagePath;
    public string? AvatarImagePath 
    { 
        get => _avatarImagePath;
        set
        {
            if (_avatarImagePath != value)
            {
                _avatarImagePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(ImageVisibility));
                OnPropertyChanged(nameof(GlyphVisibility));
                OnPropertyChanged(nameof(ImageVisibilityFixed));
            }
        }
    }

    public bool HasImage => !string.IsNullOrEmpty(AvatarImagePath);
    
    public Visibility ImageVisibility => HasImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => HasImage ? Visibility.Collapsed : Visibility.Visible;

    public bool IsUserMessage => Role == "user";
    
    public bool IsAssistantMessage => Role == "assistant";

    public string FormattedTime => Timestamp.ToString("g");

    public string UserInitial => !string.IsNullOrEmpty(UserName) ? UserName.Substring(0, 1).ToUpper() : "U";
    
    public Visibility InitialVisibility => IsUserMessage ? Visibility.Visible : Visibility.Collapsed;
    
    public Visibility ImageVisibilityFixed => !IsUserMessage && HasImage ? Visibility.Visible : Visibility.Collapsed;
}
