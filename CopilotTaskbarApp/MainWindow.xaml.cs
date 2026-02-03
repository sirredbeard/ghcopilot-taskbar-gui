using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
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
        
        // Initialize tray icon AFTER window is activated
        this.Activated += MainWindow_Activated;
        
        // Don't load old history
        // LoadChatHistory();
        CheckCopilotCli();
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
            // Get user's profile info just for the name/initial
            var users = await Windows.System.User.FindAllAsync();
            var currentUser = users.FirstOrDefault(u => u.Type == Windows.System.UserType.LocalUser) ?? users.FirstOrDefault();

            if (currentUser != null)
            {
                // Get display name
                try 
                {
                    var displayNameObj = await currentUser.GetPropertyAsync(Windows.System.KnownUserProperties.DisplayName);
                    if (displayNameObj != null)
                    {
                        _userDisplayName = displayNameObj.ToString();
                        System.Diagnostics.Debug.WriteLine($"User display name loaded: {_userDisplayName}");
                        
                        // Update existing messages
                        foreach (var msg in _messages)
                        {
                            if (msg.IsUserMessage)
                            {
                                msg.UserName = _userDisplayName;
                            }
                        }
                    }
                }
                catch { }
                
                // SKIPPING Picture loading per request
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user info: {ex.Message}");
        }
        
        // Ensure copilot avatar exists
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var copilotPath = Path.Combine(baseDir, _copilotAvatarPath);
            if (!File.Exists(copilotPath))
            {
                System.Diagnostics.Debug.WriteLine($"Copilot avatar not found at {copilotPath}");
            }
            else
            {
                _copilotAvatarPath = copilotPath;
                // Update assistant messages
                foreach (var msg in _messages)
                {
                    if (!msg.IsUserMessage)
                    {
                        msg.AvatarImagePath = _copilotAvatarPath;
                    }
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

    private async void CheckCopilotCli()
    {
        try
        {
            SendButton.IsEnabled = false;
            InputBox.IsEnabled = false;

            // Check if Copilot CLI is installed
            var status = await CopilotCliDetector.CheckCopilotCliAsync();

            if (!status.IsInstalled)
            {
                // CLI not found - show message only
                var installMessage = new ChatMessage
                {
                    Role = "system",
                    Content = "GitHub Copilot CLI is not installed.\n\n" +
                              "Installation options:\n" +
                              "1. Via winget: winget install --id GitHub.Copilot\n" +
                              "2. Via GitHub CLI: gh extension install github/gh-copilot\n" +
                              "3. Download from: https://docs.github.com/en/copilot/cli\n\n" +
                              "After installation, restart this application.",
                    Timestamp = DateTime.Now,
                    AvatarImagePath = _copilotAvatarPath
                };
                _messages.Add(installMessage);
            }
            else
            {
                // CLI installed - check authentication
                await CheckAuthenticationAsync();
            }
        }
        catch (Exception ex)
        {
            var errorMessage = new ChatMessage
            {
                Role = "system",
                Content = $"Error checking Copilot CLI: {ex.Message}",
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            _messages.Add(errorMessage);
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
                var welcomeMessage = new ChatMessage
                {
                    Role = "system", // Change to system to hide copy button
                    Content = "Not authenticated with GitHub.\n\n" +
                             "To authenticate:\n" +
                             "1. Run: gh auth login\n" +
                             "2. Follow the prompts\n" +
                             "3. Restart this application\n\n" +
                             "Need help? Visit: https://docs.github.com/en/copilot/cli",
                    Timestamp = DateTime.Now,
                    AvatarImagePath = _copilotAvatarPath
                };
                _messages.Add(welcomeMessage);
            }
            else
            {
                var welcomeMessage = new ChatMessage
                {
                    Role = "system", // Change to system to hide copy button
                    Content = "Connected to GitHub Copilot!",
                    Timestamp = DateTime.Now,
                    AvatarImagePath = _copilotAvatarPath
                };
                _messages.Add(welcomeMessage);
                
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
            _messages.Add(errorMessage);
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _notifyIcon?.Dispose();
        await _copilotService.DisposeAsync();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "GitHub Copilot Chat",
                Visible = true
            };

            // Load icon from assets - try copilot-icon.ico first, fallback to github-mark.ico
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
                // Create a simple default icon if file doesn't exist or is 0 bytes
                var bmp = new System.Drawing.Bitmap(32, 32);
                var g = System.Drawing.Graphics.FromImage(bmp);
                g.Clear(System.Drawing.Color.Purple);
                g.FillEllipse(System.Drawing.Brushes.White, 8, 8, 16, 16);
                g.Dispose();
                _notifyIcon.Icon = Icon.FromHandle(bmp.GetHicon());
            }

            // Create context menu
            var contextMenu = new WinForms.ContextMenuStrip();
            
            // Improve rendering for High DPI
            contextMenu.RenderMode = WinForms.ToolStripRenderMode.System;
            
            // Explicitly set font to ensure it's not using a default low-res bitmap font
            contextMenu.Font = new System.Drawing.Font("Segoe UI", 9F);
            
            contextMenu.Items.Add("Show Chat", null, (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() => ShowMainWindow());
            });
            contextMenu.Items.Add("Hide Chat", null, (s, e) => 
            {
                DispatcherQueue.TryEnqueue(() => HideMainWindow());
            });
            contextMenu.Items.Add("-"); // Separator

            var alwaysOnTopItem = new WinForms.ToolStripMenuItem("Always on Top");
            alwaysOnTopItem.CheckOnClick = true;
            alwaysOnTopItem.Checked = _isAlwaysOnTop;
            alwaysOnTopItem.Click += (s, e) => 
            {
                _isAlwaysOnTop = alwaysOnTopItem.Checked;
                DispatcherQueue.TryEnqueue(() => ToggleAlwaysOnTop(_isAlwaysOnTop));
            };
            contextMenu.Items.Add(alwaysOnTopItem);

            contextMenu.Items.Add("Exit", null, (s, e) => 
            {
                DispatcherQueue.TryEnqueue(() => 
                {
                    _isExiting = true; // Allow application to close
                    _notifyIcon?.Dispose();
                    Application.Current.Exit();
                });
            });
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            // Click to toggle window visibility
            _notifyIcon.MouseClick += (s, e) => 
            {
                if (e.Button != WinForms.MouseButtons.Left)
                {
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    if (IsWindowVisible(hwnd))
                    {
                        HideMainWindow();
                    }
                    else
                    {
                        ShowMainWindow();
                    }
                });
            };

            Debug.WriteLine("NotifyIcon initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tray icon initialization error: {ex}");
        }
    }

    private void ToggleAlwaysOnTop(bool enable)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, enable ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    private async void LoadChatHistory()
    {
        var messages = await _persistenceService.LoadMessagesAsync();
        foreach (var msg in messages)
        {
            _messages.Add(msg);
        }
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter key sends message
        if (e.Key == Windows.System.VirtualKey.Enter && 
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            SendButton_Click(sender, null);
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

            // If we are currently editing a new line and press UP, we should go to the last history item
            if (_historyIndex == -1)
            {
                if (direction == -1) // UP
                {
                     _currentInput = InputBox.Text ?? "";
                     _historyIndex = _commandHistory.Count - 1;
                }
                else // DOWN (nothing to do if already at bottom)
                {
                    return;
                }
            }
            else
            {
                // We are navigating history
                // UP (-1) goes to older (lower index)
                // DOWN (+1) goes to newer (higher index)
                
                int newIndex = _historyIndex + direction;

                if (newIndex < 0)
                {
                    // Don't wrap around top
                    newIndex = 0;
                }
                else if (newIndex >= _commandHistory.Count)
                {
                    // Went past the newest item -> restore current input (blank or drafted)
                    _historyIndex = -1;
                    InputBox.Text = _currentInput;
                    InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
                    return;
                }
                
                _historyIndex = newIndex;
            }

            // Display the history item
            if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
            {
                InputBox.Text = _commandHistory[_historyIndex];
                InputBox.SelectionStart = InputBox.Text.Length;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating history: {ex.Message}");
            _historyIndex = -1; // Reset on error
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[SendButton_Click] Button clicked!");
        await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        try 
        {
            System.Diagnostics.Debug.WriteLine("[SendMessageAsync] Method started!");
            
            var input = InputBox.Text?.Trim();
            System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] Input: {input}");
            
            if (string.IsNullOrEmpty(input)) 
            {
                System.Diagnostics.Debug.WriteLine("[SendMessageAsync] Input is empty, returning");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[SendMessageAsync] Proceeding with send...");

            // Add to command history
            _commandHistory.Add(input);
            _historyIndex = -1;
            _currentInput = "";

            InputBox.Text = string.Empty;

            // Optimistic UI update
            var userMessage = new ChatMessage
            {
                Role = "user",
                Content = input,
                Timestamp = DateTime.Now,
                Context = null,
                AvatarImagePath = null,
                UserName = _userDisplayName
            };
            _messages.Add(userMessage);
            await _persistenceService.SaveMessageAsync(userMessage);

            // Add a placeholder "thinking" message
            var thinkingMessage = new ChatMessage
            {
                Role = "assistant",
                Content = "Thinking...",
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            _messages.Add(thinkingMessage);

            DispatcherQueue.TryEnqueue(() => 
            {
                 ScrollToBottom();
            });
            
            // Re-enable input immediately so user can queue another message or continue working
            SendButton.IsEnabled = true;
            
            var (currentContext, screenshot) = await _contextService.GetContextAsync();
            userMessage.Context = currentContext;

            System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] Starting request. Message count: {_messages.Count}");

            try
            {
                // Get recent messages for conversation context (excluding the thinking message we just added)
                var recentMessages = _messages.Count > 2 
                    ? _messages.Take(_messages.Count - 2).ToList() 
                    : null;
                
                var responseTask = _copilotService.GetResponseAsync(input, currentContext, screenshot, recentMessages);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(300));
                
                var completedTask = await Task.WhenAny(responseTask, timeoutTask);
                
                string response;
                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine("[SendMessageAsync] UI timeout after 300 seconds");
                    response = "Request timed out after 5 minutes. For complex multi-step operations, try breaking them into separate requests.";
                }
                else
                {
                    response = await responseTask;
                    System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] Got response: {response?.Substring(0, Math.Min(50, response?.Length ?? 0))}...");
                }

                // Update the thinking message instead of adding a new one
                DispatcherQueue.TryEnqueue(() => 
                {
                    thinkingMessage.Content = response;
                    thinkingMessage.Timestamp = DateTime.Now;
                    _persistenceService.SaveMessageAsync(thinkingMessage); 
                    
                    // Smoothly scroll to the updated message
                    ScrollToBottom();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] Exception: {ex.Message}");
                // Update the thinking message with error
                DispatcherQueue.TryEnqueue(() => 
                {
                    thinkingMessage.Role = "system";
                    thinkingMessage.Content = $"Error: {ex.Message}";
                    thinkingMessage.Timestamp = DateTime.Now;
                    ScrollToBottom();
                });
            }
            finally
            {
                // Focus input for next query
                InputBox.Focus(FocusState.Programmatic);
                
                // Scroll to bottom
                await Task.Delay(100);
                ScrollToBottom();
            }
        }
        catch (Exception criticalEx)
        {
            // Log critical crash
            var tempPath = Path.Combine(Path.GetTempPath(), "copilot_crash.log");
            File.AppendAllText(tempPath, $"{DateTime.Now}: Critical error in SendMessageAsync: {criticalEx}\n");
            
            // Try to show error in UI if possible
            DispatcherQueue.TryEnqueue(() => 
            {
                 var errorMessage = new ChatMessage
                {
                    Role = "system",
                    Content = $"CRITICAL ERROR: {criticalEx.Message}\nSee {tempPath} for details.",
                    Timestamp = DateTime.Now
                };
                _messages.Add(errorMessage);
                SendButton.IsEnabled = true; // Ensure button re-enabled
            });
        }
    }

    private void ScrollToBottom()
    {
        // Scroll to bottom using the outer ScrollViewer if possible, otherwise ListView
        // Since we wrapped ListView in a ScrollViewer, we should scroll that.
        // But finding the ScrollViewer in code-behind from just a name inside XAML structure can be tricky without x:Name
        // Let's assume we can get to it or stick to ListView.ScrollIntoView which MIGHT work if nested correctly.
        
        // Actually, with the nested approach (ScrollViewer > ListView), ListView.ScrollIntoView often fails to scroll the parent.
        // A better approach for the "Scrolling Not Working" bug is to go BACK to just ListView but ensure it gets focus/hit test.
        // BUT user said "Mouse scroll wheel still not working".
        
        // If we revert to just ListView, we must ensure the item container doesn't swallow events.
        // If we use ScrollViewer > ListView, we break virtualization but fix mouse wheel usually.
        
        // Let's try to find the ScrollViewer directly if we named it, or just use UpdateLayout.
        // Wait, I didn't name the ScrollViewer in the previous step. Let me name it "ChatScrollViewer".
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
}
