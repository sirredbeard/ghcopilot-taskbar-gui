# GitHub Copilot Taskbar GUI

> **Work in Progress**: Experimental proof-of-concept for deep OS integration with GitHub Copilot. APIs and features subject to change.

.NET 10 WinUI 3 desktop application providing system tray access to GitHub Copilot CLI with automatic context awareness. Detects active focus, open applications, file system state, and running services to augment prompts with relevant environment information.

## Features

- **System Tray Integration**: Windows notification area icon for quick access
- **WinUI 3 Interface**: Native Windows UI with Fluent Design and Mica/DesktopAcrylic backdrop
- **Automatic Context Detection**:
  - Active window focus (Explorer paths, Terminal with WSL distribution detection, IDEs)
  - Open applications and visible windows
  - Background services (Docker, databases, language servers)
  - WSL distributions with smart Unix prompt detection
  - Environment variables (PYTHONPATH, NODE_ENV, DOTNET_ROOT, filtered PATH, etc.)
  - Screenshot capture when context ambiguous (LLM vision only when needed)
- **Conversation History**: Last 10 messages included for context continuity
- **Context Optimization**: Tiered detection (10-500ms) prioritizes fast operations
- **Fallback Mechanisms**: Windows Accessibility API when Win32 insufficient
- **Smart Command Execution**: Imperative commands executed immediately with partial progress reporting
- **"Thinking..." Indicator**: Visual feedback while processing requests
- **Chat Persistence**: SQLite storage for message history
- **GitHub Copilot SDK**: Direct integration with Copilot CLI (v0.1.20, 5-minute timeout for complex operations)

## Prerequisites

- Windows 10 1809+ (Windows 11 recommended)
- .NET 10 SDK
- GitHub Copilot subscription
- GitHub Copilot CLI

### Installing Copilot CLI

The SDK requires separate CLI installation:

```powershell
# Via winget (recommended)
winget install --id GitHub.Copilot

# Via GitHub CLI extension
gh extension install github/gh-copilot

# Verify
copilot --version
```

Authentication:
```powershell
copilot auth login  # or: gh auth login
```

## Build

```powershell
git clone https://github.com/sirredbeard/ghcopilot-taskbar-gui
cd ghcopilot-taskbar-gui\CopilotTaskbarApp
dotnet restore
dotnet build --configuration Release
dotnet run
```

## Usage

Run `CopilotTaskbarApp.exe`. Application icon appears in system tray. Click to open chat interface.

**First Run**:
1. CLI detection runs automatically
2. If missing, offers winget installation
3. Authentication prompts if needed

**Context Gathering**:
- Automatic on every query
- Tier 1 (10-50ms): Active window detection via Win32 Z-order, WSL Unix prompt detection
- Tier 2 (100-200ms): File explorer, applications, environment variables, screenshot (only if context ambiguous)
- Tier 3 (500ms+): WSL distributions list, background services (developer scenarios only)
- Screenshot skipped when strong text context exists (faster responses)
- Environment variables collected: PATH (filtered), PYTHONPATH, NODE_ENV, JAVA_HOME, DOTNET_ROOT, etc.
- Conversation history: Last 10 messages included for contextual awareness

**Smart Features**:
- **Context Continuity**: Remembers previous actions ("install podman" → "uninstall it" works)
- **WSL Distribution Detection**: Recognizes "user@hostname:~" patterns, checks running distros
- **Actionable Commands**: Executes imperative commands immediately (install, start, configure)
- **Partial Progress**: Reports what succeeded even if later steps fail
- **"Thinking..." Indicator**: Shows real-time feedback during long operations

**Keyboard Shortcuts**:
- `Enter`: Send message
- `Shift+Enter`: New line
- `Up/Down`: Command history

## Architecture

### Components

- **MainWindow**: WinUI 3 UI with system tray integration (System.Windows.Forms.NotifyIcon)
- **CopilotService**: GitHub Copilot SDK client wrapper with pattern matching for type safety
- **ContextService**: Multi-tiered context detection (Win32, Shell COM, UI Automation)
- **ScreenshotService**: Automatic screen capture (Base64 JPEG, 1024px max)
- **PersistenceService**: SQLite message storage

### Technologies

- .NET 10 (self-contained deployment required for unpackaged WinUI 3)
- WinUI 3 with Windows App SDK
- GitHub Copilot SDK v0.1.20 (JSON-RPC over stdio)
- System.Windows.Forms.NotifyIcon (official Microsoft API)
- Windows Accessibility API (UI Automation fallback)
- SQLite for persistence

## Project Structure

```
CopilotTaskbarApp/
├── App.xaml.cs              # Entry point
├── MainWindow.xaml.cs       # UI and tray integration
├── CopilotService.cs        # SDK client wrapper
├── ContextService.cs        # Tiered context detection
├── ScreenshotService.cs     # Screen capture
├── PersistenceService.cs    # SQLite storage
├── CopilotCliDetector.cs    # CLI installation checks
├── ChatMessage.cs           # Data model
└── Assets/                  # Icons
```

**Data Directory**: `%LOCALAPPDATA%\CopilotTaskbarApp\chat.db`

## Troubleshooting

**CLI not found**:
```powershell
winget install --id GitHub.Copilot
copilot --version
```

**Authentication errors**:
```powershell
copilot auth login  # or: gh auth login
copilot --version   # verify
```

**Subscription errors**: Verify GitHub Copilot access on your account.

