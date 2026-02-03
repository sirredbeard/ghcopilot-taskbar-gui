# Agent Instructions

This file contains instructions for AI agents working on this project.

## Knowledge Sources

### GitHub Copilot SDK Documentation

When working on this project, agents should consult and update their knowledge from the following official GitHub Copilot SDK resources:

### Primary SDK Documentation
- **Copilot SDK Repository**: https://github.com/github/copilot-sdk/
- **Copilot SDK .NET Implementation**: https://github.com/github/copilot-sdk/tree/main/dotnet

### Cookbook and Examples
- **Copilot SDK .NET Cookbook**: https://github.com/github/awesome-copilot/tree/main/cookbook/copilot-sdk/dotnet 
- **C# Specific Instructions**: https://github.com/github/awesome-copilot/blob/main/instructions/csharp.instructions.md

### SDK .NET Code Examples
- Client.cs: https://github.com/github/copilot-sdk/blob/main/dotnet/src/Client.cs
- Session.cs: https://github.com/github/copilot-sdk/blob/main/dotnet/src/Session.cs
- Types.cs: https://github.com/github/copilot-sdk/blob/main/dotnet/src/Types.cs
- Auto-Generated SessionEvents.cs: https://github.com/github/copilot-sdk/blob/main/dotnet/src/Generated/SessionEvents.cs

## Agent Workflow

Each time this project is revisited:

1. **Check for Updates**: Review the above knowledge sources for any updates to SDK patterns, best practices, or API changes
2. **Apply Latest Patterns**: Ensure the codebase follows current best practices from the Copilot SDK documentation
3. **Validate Implementation**: Verify that SDK usage aligns with official examples and recommendations
4. **Update Documentation**: If SDK changes affect this project, update README.md and code comments accordingly

## Project-Specific Context

This is a WinUI 3 desktop application that:
- Uses the GitHub Copilot SDK (v0.1.20+) for chat functionality
- Integrates with Windows taskbar via System.Windows.Forms.NotifyIcon
- Detects active Windows Explorer folders for context
- Persists chat history in SQLite
- Targets .NET 10 with self-contained deployment

## Key Technical Decisions

1. **System Tray Icon**: Uses official Microsoft System.Windows.Forms.NotifyIcon API instead of third-party libraries for maximum reliability
2. **Deployment**: Self-contained deployment required for unpackaged WinUI 3 applications
3. **SDK Integration**: Direct usage of GitHub.Copilot.SDK NuGet package with JSON-RPC communication to Copilot CLI
4. **Authentication**: Leverages underlying Copilot CLI authentication (both `copilot auth login` and `gh auth login` supported)

## Important Constraints

- WinUI 3 unpackaged apps MUST use self-contained deployment
- Single-file publish is incompatible with WinUI 3
- Native AOT is not supported with WinUI 3
- The Copilot CLI must be installed separately (SDK does not bundle it)

