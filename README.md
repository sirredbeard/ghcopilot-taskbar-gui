# GitHub Copilot Taskbar GUI

> **⚠️ CONCEPTUAL WORK IN PROGRESS**
> This application is an experimental proof-of-concept demonstrating deep OS integration with GitHub Copilot. Features, APIs, and UI are subject to change.

A modern WinUI 3 desktop application that provides a beautiful chat interface to GitHub Copilot CLI, accessible from your Windows taskbar. It goes beyond simple chat by understanding your **active desktop context**—open apps, folders, and services—to provide highly relevant AI assistance.

## Features

- **System Tray Integration**: Access GitHub Copilot from a taskbar icon with the GitHub logo
- **Modern WinUI 3 Interface**: Native Windows 11 Fluent Design with Mica backdrop
- **Deep Context Awareness**:
  - Automatically detects **active focus** and **open Explorer folders**
  - Scans **open applications** and **background services** (e.g., Docker, Python)
  - Detects installed **WSL Distributions**
- **Vision Capabilities**: Take and attach screenshots of your desktop directly to the chat
- **Chat Persistence**: Saves your chat history using SQLite
- **GitHub Copilot CLI Integration**: Leverages `gh copilot` extension for AI assistance

## Prerequisites

- **Windows 10** version 1809 or later (Windows 11 recommended for full features)
- **.NET 10 SDK**
- **GitHub account** with Copilot access

### Installing GitHub Copilot CLI

The GitHub Copilot SDK requires the Copilot CLI to be installed separately.

**Easy Install (Recommended):**

The app will detect if Copilot CLI is not installed and offer to install it automatically via winget

**Manual Installation:**

**Option 1: Via winget (Recommended)**
```powershell
winget install --id GitHub.Copilot
```

**Option 2: Via GitHub CLI Extension**
```powershell
# If you have GitHub CLI (gh) installed:
gh extension install github/gh-copilot
```

**Option 3: Direct Download**
https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line

### Verify Installation

```powershell
copilot --version
```

### Authentication

After installing the CLI, authenticate with GitHub:

**If using standalone Copilot CLI:**
```powershell
copilot auth login
```

**If using GitHub CLI with Copilot extension:**
```powershell
gh auth login
```

Follow the prompts to authenticate via web browser.

## Installation

### Build from Source

1. Clone the repository:
   ```powershell
   git clone <repository-url>
   cd ghcopilot-taskbar-gui\CopilotTaskbarApp
   ```

2. Restore dependencies:
   ```powershell
   dotnet restore
   ```

3. Build the application:
   ```powershell
   dotnet build --configuration Release
   ```

4. Run the application:
   ```powershell
   dotnet run
   ```

## Usage

1. **Launch the app**: Run `CopilotTaskbarApp.exe` from the build output
2. **First Run - Automatic Setup**:
   - App detects if Copilot CLI is installed
   - If not found, offers one-click installation via winget
   - Guides you through authentication if needed
3. **System Tray Icon**: Look for the GitHub logo in your system tray
4. **Start Chatting**: Click the icon to open the chat window
5. **Context Detection**: The app automatically detects your active Explorer folder
6. **Ask Questions**: Type your questions and get AI-powered responses

### First-Time Experience

The app makes setup easy:

1. **CLI Detection**: Automatically checks for Copilot CLI
2. **One-Click Install**: If winget is available, install with one click
3. **Authentication**: Follow prompts to authenticate with GitHub
4. **Ready to Chat**: Start asking questions immediately

## Architecture

### Components

- **MainWindow**: WinUI 3 window with chat UI and system tray integration
- **CopilotService**: Wrapper for GitHub Copilot CLI (`gh copilot`)
- **ContextService**: Detects active Windows Explorer folder using Shell COM APIs
- **PersistenceService**: SQLite-based chat history storage

### Technologies

- **.NET 10**: Latest .NET platform
- **WinUI 3**: Modern Windows UI framework with Fluent Design
- **GitHub Copilot SDK**: Official .NET SDK for GitHub Copilot (v0.1.20)
- **H.NotifyIcon.WinUI**: System tray icon support for WinUI 3
- **SQLite**: Local database for chat persistence
- **GitHub Copilot CLI**: Backend AI service

## Project Structure

```
CopilotTaskbarApp/
├── App.xaml              # Application definition
├── App.xaml.cs           # Application entry point
├── MainWindow.xaml       # Main UI layout
├── MainWindow.xaml.cs    # Main window logic
├── ChatMessage.cs        # Message data model
├── CopilotService.cs     # GitHub Copilot CLI wrapper
├── ContextService.cs     # Explorer folder detection
├── PersistenceService.cs # SQLite chat storage
├── Assets/
│   └── github-mark.png   # GitHub logo for tray icon
└── CopilotTaskbarApp.csproj
```

## Configuration

The app stores data in: `%LOCALAPPDATA%\CopilotTaskbarApp\`
- `chat.db`: SQLite database with chat history

## Keyboard Shortcuts

- **Enter**: Send message
- **Shift+Enter**: New line in message input

## Troubleshooting

### "GitHub Copilot CLI not found"

The Copilot CLI must be installed separately:

```powershell
# Via GitHub CLI (recommended)
gh extension install github/gh-copilot

# Verify it's in PATH
gh copilot --version
```

If using standalone CLI, ensure it's added to your system PATH.

### "Not authenticated with GitHub"

Authenticate with GitHub:

```powershell
gh auth login
```

Verify authentication:
```powershell
gh auth status
```

### "Subscription required"

If you see subscription-related errors, ensure you have access to GitHub Copilot through your GitHub account.

### How the SDK works

The GitHub Copilot SDK:
- Communicates with the Copilot CLI in server mode
- Starts the CLI process automatically when needed
- Manages the connection and message passing
- **Does NOT download/install the CLI** - you must install it first

### Connection issues

If you experience connection problems:
1. Verify CLI is installed: `gh copilot --version`
2. Check authentication: `gh auth status`  
3. Test CLI directly: `gh copilot suggest "test"`
4. Restart the application

### Icon not showing

The GitHub logo should be in the `Assets` folder and copied to the output directory during build.

## Development

### Building

```powershell
# Debug build
dotnet build

# Release build
dotnet build --configuration Release

# Clean build
dotnet clean && dotnet build
```

### Publishing (Self-Contained with ReadyToRun)

Build optimized single-file binaries for faster startup:

```powershell
# For x64 (AMD64)
dotnet publish -c Release -r win-x64

# For ARM64
dotnet publish -c Release -r win-arm64
```

Published binaries include:
- **Single-file executable** - All managed code bundled into one .exe
- **ReadyToRun (R2R)** - Pre-compiled code for faster startup
- **Self-contained** - Includes .NET runtime (no installation required)
- Native dependencies extracted on first run

Output location: `bin\Release\net10.0-windows10.0.19041.0\{runtime}\publish\`

### Dependencies

- **Microsoft.WindowsAppSDK**: Windows App SDK for WinUI 3
- **H.NotifyIcon.WinUI**: System tray icon support
- **Microsoft.Data.Sqlite**: SQLite database provider
- **CommunityToolkit.WinUI.Controls**: Additional WinUI controls

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- GitHub Copilot for AI assistance
- Microsoft for WinUI 3 and Windows App SDK
- H.NotifyIcon contributors for system tray support
