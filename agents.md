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
- Uses the GitHub Copilot SDK (v0.1.20+) for chat functionality with 5-minute timeouts for complex operations
- Integrates with Windows taskbar via System.Windows.Forms.NotifyIcon
- Detects active Windows Explorer folders and applications for context
- Identifies WSL distributions when Windows Terminal shows Unix-style prompts
- Collects relevant environment variables (PYTHONPATH, NODE_ENV, DOTNET_ROOT, etc.)
- Maintains conversation history (last 10 messages) for context continuity
- Uses Windows Accessibility API (UI Automation) as fallback for enhanced context inference
- Shows "Thinking..." placeholder while processing requests
- Persists chat history in SQLite
- Targets .NET 10 with self-contained deployment on ARM64 and x64

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
3. **SDK Integration**: Direct usage of GitHub.Copilot.SDK NuGet package with JSON-RPC communication to Copilot CLI
4. **Authentication**: Leverages underlying Copilot CLI authentication (both `copilot auth login` and `gh auth login` supported)

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

- WinUI 3 unpackaged apps MUST use self-contained deployment
- Single-file publish is incompatible with WinUI 3
- Native AOT is not supported with WinUI 3
- The Copilot CLI must be installed separately (SDK does not bundle it)
- Request timeout is 300 seconds (5 minutes) for complex multi-step operations

## Type Safety Considerations

### SDK Type System (v0.1.20)

The GitHub Copilot SDK v0.1.20 uses internal types that are not fully exposed in the public API surface. This requires careful handling:

**Current Approach:**
- Use `dynamic` for SDK session and response handling
- Apply pattern matching for null-safe access: `if (responseEvent?.Data?.Content is string content)`
- Add inline comments explaining type trade-offs

**Rationale:**
- SDK's `SendAndWaitAsync` returns internal `AssistantMessageEvent?` type
- Session types are not publicly exposed in v0.1.20
- `dynamic` provides flexibility for SDK evolution between versions
- Pattern matching (`is` operator) enables type-safe extraction while working with dynamic

**Best Practices:**
```csharp
// Good: Pattern matching for type-safe null checks
if (responseEvent?.Data?.Content is string content)
{
    return content;
}

// Avoid: Direct access without null safety
string content = responseEvent.Data.Content; // Nullable warning

// Avoid: Excessive null-forgiving operators
string content = responseEvent!.Data!.Content!; // Fragile
```

**Future Improvements:**
When SDK exposes public types (likely v0.2.x+), migrate to:
```csharp
AssistantMessageEvent? responseEvent = await session.SendAndWaitAsync(...);
if (responseEvent?.Data?.Content is string content)
{
    return content;
}
```

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
[CopilotService] ===== GetResponseAsync START at 14:23:45.123 =====
[CopilotService] Stage 1 (CLI Start): 0.05s
[CopilotService] Stage 2 (Session Create): 0.12s
[CopilotService] ===== FULL PROMPT (2345 chars) =====
[CopilotService] <full prompt content>
[CopilotService] ===== END PROMPT =====
[CopilotService] Stage 3 (Sending to model): Starting at 14:23:45.300...
[CopilotService] Stage 3 (Model Response): 18.42s
[CopilotService] Total request time: 18.59s
[CopilotService] ===== RESPONSE (1234 chars) =====
[CopilotService] <model response>
[CopilotService] ===== END RESPONSE =====
```

### Launch Configuration

`.vscode/launch.json` is configured for ARM64 debugging with:
- Pre-launch build task
- Internal console with auto-open
- `justMyCode: false` for SDK debugging

## Known Issues

### TextBox Cursor Spacing Bug

**Symptom**: As you type in the input box, increasing space appears between text and cursor

**Root Cause**: WinUI 3 TextBox measurement bug when combining:
- Variable fonts (Segoe UI Variable Text)
- Fixed height constraints
- Explicit padding values
- VerticalContentAlignment settings

**Current Mitigation**: Minimalist TextBox with only essential properties:
```xaml
<TextBox PlaceholderText="..."
         AcceptsReturn="False"
         TextWrapping="NoWrap"
         IsSpellCheckEnabled="False"
         IsTextPredictionEnabled="False"
         FontSize="{StaticResource StandardFontSize}"/>
```

**Status**: Partially mitigated but may still occur. Avoid adding:
- Explicit `Height`, `MinHeight`, `MaxHeight`
- Custom `Padding` values
- `CharacterSpacing`
- `FontFamily` overrides
- Layout properties like `HorizontalAlignment="Stretch"`

**Workaround**: If issue persists, restart the application.

### SDK/CLI Compatibility

**Issue**: SDK v0.1.20 may have compatibility issues with newer CLI versions
- SDK expects older CLI interface
- CLI v0.0.401+ uses `--acp` mode with different command structure

**Monitoring**: Debug logs will show if CLI startup or session creation fails
