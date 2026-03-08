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
- Uses the GitHub Copilot SDK (v0.1.32) for chat functionality with 5-minute timeouts for complex operations
- Integrates with Windows taskbar via System.Windows.Forms.NotifyIcon
- Detects active Windows Explorer folders and applications for context
- Identifies WSL distributions when Windows Terminal shows Unix-style prompts
- Collects relevant environment variables (PYTHONPATH, NODE_ENV, DOTNET_ROOT, etc.)
- Maintains conversation history (last 10 messages) for context continuity
- Uses Windows Accessibility API (UI Automation) as fallback for enhanced context inference
- Shows "Thinking..." placeholder while processing requests
- Persists chat history in SQLite
- Targets .NET 11 Preview with partial trimming on ARM64 and x64
- Full Native AOT disabled due to WinUI 3 incompatibility (data binding, XAML resources)

### Context Inference Strategy

The application infers user intent/questions/problems using a **tiered optimization strategy**:

**Tier 1: Quick Detection (10-50ms)**
- Win32 Z-order walking for active focus
- Detects Explorer paths, Terminal windows, IDEs (VS Code, Visual Studio, Rider)
- Strong context = early exit to skip heavier operations

**Tier 2: Medium Detection (100-200ms)**
- File System Context: Open Explorer windows (Shell COM APIs)
- Application Context: Visible windows (Win32 EnumWindows)
- **Screenshot Capture**: Only when context is weak/ambiguous (Base64 JPEG, 1024px max)
- Runs in parallel with other Tier 2 operations

**Tier 3: Heavy Detection (500ms+)**
- Only for developer scenarios (project folders detected)
- WSL distributions
- Background services (Docker, databases, language servers)

**Always Included:**
- System environment (OS, user)

**Fallback Mechanism:**
- Windows Accessibility API (UI Automation) when Win32 insufficient
- Extracts focused UI element details, control hierarchy, and process info

**Screenshot Optimization:**
- Skipped when strong text context exists (Explorer path, Terminal, IDE)
- Only captured for ambiguous scenarios where visual context adds value
- Prevents unnecessary OCR/vision processing latency on LLM side

**Environment Variables Collected:**
- PATH (filtered to remove common Windows system paths)
- PYTHONPATH, NODE_ENV, JAVA_HOME, GOPATH, CARGO_HOME
- DOTNET_ROOT, DOTNET_CLI_HOME, DOTNET_INSTALL_DIR, MSBuildSDKsPath

**WSL Distribution Detection:**
- Detects Unix-style prompts in Windows Terminal (e.g., "user@hostname:~")
- Checks running WSL distributions via `wsl --list --verbose`
- Reports single running distro, or lists multiple for disambiguation

**Conversation History:**
- Last 10 messages (5 exchanges) included with each request
- Enables context continuity ("install podman" → "uninstall it")
- Model maintains awareness of previous actions and environments

## Key Technical Decisions

1. **System Tray Icon**: Uses official Microsoft System.Windows.Forms.NotifyIcon API instead of third-party libraries for maximum reliability
2. **Deployment**: Self-contained deployment required for unpackaged WinUI 3 applications
3. **SDK Integration**: Direct usage of GitHub.Copilot.SDK NuGet package with JSON-RPC communication to bundled Copilot CLI
4. **Authentication**: Authentication via GitHub CLI (`gh auth login`) required

## System Prompt Guidelines

The application uses a comprehensive system prompt that instructs the model to:

1. **Avoid Markdown**: Use plain conversational text (no bold, bullets, headers)
2. **Context Continuity**: Review recent conversation to understand active environment/tools
3. **Be Actionable**: Execute imperative commands (install, uninstall, start, stop) immediately
4. **Report Partial Progress**: For multi-step operations, report what succeeded even if later steps fail
   - Example: "Successfully installed podman and started MySQL, but port verification failed: [error]"
5. **Maintain Consistency**: Use same action-oriented approach for related commands
6. **Prioritize Context**: When WSL distribution active, prioritize that environment over Windows tools
7. **Accuracy Critical**: Only state facts you're certain about; acknowledge uncertainty

## Important Constraints

- Partial trimming enabled (.NET 11 Preview) for size optimization
- Native AOT not compatible with WinUI 3 (XAML data binding, resources, and dynamic types)
- Single-file publish is incompatible with WinUI 3 + trimming
- The Copilot CLI is bundled with the SDK (no separate installation required)
- Request timeout is 300 seconds (5 minutes) for complex multi-step operations

## Type Safety Considerations

### SDK Type System (v0.1.32)

The GitHub Copilot SDK v0.1.32 exposes strongly-typed public types. The old `dynamic` + `SendAndWaitAsync` pattern is no longer needed.

**Current Approach (event-based API):**
- `CreateSessionAsync` returns `CopilotSession` (strongly typed)
- Use `session.On(handler)` to subscribe to events
- `SendAsync` returns message ID; response comes via `AssistantMessageEvent`
- `SessionIdleEvent` signals completion; `SessionErrorEvent` signals errors
- Use `TaskCompletionSource` to bridge event-based pattern to async/await

**Best Practices:**
```csharp
await using var session = await client.CreateSessionAsync(new SessionConfig { Model = "gpt-4" });
var done = new TaskCompletionSource<string>();
var responseContent = "";

using var subscription = session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageEvent msg:
            responseContent = msg.Data.Content ?? "";
            break;
        case SessionIdleEvent:
            done.TrySetResult(responseContent);
            break;
        case SessionErrorEvent err:
            done.TrySetException(new Exception(err.Data.Message));
            break;
    }
});

await session.SendAsync(new MessageOptions { Prompt = "Hello" });
var result = await done.Task;
```

