using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Text;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using WinForms = System.Windows.Forms;

namespace CopilotTaskbarApp;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly CopilotService _copilotService;
    private readonly ContextService _contextService;
    private readonly PersistenceService _persistenceService;
    private WinForms.NotifyIcon? _notifyIcon;
    
    // Avatar images
    private string? _userDisplayName;
    private string? _userAvatarPath;
    private string _copilotAvatarPath = "Assets/copilot-logo.png";

    // Command history for up/down arrow navigation
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _currentInput = "";
    
    private Microsoft.UI.Windowing.AppWindow _appWindow;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();
        
        // Ensure WinForms high DPI mode is set for the tray icon context menu
        try 
        {
            WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        }
        catch { /* Might fail if already set, which is fine */ }
        
        // Get AppWindow immediately for event handling
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        
        // Handle closing event to minimize to tray instead of exit
        _appWindow.Closing += AppWindow_Closing;

        _copilotService = new CopilotService();
        _contextService = new ContextService();
        _persistenceService = new PersistenceService();
        
        // Clear old history on startup
        _messages.Clear();
        try { _persistenceService.ClearHistoryAsync().GetAwaiter().GetResult(); } catch { }
        
        this.Activated += MainWindow_Activated;
        
        InitializeCopilot();
        LoadAvatarImages();
        
        Title = "GitHub Copilot Chat";
        
        // Use DesktopAcrylic for "Start Menu" like transparency
        // This requires Windows 10 1809+ (Build 17763)
        if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        }
        else if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
             SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        
        // Set title bar drag region
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        
        // Position window in bottom right corner (1/4 screen size)
        PositionWindowBottomRight();
        
        // Custom Title Bar Setup: Hide system buttons and border to draw our own
        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        
        // Load icon from assets - try copilot-icon.ico first, fallback to github-mark.ico
        var baseDir = AppContext.BaseDirectory;
        var iconPath = Path.Combine(baseDir, "Assets", "copilot-icon.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(baseDir, "Assets", "github-mark.ico");
        }
        
        // Set the AppWindow icon (taskbar/window icon)
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        // Handle window closing - hide instead of close to keep app running
        this.Closed += OnWindowClosed;
    }

    private async void LoadAvatarImages()
    {
        try
        {
            var users = await Windows.System.User.FindAllAsync();
            var currentUser = users.FirstOrDefault(u => u.Type == Windows.System.UserType.LocalUser) ?? users.FirstOrDefault();

            if (currentUser != null)
            {
                try 
                {
                    var displayNameObj = await currentUser.GetPropertyAsync(Windows.System.KnownUserProperties.DisplayName);
                    if (displayNameObj != null)
                    {
                        var fullName = displayNameObj.ToString();
                        _userDisplayName = fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
                        
                        foreach (var msg in _messages.Where(m => m.IsUserMessage))
                        {
                            msg.UserName = _userDisplayName;
                        }
                    }
                }
                catch { }

                try
                {
                    var picStream = await currentUser.GetPictureAsync(Windows.System.UserPictureSize.Size64x64);
                    if (picStream != null)
                    {
                        using var stream = await picStream.OpenReadAsync();
                        if (stream.Size > 0)
                        {
                            var avatarDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                            Directory.CreateDirectory(avatarDir);
                            var avatarPath = Path.Combine(avatarDir, "user-avatar.png");
                            using var fileStream = File.Create(avatarPath);
                            stream.AsStreamForRead().CopyTo(fileStream);
                            _userAvatarPath = avatarPath;

                            foreach (var msg in _messages.Where(m => m.IsUserMessage))
                            {
                                msg.AvatarImagePath = _userAvatarPath;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var copilotPath = Path.Combine(baseDir, _copilotAvatarPath);
            if (File.Exists(copilotPath))
            {
                _copilotAvatarPath = copilotPath;
                foreach (var msg in _messages.Where(m => !m.IsUserMessage))
                {
                    msg.AvatarImagePath = _copilotAvatarPath;
                }
            }
        }
        catch { }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        HideMainWindow();
    }

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        // If not explicitly exiting via tray, cancel close and hide window instead
        if (!_isExiting)
        {
            args.Cancel = true;
            HideMainWindow();
        }
    }

    private void PositionWindowBottomRight()
    {
        // Use the cached AppWindow
        var appWindow = _appWindow;
        
        // TitleBar customization handled in Constructor/XAML now
        
        // Hide min/max buttons (using custom minimize button)
        var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = false;
        }
        
        // Get primary display work area directly
        // DisplayArea.Primary works best for taskbar apps usually
        var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
        var workArea = displayArea.WorkArea;
        var outerBounds = displayArea.OuterBounds;
        
        // 1/4 width, 1/2 height (2x taller)
        int width = workArea.Width / 4;
        int height = workArea.Height / 2; // 2x taller
        
        // Add a small margin from the edges (12px) for better aesthetics
        int marginX = 12;
        int marginY = 12;

        // Smart Detection: If WorkArea height is almost same as OuterBounds height, 
        // it means Taskbar is Auto-Hidden (or not at bottom).
        // In this case, we need extra bottom margin to avoid being covered when Taskbar pops up.
        // Standard taskbar is ~48px.
        if (Math.Abs(workArea.Height - outerBounds.Height) < 50)
        {
             marginY = 48; // Safe zone for auto-hide taskbar
        }
        
        int x = workArea.X + workArea.Width - width - marginX;
        int y = workArea.Y + workArea.Height - height - marginY;
        
        // Set window position and size
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Only initialize once
        if (_notifyIcon == null && args.WindowActivationState != WindowActivationState.Deactivated)
        {
            this.Activated -= MainWindow_Activated; // Unsubscribe
            InitializeTrayIcon();
        }
    }

    private void ShowMainWindow()
    {
        // Show and activate the window
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_SHOW);
        
        // Force window to foreground
        SetForegroundWindow(hwnd);
        this.Activate();
        
        // Reposition to ensure it's in bottom right
        PositionWindowBottomRight();
        
        // Ensure focus is on input box
        InputBox.Focus(FocusState.Programmatic);
    }

    private void HideMainWindow()
    {
        // Hide the window but keep app running
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_HIDE);
    }

    // Win32 API for hiding/showing window
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private bool _isAlwaysOnTop = false;

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        this.Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Application.Current.Exit();
    }

    private async void InitializeCopilot()
    {
        try
        {
            SendButton.IsEnabled = false;
            InputBox.IsEnabled = false;

            // Copilot CLI is now bundled with the SDK, so we assume it is installed.
            // Directly check authentication.
            await CheckAuthenticationAsync();
        }
        catch (Exception ex)
        {
            var errorMessage = new ChatMessage
            {
                Role = "system",
                Content = $"Error initializing Copilot: {ex.Message}",
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            AddMessage(errorMessage);
        }

        SendButton.IsEnabled = true;
        InputBox.IsEnabled = true;
    }

    private async Task CheckAuthenticationAsync()
    {
        try
        {
            var isAuthenticated = await _copilotService.CheckAuthenticationAsync();
            
            if (!isAuthenticated)
            {
                var authInstructions = "To authenticate:\n" +
                                     "1. Run in terminal: gh auth login\n" +
                                     "2. Select 'GitHub.com' -> 'Login with a web browser'\n" +
                                     "3. Follow the prompts to authorize 'GitHub Copilot'\n" +
                                     "4. Restart this application";

                var welcomeMessage = new ChatMessage
                {
                    Role = "system", // Change to system to hide copy button
                    Content = "Not authenticated with GitHub Copilot.\n\n" +
                             authInstructions + "\n\n" +
                             "Need help? Visit: https://docs.github.com/en/copilot/cli",
                    Timestamp = DateTime.Now,
                    AvatarImagePath = _copilotAvatarPath
                };
                AddMessage(welcomeMessage);
            }
            else
            {
                SendButton.IsEnabled = true;
                InputBox.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            var errorMessage = new ChatMessage
            {
                Role = "system",
                Content = $"Error checking authentication: {ex.Message}",
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            AddMessage(errorMessage);
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _notifyIcon?.Dispose();
        await _copilotService.DisposeAsync();
    }

    private TrayMenuWindow? _trayMenu;

    private void InitializeTrayIcon()
    {
        try
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "GitHub Copilot Chat",
                Visible = true
            };

            var baseDir = AppContext.BaseDirectory;
            var iconPath = Path.Combine(baseDir, "Assets", "copilot-icon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(baseDir, "Assets", "github-mark.ico");
            }
            
            if (File.Exists(iconPath) && new FileInfo(iconPath).Length > 0)
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
            else
            {
                var bmp = new System.Drawing.Bitmap(32, 32);
                var g = System.Drawing.Graphics.FromImage(bmp);
                g.Clear(System.Drawing.Color.Purple);
                g.FillEllipse(System.Drawing.Brushes.White, 8, 8, 16, 16);
                g.Dispose();
                _notifyIcon.Icon = Icon.FromHandle(bmp.GetHicon());
            }

            // Create WinUI 3 tray menu (native XAML, not WinForms)
            _trayMenu = new TrayMenuWindow();
            _trayMenu.ShowChatRequested += () => DispatcherQueue.TryEnqueue(ShowMainWindow);
            _trayMenu.HideChatRequested += () => DispatcherQueue.TryEnqueue(HideMainWindow);
            _trayMenu.AlwaysOnTopToggled += (isOn) => 
            {
                _isAlwaysOnTop = isOn;
                DispatcherQueue.TryEnqueue(() => ToggleAlwaysOnTop(isOn));
            };
            _trayMenu.ShowTimestampsToggled += (show) =>
            {
                DispatcherQueue.TryEnqueue(() => ToggleTimestamps(show));
            };
            _trayMenu.ExitRequested += () => DispatcherQueue.TryEnqueue(() =>
            {
                _isExiting = true;
                _notifyIcon?.Dispose();
                Application.Current.Exit();
            });

            // Empty ContextMenuStrip suppresses the default Win32 context menu on right-click
            _notifyIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Opening += (s, e) => e.Cancel = true;

            _notifyIcon.MouseClick += (s, e) => 
            {
                if (e.Button == WinForms.MouseButtons.Right)
                {
                    DispatcherQueue.TryEnqueue(() => _trayMenu.ShowAtCursor());
                    return;
                }
                
                if (e.Button != WinForms.MouseButtons.Left)
                    return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    if (IsWindowVisible(hwnd))
                        HideMainWindow();
                    else
                        ShowMainWindow();
                });
            };
        }
        catch { }
    }

    private void ToggleAlwaysOnTop(bool enable)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, enable ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    private void ToggleTimestamps(bool show)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (var msg in _messages)
            msg.TimestampVisibility = visibility;
        _showTimestamps = show;
    }

    private bool _showTimestamps = false;

    private void AddMessage(ChatMessage msg)
    {
        if (_showTimestamps) msg.TimestampVisibility = Visibility.Visible;
        _messages.Add(msg);
    }

    private void InsertMessage(int index, ChatMessage msg)
    {
        if (_showTimestamps) msg.TimestampVisibility = Visibility.Visible;
        _messages.Insert(index, msg);
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter key sends message
        if (e.Key == Windows.System.VirtualKey.Enter && 
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            SendButton_Click(sender, null!);
        }
        // Up arrow - navigate to previous command
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            e.Handled = true;
            NavigateHistory(-1);
        }
        // Down arrow - navigate to next command
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            e.Handled = true;
            NavigateHistory(1);
        }
    }

    private void NavigateHistory(int direction)
    {
        try
        {
            if (_commandHistory.Count == 0)
                return;

            if (_historyIndex == -1)
            {
                if (direction == -1)
                {
                     _currentInput = InputBox.Text ?? "";
                     _historyIndex = _commandHistory.Count - 1;
                }
                else
                {
                    return;
                }
            }
            else
            {
                int newIndex = _historyIndex + direction;

                if (newIndex < 0)
                {
                    newIndex = 0;
                }
                else if (newIndex >= _commandHistory.Count)
                {
                    _historyIndex = -1;
                    InputBox.Text = _currentInput;
                    return;
                }
                
                _historyIndex = newIndex;
            }

            if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
            {
                InputBox.Text = _commandHistory[_historyIndex];
            }
        }
        catch
        {
            _historyIndex = -1;
        }
    }

    private void InputBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Required event handler for AutoSuggestBox (no autocomplete needed)
    }

    private async void InputBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Handle Enter key press
        await SendMessageAsync();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        try 
        {
            var input = InputBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(input)) 
                return;

            _commandHistory.Add(input);
            _historyIndex = -1;
            _currentInput = "";

            InputBox.Text = string.Empty;

            var userMessage = new ChatMessage
            {
                Role = "user",
                Content = input,
                Timestamp = DateTime.Now,
                Context = null,
                AvatarImagePath = _userAvatarPath,
                UserName = _userDisplayName
            };
            AddMessage(userMessage);
            await _persistenceService.SaveMessageAsync(userMessage);

            var thinkingMessage = new ChatMessage
            {
                Role = "assistant",
                Content = "",
                IsThinking = true,
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            AddMessage(thinkingMessage);

            DispatcherQueue.TryEnqueue(() => ScrollToBottom());
            
            SendButton.IsEnabled = true;
            
            var (currentContext, screenshot) = await _contextService.GetContextAsync();
            userMessage.Context = currentContext;

            try
            {
                var recentMessages = _messages.Count > 2 
                    ? _messages.Take(_messages.Count - 2).ToList() 
                    : null;
                
                var responseTask = _copilotService.GetResponseAsync(input, currentContext, screenshot, recentMessages);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(300));
                
                var completedTask = await Task.WhenAny(responseTask, timeoutTask);
                
                string response;
                if (completedTask == timeoutTask)
                {
                    response = "Request timed out after 5 minutes. For complex multi-step operations, try breaking them into separate requests.";
                }
                else
                {
                    try
                    {
                        response = await responseTask;
                    }
                    catch (Exception responseEx)
                    {
                        response = $"Error getting response: {responseEx.Message}\n\nStack trace:\n{responseEx.StackTrace}";
                    }
                }

                response = NormalizeAssistantResponse(response);

                var tcs = new System.Threading.Tasks.TaskCompletionSource();
                var uiUpdateSuccess = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () => 
                {
                    try
                    {
                        var thinkingIndex = _messages.IndexOf(thinkingMessage);
                        
                        if (thinkingIndex >= 0)
                        {
                            _messages.RemoveAt(thinkingIndex);
                            
                            var responseMessage = new ChatMessage
                            {
                                Role = "assistant",
                                Content = response ?? string.Empty,
                                Timestamp = DateTime.Now,
                                AvatarImagePath = _copilotAvatarPath
                            };
                            
                            InsertMessage(thinkingIndex, responseMessage);
                            _ = _persistenceService.SaveMessageAsync(responseMessage);
                        }
                        
                        ScrollToBottom();
                        tcs.SetResult();
                    }
                    catch (Exception uiEx)
                    {
                        tcs.SetException(uiEx);
                    }
                });
                
                if (uiUpdateSuccess)
                {
                    await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => 
                {
                    thinkingMessage.IsThinking = false;
                    thinkingMessage.Role = "system";
                    thinkingMessage.Content = $"Error: {ex.Message}";
                    thinkingMessage.Timestamp = DateTime.Now;
                    ScrollToBottom();
                });
            }
            finally
            {
                InputBox.Focus(FocusState.Programmatic);
                await Task.Delay(100);
                ScrollToBottom();
            }
        }
        catch (Exception criticalEx)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "copilot_crash.log");
            File.AppendAllText(tempPath, $"{DateTime.Now}: Critical error in SendMessageAsync: {criticalEx}\n");
            
            DispatcherQueue.TryEnqueue(() => 
            {
                 var errorMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"CRITICAL ERROR: {criticalEx.Message}\nSee {tempPath} for details.",
                    Timestamp = DateTime.Now
                };
                AddMessage(errorMessage);
                SendButton.IsEnabled = true;
            });
        }
    }

    private void ScrollToBottom()
    {
        if (ChatScrollViewer != null)
        {
             ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ChatMessage message)
        {
             var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
             dataPackage.SetText(message.Content);
             Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
             
             // Optional: Show a small tooltip or visual feedback?
             // For now, the action is immediate.
        }
    }

    private void MessageContentRichText_Loaded(object sender, RoutedEventArgs e)
    {
        RenderRichTextContent(sender as RichTextBlock);
    }

    private void MessageContentRichText_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        RenderRichTextContent(sender as RichTextBlock);
    }

    private static void RenderRichTextContent(RichTextBlock? richText)
    {
        if (richText is null || richText.DataContext is not ChatMessage message)
        {
            return;
        }

        var text = NormalizeAssistantResponse(message.Content);
        richText.Blocks.Clear();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new Paragraph();
            AddMarkdownInlines(paragraph, line);
            richText.Blocks.Add(paragraph);
        }
    }

    // Group 1 = bold (**text**), Group 2 = italic (*text*)
    [GeneratedRegex(@"\*\*(.+?)\*\*|\*(.+?)\*")]
    private static partial Regex MarkdownInlineRegex();

    private static void AddMarkdownInlines(Paragraph paragraph, string line)
    {
        if (line.Length == 0)
        {
            paragraph.Inlines.Add(new Run { Text = string.Empty });
            return;
        }

        int start = 0;
        foreach (Match match in MarkdownInlineRegex().Matches(line))
        {
            if (match.Index > start)
            {
                paragraph.Inlines.Add(new Run { Text = line[start..match.Index] });
            }

            if (match.Groups[1].Success)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = match.Groups[1].Value,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = match.Groups[2].Value,
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                });
            }

            start = match.Index + match.Length;
        }

        if (start < line.Length)
        {
            paragraph.Inlines.Add(new Run { Text = line[start..] });
        }
    }

    private static string NormalizeAssistantResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return string.Empty;
        }

        // Remove leading whitespace/newlines that create an apparent visual gap under the role label.
        var normalized = response.TrimStart();

        // Collapse excessive blank lines while preserving intentional paragraph breaks.
        normalized = Regex.Replace(normalized, "\\r?\\n{3,}", "\n\n");

        return normalized;
    }
}
