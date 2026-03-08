# GitHub Copilot Taskbar GUI

<img width="1002" height="657" alt="Screenshot 2026-02-03 010803" src="https://github.com/user-attachments/assets/01d43924-95e5-489b-a352-7bdc80855e2f" />

> This is an experimental proof-of-concept. APIs and features may change.

A Windows desktop app that puts GitHub Copilot in your system tray. It watches what you are doing on your computer and automatically gives Copilot relevant context about your active windows, folders, terminal sessions, and running services so you can ask questions without explaining your setup.

## What it does

- Lives in the Windows system tray for quick access
- Detects what you are working on: open folders, terminals, IDEs, browsers, and background services
- Recognizes WSL distributions when you are using Windows Terminal with Linux
- Takes a screenshot only when it cannot figure out the context from text alone
- Remembers recent conversation so you can say things like "uninstall it" after asking to install something
- Saves chat history locally in SQLite
- Shows a "Thinking..." indicator while waiting for a response

## Requirements

- Windows 10 version 1809 or later (Windows 11 recommended)
- .NET 11 Preview SDK
- GitHub Copilot subscription
- Authenticated with `gh auth login`

## Getting started

Clone and build:

```powershell
git clone https://github.com/sirredbeard/ghcopilot-taskbar-gui
cd ghcopilot-taskbar-gui\CopilotTaskbarApp
dotnet restore
dotnet build --configuration Release
dotnet run
```

When you run the app, an icon appears in the system tray. Click it to open the chat window.

On first launch the app checks for authentication. If you have not logged in yet, it will ask you to run `gh auth login`.

## How context detection works

The app gathers context in three passes, stopping early when it has enough information:

**Fast pass (under 50ms):** Checks which window is in the foreground. If it is File Explorer, a terminal, or an IDE like VS Code or Visual Studio, the app already has strong context and skips the slower steps.

**Medium pass (100-200ms):** Lists open windows and open Explorer folders. Takes a screenshot if the fast pass did not produce clear context.

**Full pass (500ms or more, developer scenarios only):** Checks for running WSL distributions, background services like Docker or databases, and collects environment variables like PATH, PYTHONPATH, NODE_ENV, JAVA_HOME, and DOTNET_ROOT.

When a WSL distribution is active in the terminal, the app detects the Linux prompt pattern and prioritizes that environment.

## Keyboard shortcuts

- Enter: send message
- Up/Down arrows: scroll through previous messages

## Project layout

```
CopilotTaskbarApp/
  App.xaml.cs              Application entry point
  MainWindow.xaml.cs       Chat window and tray icon
  CopilotService.cs        Talks to the Copilot SDK
  ContextService.cs        Gathers context from your desktop
  ScreenshotService.cs     Screen capture for ambiguous scenarios
  PersistenceService.cs    Saves chat history in SQLite
  CopilotCliDetector.cs    Checks CLI availability
  ChatMessage.cs           Message data model
  Win11ContextMenu.cs      Windows 11 styled context menu
  TrayMenuWindow.xaml.cs   Tray popup menu
  Native/                  Windows API interop
  Controls/                Chat input control (in progress)
  Commands/                Async command helpers
  Assets/                  Icons
```

Chat history is stored at `%LOCALAPPDATA%\CopilotTaskbarApp\chat.db`.

## Building for release

The app builds for both x64 and ARM64. It ships as a self-contained package so users do not need .NET installed.

```powershell
dotnet publish -c Release -r win-x64
dotnet publish -c Release -r win-arm64
```

Output goes to `bin\Release\net11.0-windows10.0.19041.0\{runtime}\publish\`.

## CI/CD

Three GitHub Actions workflows handle the release pipeline:

- **build.yml**: Runs on pull requests. Builds both x64 and ARM64. When called by the release workflow, also publishes and uploads zip artifacts.
- **release.yml**: Triggered manually with a version number. Calls the build workflow, creates a git tag, publishes a GitHub Release with both zips attached, and then triggers the WinGet submission.
- **winget-release.yml**: Downloads release assets, computes hashes, generates WinGet manifest files, and submits them to the winget-pkgs repository.

## Troubleshooting

**Authentication errors**: Run `gh auth login` and try again.

**Subscription errors**: Make sure your GitHub account has an active Copilot subscription.

**Timeouts**: The request timeout is 5 minutes. For complex requests, try breaking them into smaller steps. Debug logs in VS Code (press F5, then check the Debug Console) show exactly where the delay is.

**Connection issues**: Restart the app. The Copilot CLI is bundled with the SDK and starts automatically.

## License

[MIT License](LICENSE)

## Trademarks

GitHub, the GitHub logo, GitHub Copilot, and the GitHub Copilot logo are trademarks of GitHub, Inc. This project is not affiliated with, endorsed by, or sponsored by GitHub, Inc.