**Key changes from v0.1.24:**
- `SendAndWaitAsync` removed — use `SendAsync` + event handlers
- `dynamic` no longer needed — all types are public
- Permission handler can be provided via session config
- `PermissionRequestResultKind` is strongly typed (v0.1.31+)
- `session.SetModelAsync()` available for mid-session model switching (v0.1.30+)
- Backward compatibility with v2 CLI servers (v0.1.32)

## Debugging

### CopilotService Diagnostics

Detailed timing diagnostics are logged to Debug Console in VS Code:

1. **Launch with F5** in VS Code (requires debugger attached)
2. **View → Debug Console** to see output
3. **Look for [CopilotService] logs** showing:
   - Stage 1: CLI startup time
   - Stage 2: Session creation time
   - Stage 3: Model response time (this is where most time is spent)
   - Full prompt content
   - Complete model response
   - Total request duration

**Timeout Diagnostics:**
When timeout occurs, logs show:
- Exact stage where timeout happened
- Total elapsed time
- TimeoutException details with stack trace

**Example Output:**
```
[CopilotService] ===== Request START at 14:23:45.123 =====
[CopilotService] Stage 1 (CLI Start): 0.05s
[CopilotService] Stage 2 (Session Create): 0.12s
[CopilotService] ===== PROMPT (2345 chars) =====
[CopilotService] <full prompt content>
[CopilotService] ===== END PROMPT =====
[CopilotService] Stage 3 (Sending to model)...
[CopilotService] Stage 3 (Model Response): 18.42s
[CopilotService] Total request time: 18.59s
[CopilotService] ===== RESPONSE (1234 chars) =====
[CopilotService] <model response>
[CopilotService] ===== END RESPONSE =====
```

## Known Issues

### TextBox Cursor Spacing Bug

**Symptom**: As you type in the input box, increasing space appears between text and cursor

**Root Cause**: WinUI 3 TextBox/RichEditBox layout bug that causes text measurement to desync from cursor position

**Attempted Solutions**:
1. ✗ Variable font removal (Segoe UI Variable → Segoe UI)
2. ✗ Explicit CharacterSpacing=0
3. ✗ Monospace font (Consolas)
4. ✗ UseLayoutRounding=False
5. ✗ RichEditBox control (different rendering path)
6. ✓ AutoSuggestBox control (FIXED)

**Final Solution**: 
```xaml
<AutoSuggestBox x:Name="InputBox"
                PlaceholderText="Ask GitHub Copilot..."
                FontFamily="Segoe UI"
                FontSize="16"
                Padding="12,8,12,8"
                QuerySubmitted="InputBox_QuerySubmitted"
                TextChanged="InputBox_TextChanged"
                KeyDown="InputBox_KeyDown"/>
```

```csharp
private async void InputBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
{
    // Handle Enter key press
    await SendMessageAsync();
}
```

**Key Findings**:
- Issue affects TextBox and RichEditBox controls in WinUI 3
- AutoSuggestBox uses different text rendering implementation that avoids the cursor positioning bug
- Research indicated TextBoxView.cpp measurement/positioning desynchronization in TextBox/RichEditBox
- No documented official fixes from Microsoft for TextBox/RichEditBox
- AutoSuggestBox successfully avoids the issue

**Status**: ✓ Resolved by switching to AutoSuggestBox.

### SDK/CLI Compatibility

**Issue**: SDK versions may require specific CLI versions.
**Resolution**: The SDK now bundles the correct CLI version, reducing compatibility issues.

**Monitoring**: Debug logs will show if CLI startup or session creation fails.

## UI Design Principles

### Typography

WinUI 3 XAML controls use **Segoe UI** (via `ContentControlThemeFontFamily`) with standardized sizes for Windows 11 native appearance. The WinForms-based `Win11ContextMenu` still uses **Segoe UI Variable Text** at 9pt, which is appropriate for that Win32 surface and does not affect WinUI 3 cursor rendering.

- **Standard content**: 18px (chat messages, input box — `FontSize="18"`)
- **Secondary text**: 13px (timestamps, metadata, copy button icons — `CaptionTextBlockStyle`)
- **Speaker labels / headers**: 18–19px SemiBold (`FlyoutSpeakerTextStyle`, `FlyoutHeaderTextStyle`)
- **Tray menu (XAML)**: `ControlContentThemeFontSize` / `BodyTextBlockStyle` (system-scaled, no hardcoded sizes)
- **Tray menu (WinForms)**: Segoe UI Variable Text 9pt

**Text Scaling**: WinUI 3 automatically respects Windows text scaling settings (Settings → Accessibility → Text size). The tray menu uses `ControlContentThemeFontSize` ThemeResource which auto-scales with accessibility settings. Chat content uses a fixed 18px for comfortable reading.

**Font Family Notes**: "Segoe UI Variable Display" and "Segoe UI Variable Text" were removed from XAML/WinUI 3 controls because they contributed to the TextBox cursor spacing bug. Plain "Segoe UI" (via `ContentControlThemeFontFamily`) is used for all WinUI 3 controls. `Win11ContextMenu.cs` intentionally retains "Segoe UI Variable Text" for the WinForms ContextMenuStrip, where the variable font renders correctly.

### Input Control

- **AutoSuggestBox** for message input:
  - Handles Enter key via `QuerySubmitted` event
  - Avoids TextBox/RichEditBox cursor spacing bug
  - Maintains Windows 11 native look and feel
  - Up/Down arrows for command history navigation
