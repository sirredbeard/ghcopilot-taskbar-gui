using CopilotTaskbarApp.Controls;
using CopilotTaskbarApp.Controls.ChatInput;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Windows.Storage.Pickers;
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

    private CancellationTokenSource? _streamingCts;

    public bool IsStreaming { get; set; }

    public MainWindow()
    {
        InitializeComponent();

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

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Only initialize once
        if (_notifyIcon == null && args.WindowActivationState != WindowActivationState.Deactivated)
        {
            this.Activated -= MainWindow_Activated; // Unsubscribe
            InitializeTrayIcon();

            await SetupChatInputAsync();


        }
    }

    private async Task SetupChatInputAsync()
    {
        // fill models
        chatInput.Models = await _copilotService.GetAvailableModelsAsync();

        if (chatInput.Models.Any())
        {
            chatInput.SelectedModel = chatInput.Models.FirstOrDefault(m => m!.Id == "gpt-4.1") ?? chatInput.Models.First();
        }

        chatInput.AllowedFileExtensions = FileTypesHelpers.GetAllSupportedExtensions().ToList();

        chatInput.MessageSent += ChatInput_MessageSent;
        chatInput.FileSendRequested += ChatInput_FileSendRequested;
        chatInput.RequestHistoryItem += ChatInput_RequestHistoryItem;
        chatInput.StreamingStopRequested += ChatInput_StreamingStopRequested;

    }

    private void ChatInput_RequestHistoryItem(object? sender, int e)
    {
        NavigateHistory(e);
    }

    private async void ChatInput_FileSendRequested(object? sender, EventArgs e)
    {
        if (chatInput.CurrentAttachment != null)
        {
            // Clear current attachment if clicked again
            chatInput.CurrentAttachment = null;
            return;
        }

        FileOpenPicker openPicker = new()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        openPicker.FileTypeFilter.Clear();

        foreach (var ext in FileTypesHelpers.GetAllSupportedExtensions())
        {
            if (!openPicker.FileTypeFilter.Contains(ext))
                openPicker.FileTypeFilter.Add(ext);
        }

        openPicker.FileTypeFilter.Add("*");

        // Since we're in a Page, we need to obtain the parent Window.
        // Replace this with your app-specific method of getting the Window handle.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        var file = await openPicker.PickSingleFileAsync();

        Debug.WriteLine(file != null ? $"Picked file: {file.Name}" : "Operation cancelled.");

        if (file != null)
        {
            var properties = await file.GetBasicPropertiesAsync();

            chatInput.CurrentAttachment = new FileAttachment(file.Path, file.Name, file.FileType, (long)properties.Size);
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
        chatInput.FocusInput();
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
            chatInput.IsEnabled = false;

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
            _messages.Add(errorMessage);
        }

        chatInput.IsEnabled = true;
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
                
                chatInput.IsEnabled = true;
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

            var contextMenu = new WinForms.ContextMenuStrip
            {
                RenderMode = WinForms.ToolStripRenderMode.System,
                Font = new System.Drawing.Font("Segoe UI", 9F)
            };
            
            contextMenu.Items.Add("Show Chat", null, (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() => ShowMainWindow());
            });
            contextMenu.Items.Add("Hide Chat", null, (s, e) => 
            {
                DispatcherQueue.TryEnqueue(() => HideMainWindow());
            });
            contextMenu.Items.Add("-");

            var alwaysOnTopItem = new WinForms.ToolStripMenuItem("Always on Top")
            {
                CheckOnClick = true,
                Checked = _isAlwaysOnTop
            };
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
                    _isExiting = true;
                    _notifyIcon?.Dispose();
                    Application.Current.Exit();
                });
            });
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            _notifyIcon.MouseClick += (s, e) => 
            {
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
                     _currentInput = chatInput.Message ?? "";
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
                    chatInput.Message = _currentInput;
                    return;
                }
                
                _historyIndex = newIndex;
            }

            if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
            {
                chatInput.Message = _commandHistory[_historyIndex];
            }
        }
        catch
        {
            _historyIndex = -1;
        }
    }

    private void ChatInput_StreamingStopRequested(object? sender, EventArgs e)
    {
        _streamingCts?.Cancel();
    }

    private async void ChatInput_MessageSent(object? sender, MessageEventArgs e)
    {
        await SendMessageAsync(e.Message, e.Model, e.Attachment?.FilePath);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "CopilotService.GetResponseAsync uses dynamic for SDK internal types. This is isolated and documented.")]
    private async Task SendMessageAsync(string input, string model, string? attachment)
    {
        try 
        {            
            if (string.IsNullOrEmpty(input))
                return;

            _streamingCts?.Dispose();
            _streamingCts = new CancellationTokenSource();

            chatInput.IsStreaming = true;

            _commandHistory.Add(input);
            _historyIndex = -1;
            _currentInput = "";

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

            var thinkingMessage = new ChatMessage
            {
                Role = "assistant",
                Content = "Thinking...",
                Timestamp = DateTime.Now,
                AvatarImagePath = _copilotAvatarPath
            };
            _messages.Add(thinkingMessage);

            DispatcherQueue.TryEnqueue(() => ScrollToBottom());
            
            var (currentContext, screenshot) = await _contextService.GetContextAsync();
            userMessage.Context = currentContext;

            try
            {
                var recentMessages = _messages.Count > 2 
                    ? _messages.Take(_messages.Count - 2).ToList() 
                    : null;
                
                var responseTask = _copilotService.GetResponseAsync(input, model, currentContext,
                    screenshot, attachment, recentMessages, _streamingCts.Token);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(300), _streamingCts.Token);
                
                var completedTask = await Task.WhenAny(responseTask, timeoutTask);
                
                string response;
                if (_streamingCts.IsCancellationRequested)
                {
                    response = "Request cancelled.";
                }
                else if (completedTask == timeoutTask)
                {
                    response = "Request timed out after 5 minutes. For complex multi-step operations, try breaking them into separate requests.";
                }
                else
                {
                    try
                    {
                        response = await responseTask;
                    }
                    catch (OperationCanceledException)
                    {
                        response = "Request cancelled.";
                    }
                    catch (Exception responseEx)
                    {
                        response = $"Error getting response: {responseEx.Message}\n\nStack trace:\n{responseEx.StackTrace}";
                    }
                }

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
                            
                            _messages.Insert(thinkingIndex, responseMessage);
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
                    thinkingMessage.Role = "system";
                    thinkingMessage.Content = $"Error: {ex.Message}";
                    thinkingMessage.Timestamp = DateTime.Now;
                    ScrollToBottom();
                });
            }
            finally
            {
                chatInput.IsStreaming = false;
                chatInput.FocusInput();
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
                _messages.Add(errorMessage);
                chatInput.IsEnabled = true;
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
}
