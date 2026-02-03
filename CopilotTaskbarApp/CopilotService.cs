using System;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using WinForms = System.Windows.Forms;

namespace CopilotTaskbarApp;

public class CopilotService : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private object? _session; // Using object until we determine the correct type
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
                await _client.StartAsync(cancellationToken);
                _isStarted = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to start Copilot. Ensure you're authenticated with GitHub.\n\nDetails: {ex.Message}", ex);
            }
        }
    }

    public async Task<string> GetResponseAsync(string prompt, string? context = null, string? imageBase64 = null, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[CopilotService] ===== GetResponseAsync START =====");
        
        try
        {
            await EnsureStartedAsync(cancellationToken);

            // Create a new session for this request
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Creating SDK session...");
            dynamic session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = "gpt-4",
                Streaming = true
            }, cancellationToken);

            try
            {
                // Build the prompt with context
                var fullPrompt = prompt;
                if (!string.IsNullOrEmpty(context))
                {
                    if (context.Contains("[Active Focus]") || context.Contains("[Open Folders]"))
                    {
                        fullPrompt = 
                            "You are an intelligent desktop assistant integrated into the Windows 11 Taskbar.\n" +
                            "Your goal is to infer the user's intent based on their active context and query.\n\n" +
                            "CONTEXT SNAPSHOT:\n" +
                            context + "\n\n" +
                            "INSTRUCTIONS:\n" +
                            "1. **Analyze Context:** Check [Active Focus], [Open Folders], [Open Applications], and [Background Services] to ground the query.\n" +
                            "2. **Be Curious:** If the context suggests a complex scenario (e.g., Docker is running but the error is obscure), look for connections between services and open apps. Ask clarifying questions if the snapshot is insufficient.\n" +
                            "3. **Balance Speed vs Depth:** Provide immediate answers for direct questions. For broad problems, propose a step-by-step investigation using the available system tools (WSL, Terminal, etc.).\n" +
                            "4. **Technical Inference:** Use the presence of specific processes (e.g., python, node, wsl) to tailor your code suggestions and commands to the user's actual environment.\n\n" +
                            $"User Query: {prompt}";
                    }
                    else
                    {
                        fullPrompt = $"Working directory: {context}\n\n{prompt}";
                    }
                }

                // Append image if provided (using Markdown Data URI syntax for Vision models)
                if (!string.IsNullOrEmpty(imageBase64))
                {
                    // Note: We place the image at the end of the prompt
                    fullPrompt += $"\n\n![User Screenshot](data:image/jpeg;base64,{imageBase64})";
                    System.Diagnostics.Debug.WriteLine($"[CopilotService] Appended screenshot ({imageBase64.Length} chars)");
                }

                System.Diagnostics.Debug.WriteLine($"[CopilotService] Sending message via SendAndWaitAsync...");
                
                // Use SendAndWaitAsync which handles the wait loop and events internally, returns AssistantMessageEvent?
                dynamic responseEvent = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = fullPrompt }, 
                    TimeSpan.FromSeconds(60), 
                    cancellationToken
                );

                if (responseEvent != null && responseEvent.Data != null)
                {
                    string content = responseEvent.Data.Content;
                    System.Diagnostics.Debug.WriteLine($"[CopilotService] Response received: {content?.Substring(0, Math.Min(50, content?.Length ?? 0))}...");
                    return content ?? "";
                }
                
                return "No response received from GitHub Copilot.";
            }
            finally
            {
                if (session is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
            }
        }
        catch (TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine("[CopilotService] Request timed out!");
            return "Request timed out. The Copilot CLI may not be responding.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CopilotService] Error: {ex}");
            
            // Handle specific errors as before
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

    // Checks if the Copilot CLI is ready and authenticated
    public async Task<bool> CheckAuthenticationAsync()
    {
        try
        {
            await EnsureStartedAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            _session = null;
        }
        
        if (_isStarted)
        {
            await _client.StopAsync();
            _isStarted = false;
        }
    }
}