**SDK Notes**:
- SDK communicates via JSON-RPC over stdio
- Starts CLI process automatically in server mode
- Does not bundle or install CLI
- Request timeout: 300 seconds (5 minutes) for complex multi-step operations

**Connection issues**:
1. Check CLI: `copilot --version`
2. Test directly: `copilot chat "test"`
3. Restart application

**Timeout issues**: For complex multi-step commands, try breaking into separate requests. Check debug logs (see Debugging section).

## Debugging

### Viewing CopilotService Debug Output

Detailed diagnostics are available in VS Code Debug Console:

**Setup**:
1. Open project in VS Code
2. Press **F5** to start debugging (or Run → Start Debugging)
3. Debug Console opens automatically showing all output

**What You'll See**:
```
[CopilotService] ===== GetResponseAsync START at 14:23:45.123 =====
[CopilotService] CLI already started, reusing connection
[CopilotService] Stage 1 (CLI Start): 0.05s
[CopilotService] Stage 2 (Session Create): 0.12s
[CopilotService] ===== FULL PROMPT (2345 chars) =====
[CopilotService] You are a desktop assistant...
[CopilotService] <full prompt with context>
[CopilotService] ===== END PROMPT =====
[CopilotService] Stage 3 (Sending to model): Starting at 14:23:45.300...
[CopilotService] Stage 3 (Model Response): 18.42s
[CopilotService] Total request time: 18.59s
[CopilotService] ===== RESPONSE (1234 chars) =====
[CopilotService] <complete model response>
[CopilotService] ===== END RESPONSE =====
```

**Diagnosing Timeouts**:
Logs show exactly where delays occur:
- **Stage 1** (CLI Start): Should be <1s after first request
- **Stage 2** (Session Create): Usually <1s
- **Stage 3** (Model Response): Where most time is spent (varies by complexity)

If timeout occurs:
```
[CopilotService] TIMEOUT after 300.12s!
[CopilotService] Timeout details: SendAndWaitAsync timed out after 00:05:00
[CopilotService] Stack trace: ...
```

**Launch Configuration**: See `.vscode/launch.json` for ARM64/x64 debug settings

## Known Issues

### TextBox Cursor Spacing

**Symptom**: Space gradually appears between typed text and cursor as you type

**Cause**: WinUI 3 TextBox layout bug when combining variable fonts, fixed heights, and padding

**Status**: Partially mitigated by using minimal TextBox properties. May still occur occasionally.

**Workaround**: Restart the application if typing becomes difficult.

**Technical Details**: Issue occurs when WinUI's text measurement desyncs from cursor position calculation. Related to:
- Variable font rendering (Segoe UI Variable)
- Fixed height constraints
- Complex layout property interactions

See AGENTS.md for detailed technical analysis.

### SDK/CLI Version Compatibility

**Symptom**: Authentication errors or session creation failures

**Cause**: SDK v0.1.20 may have compatibility issues with newer CLI versions (v0.0.401+)

**Diagnosis**: Check debug logs for:
```
[CopilotService] CLI startup failed: ...
[CopilotService] Stage 2 (Session Create): <long time or error>
```

**Workaround**: Ensure CLI is properly authenticated:
```powershell
copilot auth login
copilot chat "test"  # Verify CLI works standalone
```

## Troubleshooting

**CLI not found**:
```powershell
winget install --id GitHub.Copilot
copilot --version
```

**Authentication errors**:
```powershell
copilot auth login  # or: gh auth login
copilot --version   # verify
```

**Subscription errors**: Verify GitHub Copilot access on your account.

**SDK Notes**:
- SDK communicates via JSON-RPC over stdio
- Starts CLI process automatically in server mode
- Does not bundle or install CLI

**Connection issues**:
1. Check CLI: `copilot --version`
2. Test directly: `copilot chat "test"`
3. Restart application

## Development

**Build**:
```powershell
dotnet build --configuration Release
```

**Publish** (self-contained required for unpackaged WinUI 3):
```powershell
dotnet publish -c Release -r win-x64    # x64
dotnet publish -c Release -r win-arm64  # ARM64
```

Output: `bin\Release\net10.0-windows10.0.19041.0\{runtime}\publish\`

**Key Dependencies**:
- `Microsoft.WindowsAppSDK` - WinUI 3 framework
- `GitHub.Copilot.SDK` v0.1.20 - Copilot integration
- `Microsoft.Data.Sqlite` - Persistence
- `CommunityToolkit.WinUI.UI.Controls.Markdown` - Message rendering
- Framework references: WindowsForms (NotifyIcon), WPF (UI Automation)

## Technical Notes

**Type Safety**: SDK v0.1.20 uses internal types. Solution uses `dynamic` with pattern matching:
```csharp
if (responseEvent?.Data?.Content is string content) { }
```

**Deployment Constraints**:
- Self-contained deployment required (unpackaged WinUI 3 limitation)
- Single-file publish incompatible with WinUI 3
- Native AOT not supported

**Context Detection Performance**:
- Strong context (Explorer/Terminal/IDE + WSL detection): 15-30ms (no screenshot)
- Weak context (generic app): 250-400ms (includes screenshot + OCR)
- Full developer context: 500-700ms

**Timeout Handling**:
- SDK timeout: 300 seconds (5 minutes)
- UI timeout matches SDK
- Staged diagnostics identify bottlenecks (CLI start, session create, model response)

## License

MIT License - see [LICENSE](LICENSE)
