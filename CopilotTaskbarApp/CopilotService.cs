using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;

namespace CopilotTaskbarApp;

public class CopilotService : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private bool _isStarted;

    public CopilotService()
    {
        _client = new CopilotClient();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            try
            {
                await _client.StartAsync(cancellationToken).ConfigureAwait(false);
                _isStarted = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to start Copilot. Ensure you're authenticated with GitHub.\n\nDetails: {ex.Message}", ex);
            }
        }
    }

    public async Task<string> GetResponseAsync(string prompt, string? context = null, string? imageBase64 = null, List<ChatMessage>? recentMessages = null, CancellationToken cancellationToken = default)
    {
        var methodStartTime = DateTime.UtcNow;
        System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== Request START at {methodStartTime:HH:mm:ss.fff} =====");
        
        try
        {
            var stageStart = DateTime.UtcNow;
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Stage 1 (CLI Start): {(DateTime.UtcNow - stageStart).TotalSeconds:F2}s");

            stageStart = DateTime.UtcNow;
            await using var session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = "gpt-4",
                Streaming = false,
                OnPermissionRequest = PermissionHandler.ApproveAll
            });
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Stage 2 (Session Create): {(DateTime.UtcNow - stageStart).TotalSeconds:F2}s");

            var fullPrompt = prompt;
            if (!string.IsNullOrEmpty(context))
            {
                if (context.Contains("[Active Focus]") || context.Contains("[Open Folders]"))
                {
                    var conversationHistory = "";
                    if (recentMessages != null && recentMessages.Count > 0)
                    {
                        var recentCount = Math.Min(10, recentMessages.Count);
                        var relevant = recentMessages.Skip(Math.Max(0, recentMessages.Count - recentCount)).ToList();
                        
                        conversationHistory = "RECENT CONVERSATION:\n";
                        foreach (var msg in relevant)
                        {
                            var role = msg.Role == "user" ? "User" : "Assistant";
                            conversationHistory += $"{role}: {msg.Content}\n";
                        }
                        conversationHistory += "\n";
                    }
                    
                    fullPrompt = 
                        "You are a desktop assistant integrated into Windows 11. Answer questions based on what the user is actively doing.\n\n" +
                        conversationHistory +
                        "CURRENT CONTEXT:\n" +
                        context + "\n\n" +
                        "RESPONSE GUIDELINES:\n" +
                        "- Avoid markdown formatting (no bold, bullets, or headers) - use plain conversational text\n" +
                        "- When folders/projects are open: mention the specific path and tailor suggestions to that directory\n" +
                        "- Be curious: analyze [Active Focus], [Open Folders], [Open Applications], [Background Services], and [Environment Variables] to understand what the user is working on\n" +
                        "- Use presence of specific processes, services, and environment variables (e.g., PYTHONPATH, NODE_ENV, Docker, WSL) to provide relevant code suggestions and commands\n" +
                        "- MAINTAIN CONTEXT CONTINUITY: Review the RECENT CONVERSATION to understand what environment/tools the user has been working with. If they just installed or used something (e.g., podman in Fedora WSL), assume follow-up questions refer to that same environment unless explicitly stated otherwise\n" +
                        "- When [Active Focus] shows a WSL distribution (e.g., 'FedoraLinux-43 shell'), prioritize that environment for commands and suggestions even if other tools are also available on Windows\n" +
                        "- BE ACTIONABLE: When the user gives an imperative command (install, uninstall, remove, start, stop, restart, configure, create, delete, etc.), execute it immediately and report the result. Do not just provide instructions unless the command is ambiguous or risky without confirmation.\n" +
                        "- REPORT PARTIAL PROGRESS: For multi-step operations, report what succeeded even if a later step failed. Example: 'Successfully installed podman and started MySQL container, but port verification from Windows failed: <error message>'\n" +
                        "- Consistency: If you executed a similar command earlier in the conversation (e.g., installed software), use the same action-oriented approach for related commands (e.g., uninstall that software)\n" +
                        "- Give practical, immediate answers\n" +
                        "- Keep responses concise unless depth is needed\n" +
                        "- ACCURACY IS CRITICAL: Only state facts you are certain about. Do not conflate different people, projects, or events. If unsure, acknowledge uncertainty rather than guessing.\n\n" +
                        $"Current Question: {prompt}";
                }
                else
                {
                    fullPrompt = $"Working directory: {context}\n\n{prompt}";
                }
            }

            if (!string.IsNullOrEmpty(imageBase64))
            {
                fullPrompt += $"\n\n![User Screenshot](data:image/jpeg;base64,{imageBase64})";
                System.Diagnostics.Debug.WriteLine($"[CopilotService] Screenshot attached (~{imageBase64.Length / 1024}KB)");
            }

            System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== PROMPT ({fullPrompt.Length} chars) =====");
            System.Diagnostics.Debug.WriteLine($"[CopilotService] {fullPrompt}");
            System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== END PROMPT =====");
            
            var sendStart = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Stage 3 (Sending to model)...");
            
            // Use event-based API (SDK v0.1.32): subscribe to events, then send
            var responseContent = "";
            var done = new TaskCompletionSource<string>();

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
                        done.TrySetException(new Exception(err.Data.Message ?? "Unknown session error"));
                        break;
                }
            });

            await session.SendAsync(new MessageOptions { Prompt = fullPrompt }).ConfigureAwait(false);

            // Wait for completion with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(300));
            
            using var registration = timeoutCts.Token.Register(() => 
                done.TrySetException(new TimeoutException("Request timed out after 300 seconds.")));

            var result = await done.Task.ConfigureAwait(false);

            var sendElapsed = DateTime.UtcNow - sendStart;
            var totalElapsed = DateTime.UtcNow - methodStartTime;
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Stage 3 (Model Response): {sendElapsed.TotalSeconds:F2}s");
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Total request time: {totalElapsed.TotalSeconds:F2}s");
            
            if (!string.IsNullOrEmpty(result))
            {
                System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== RESPONSE ({result.Length} chars) =====");
                System.Diagnostics.Debug.WriteLine($"[CopilotService] {result}");
                System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== END RESPONSE =====");
                return result;
            }
            
            return "No response received from GitHub Copilot.";
        }
        catch (TimeoutException tex)
        {
            var totalElapsed = DateTime.UtcNow - methodStartTime;
            System.Diagnostics.Debug.WriteLine($"[CopilotService] TIMEOUT after {totalElapsed.TotalSeconds:F2}s!");
            System.Diagnostics.Debug.WriteLine($"[CopilotService] {tex.Message}");
            return $"Request timed out after {totalElapsed.TotalSeconds:F0} seconds. This usually means the Copilot CLI is not responding. Check if 'copilot' command works in your terminal.";
        }
        catch (Exception ex)
        {
            var totalElapsed = DateTime.UtcNow - methodStartTime;
            System.Diagnostics.Debug.WriteLine($"[CopilotService] ERROR after {totalElapsed.TotalSeconds:F2}s: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[CopilotService] {ex.Message}");
            
            var message = ex.Message.ToLower();
            if (message.Contains("auth") || message.Contains("login") || message.Contains("unauthorized"))
            {
                return "Authentication required.\n\n" +
                       "Please authenticate with GitHub:\n" +
                       "Run: gh auth login\n" +
                       "Or visit: https://docs.github.com/en/copilot/cli\n\n" +
                       "Then restart this application.";
            }
            
            return $"Error: {ex.Message}";
        }
    }

    public async Task<bool> CheckAuthenticationAsync()
    {
        try
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isStarted)
        {
            await _client.StopAsync().ConfigureAwait(false);
            _isStarted = false;
        }
    }
}
