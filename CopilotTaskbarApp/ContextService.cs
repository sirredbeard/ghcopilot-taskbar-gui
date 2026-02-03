using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CopilotTaskbarApp;

public class ContextService
{
    private readonly ScreenshotService _screenshotService = new();

    // P/Invoke for Z-order checking
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_HWNDNEXT = 2;

    public async Task<(string context, string? screenshot)> GetContextAsync()
    {
        return await Task.Run(async () =>
        {
            try
            {
                var contextBuilder = new System.Text.StringBuilder();

                // Tier 1: Quick Win32 checks (10-50ms)
                var (activeContext, hasStrongContext) = GetActiveContextWithConfidence();
                
                contextBuilder.AppendLine("[Active Focus]");
                contextBuilder.AppendLine(activeContext);
                contextBuilder.AppendLine();

                List<string>? openFolders = null;
                List<string>? windows = null;
                string? screenshot = null;
                
                // Tier 2: Medium operations (~100-200ms)
                if (!hasStrongContext)
                {
                    // No strong context - need comprehensive information including visual
                    var openFoldersTask = Task.Run(() => GetAllExplorerFolders());
                    var openWindowsTask = Task.Run(() => GetOpenWindows());
                    var screenshotTask = Task.Run(() => _screenshotService.CaptureScreenBase64());
                    await Task.WhenAll(openFoldersTask, openWindowsTask, screenshotTask);
                    
                    openFolders = openFoldersTask.Result;
                    windows = openWindowsTask.Result;
                    screenshot = screenshotTask.Result;
                }
                else
                {
                    // Strong context exists (Explorer path, Terminal, IDE) - skip screenshot
                    // Visual context not needed when we have explicit file/application context
                    var quickFoldersTask = Task.Run(() => GetAllExplorerFolders());
                    openFolders = await quickFoldersTask;
                }

                if (openFolders != null && openFolders.Count > 0)
                {
                    contextBuilder.AppendLine("[Open Folders]");
                    foreach (var folder in openFolders)
                    {
                        contextBuilder.AppendLine($"- {folder}");
                    }
                    contextBuilder.AppendLine();
                }

                if (windows != null && windows.Count > 0)
                {
                    contextBuilder.AppendLine("[Open Applications]");
                    foreach (var w in windows)
                    {
                        if (!string.IsNullOrWhiteSpace(w) && w != "Program Manager")
                        {
                            contextBuilder.AppendLine($"- {w}");
                        }
                    }
                    contextBuilder.AppendLine();
                }

                string? wslInfo = null;
                string? servicesInfo = null;

                // Tier 3: Heavy operations (~500ms+)
                if (!hasStrongContext || (openFolders != null && openFolders.Any(f => 
                    f.Contains("\\dev\\", StringComparison.OrdinalIgnoreCase) || 
                    f.Contains("\\projects\\", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("\\source\\", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("\\repos\\", StringComparison.OrdinalIgnoreCase))))
                {
                    var wslTask = Task.Run(() => GetWSLStatus());
                    var processesTask = Task.Run(() => GetInterestingProcesses());
                    await Task.WhenAll(wslTask, processesTask);
                    
                    wslInfo = wslTask.Result;
                    servicesInfo = processesTask.Result;
                }

                if (!string.IsNullOrEmpty(wslInfo))
                {
                    contextBuilder.AppendLine("[WSL Distros]");
                    contextBuilder.AppendLine(wslInfo);
                    contextBuilder.AppendLine();
                }

                if (!string.IsNullOrEmpty(servicesInfo))
                {
                    contextBuilder.AppendLine("[Background Services]");
                    contextBuilder.AppendLine(servicesInfo);
                    contextBuilder.AppendLine();
                }

                contextBuilder.AppendLine("[System Environment]");
                contextBuilder.AppendLine($"OS: {Environment.OSVersion} (Windows 11 Desktop)");
                contextBuilder.AppendLine($"User: {Environment.UserName}");
                
                // Add relevant environment variables
                var envVars = new[] { "PATH", "PYTHONPATH", "NODE_ENV", "JAVA_HOME", "GOPATH", "CARGO_HOME", "DOTNET_ROOT", "DOTNET_CLI_HOME", "DOTNET_INSTALL_DIR", "MSBuildSDKsPath" };
                var presentVars = envVars
                    .Select(v => new { Name = v, Value = Environment.GetEnvironmentVariable(v) })
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToList();
                
                if (presentVars.Any())
                {
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("[Environment Variables]");
                    foreach (var env in presentVars)
                    {
                        var displayValue = env.Value;
                        
                        // Filter common Windows paths from PATH to keep it relevant
                        if (env.Name == "PATH")
                        {
                            var pathParts = env.Value!.Split(';', StringSplitOptions.RemoveEmptyEntries);
                            var commonWindowsPaths = new[]
                            {
                                "\\Windows\\System32", "\\Windows\\SysWOW64", "\\Windows\\Wbem",
                                "\\Windows\\System32\\Wbem", "\\Windows\\System32\\WindowsPowerShell",
                                "\\Windows\\System32\\OpenSSH", "C:\\Windows", "C:\\WINDOWS",
                                "\\Program Files\\Common Files", "\\Common Files\\Oracle\\Java",
                                "\\System32\\Dism"
                            };
                            
                            var filteredPaths = pathParts
                                .Where(p => !commonWindowsPaths.Any(cp => p.Contains(cp, StringComparison.OrdinalIgnoreCase)))
                                .ToList();
                            
                            displayValue = string.Join(";", filteredPaths);
                            
                            // Still truncate if very long after filtering
                            if (displayValue.Length > 300)
                            {
                                displayValue = displayValue.Substring(0, 300) + "...";
                            }
                        }
                        
                        contextBuilder.AppendLine($"{env.Name}={displayValue}");
                    }
                }
                
                return (contextBuilder.ToString(), screenshot);
            }
            catch (Exception ex)
            {
                return ($"Error retrieving context: {ex.Message}", null);
            }
        });
    }

    private string GetWSLStatus()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--list --verbose",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000); // Don't block too long
                
                // Clean up output (remove null bytes if any, trim)
                return output.Trim();
            }
        }
        catch { /* WSL might not be enabled/installed */ }
        return "";
    }

    private string GetInterestingProcesses()
    {
        try
        {
            var interestingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "docker", "dockerd", "wslservice", "python", "node", "java", "postgres", "mysqld", "sqlservr", "nginx", "httpd", "adb"
            };

            var found = new List<string>();
            var processes = System.Diagnostics.Process.GetProcesses();
            
            foreach (var p in processes)
            {
                if (interestingNames.Contains(p.ProcessName))
                {
                    found.Add(p.ProcessName);
                }
            }

            if (found.Count > 0)
            {
                // Return unique sorted list
                return string.Join(", ", found.Distinct().OrderBy(x => x));
            }
        }
        catch { }
        return "";
    }

    private List<string> GetAllExplorerFolders()
    {
        var folders = new List<string>();
        try
        {
            dynamic? shellWindows = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            if (shellWindows != null)
            {
                foreach (var window in shellWindows.Windows())
                {
                    if (window == null) continue;
                    try
                    {
                        string fullName = window.FullName ?? "";
                        var fileName = Path.GetFileNameWithoutExtension(fullName);
                        
                        if (fileName?.Equals("explorer", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            string locationUrl = window.LocationURL ?? "";
                            if (!string.IsNullOrEmpty(locationUrl) && locationUrl.StartsWith("file:///"))
                            {
                                var path = Uri.UnescapeDataString(locationUrl.Replace("file:///", ""));
                                path = path.Replace('/', '\\');
                                if (Directory.Exists(path))
                                {
                                    folders.Add(path);
                                }
                            }
                        }
                    }
                    catch { continue; }
                }
            }
        }
        catch { /* Shell automation failure */ }
        return folders;
    }

    private (string context, bool hasStrongContext) GetActiveContextWithConfidence()
    {
        try
        {
            var openExplorers = new Dictionary<IntPtr, string>();
            try
            {
                dynamic? shellWindows = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                if (shellWindows != null)
                {
                    foreach (var window in shellWindows.Windows())
                    {
                        if (window == null) continue;
                        try
                        {
                            string fullName = window.FullName ?? "";
                            var fileName = Path.GetFileNameWithoutExtension(fullName);
                            
                            if (fileName?.Equals("explorer", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                string locationUrl = window.LocationURL ?? "";
                                if (!string.IsNullOrEmpty(locationUrl) && locationUrl.StartsWith("file:///"))
                                {
                                    var path = Uri.UnescapeDataString(locationUrl.Replace("file:///", ""));
                                    path = path.Replace('/', '\\');
                                    if (Directory.Exists(path))
                                    {
                                        long hwndLong = window.HWND;
                                        openExplorers[new IntPtr(hwndLong)] = path;
                                    }
                                }
                            }
                        }
                        catch { continue; }
                    }
                }
            }
            catch { /* Shell automation failure */ }

            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr currentHwnd = GetTopWindow(IntPtr.Zero);
            int maxIterations = 100;
            int i = 0;

            while (currentHwnd != IntPtr.Zero && i < maxIterations)
            {
                if (IsWindowVisible(currentHwnd))
                {
                    if (openExplorers.ContainsKey(currentHwnd))
                    {
                        return ($"Active Explorer Path: {openExplorers[currentHwnd]}", true);
                    }

                    GetWindowThreadProcessId(currentHwnd, out uint pid);
                    if (pid != currentPid)
                    {
                        try 
                        {
                            var process = System.Diagnostics.Process.GetProcessById((int)pid);
                            var sb = new System.Text.StringBuilder(256);
                            GetWindowText(currentHwnd, sb, sb.Capacity);
                            string windowTitle = sb.ToString();

                            if (!string.IsNullOrEmpty(windowTitle))
                            {
                                if (process.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Check if window title looks like a Unix shell prompt (e.g., "hayden@WDK2023:~" or "user@hostname:~")
                                    if (System.Text.RegularExpressions.Regex.IsMatch(windowTitle, @"^[\w.-]+@[\w.-]+[:/~]"))
                                    {
                                        // This is likely a WSL or SSH session - check running WSL distributions
                                        var wslInfo = GetWSLStatus();
                                        var runningDistros = new List<string>();
                                        
                                        if (!string.IsNullOrEmpty(wslInfo))
                                        {
                                            var lines = wslInfo.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                            foreach (var line in lines)
                                            {
                                                // WSL list format: "  NAME  STATE  VERSION" or "* NAME  STATE  VERSION"
                                                var trimmed = line.Trim().TrimStart('*').Trim();
                                                if (trimmed.Contains("Running", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                                    if (parts.Length >= 2)
                                                    {
                                                        runningDistros.Add(parts[0]);
                                                    }
                                                }
                                            }
                                        }
                                        
                                        // If exactly one WSL distro is running, assume it's that one
                                        if (runningDistros.Count == 1)
                                        {
                                            return ($"Active Application: Windows Terminal (running {runningDistros[0]} shell: {windowTitle})", true);
                                        }
                                        else if (runningDistros.Count > 1)
                                        {
                                            return ($"Active Application: Windows Terminal (running WSL shell: {windowTitle}, possible distros: {string.Join(", ", runningDistros)})", true);
                                        }
                                        else
                                        {
                                            return ($"Active Application: Windows Terminal (running shell: {windowTitle})", true);
                                        }
                                    }
                                    
                                    // Parse traditional shell format (e.g., "PowerShell - Windows Terminal")
                                    var shellParts = windowTitle.Split('-', 2, StringSplitOptions.TrimEntries);
                                    var shellName = shellParts.Length > 0 ? shellParts[0] : "Unknown Shell";
                                    return ($"Active Application: Windows Terminal (running {shellName})", true);
                                }
                                
                                if (process.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("rider64", StringComparison.OrdinalIgnoreCase))
                                {
                                    return ($"Active IDE: {process.ProcessName} - {windowTitle}", true);
                                }
                            }
                        }
                        catch { /* Ignore process access errors */ }
                    }
                }

                currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
                i++;
            }

            if (openExplorers.Count > 0)
            {
                foreach (var path in openExplorers.Values) 
                    return ($"Active Explorer Path (Fallback): {path}", true);
            }

            var accessibilityContext = GetAccessibilityContext();
            if (!string.IsNullOrEmpty(accessibilityContext))
            {
                return (accessibilityContext, false);
            }

            return ($"Current Directory: {Environment.CurrentDirectory}", false);
        }
        catch (Exception ex)
        {
            return ($"Error getting active context: {ex.Message}", false);
        }
    }

    private List<string> GetOpenWindows()
    {
        var windows = new List<string>();
        int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != currentPid)
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        windows.Add(title);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private string GetAccessibilityContext()
    {
        try
        {
            // Get the currently focused UI element
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement == null)
            {
                return string.Empty;
            }

            var contextParts = new List<string>();

            var elementName = focusedElement.Current.Name;
            if (!string.IsNullOrEmpty(elementName))
            {
                contextParts.Add($"Focused Element: {elementName}");
            }

            var controlType = focusedElement.Current.ControlType.ProgrammaticName;
            if (!string.IsNullOrEmpty(controlType))
            {
                var cleanType = controlType.Replace("ControlType.", "");
                contextParts.Add($"Type: {cleanType}");
            }

            var automationId = focusedElement.Current.AutomationId;
            if (!string.IsNullOrEmpty(automationId))
            {
                contextParts.Add($"Control ID: {automationId}");
            }

            if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj) &&
                valuePatternObj is ValuePattern valuePattern)
            {
                var value = valuePattern.Current.Value;
                if (!string.IsNullOrEmpty(value) && value.Length <= 100)
                {
                    contextParts.Add($"Current Value: {value}");
                }
            }

            try
            {
                var window = focusedElement;
                var treeWalker = TreeWalker.ControlViewWalker;
                
                while (window != null && window.Current.ControlType != ControlType.Window)
                {
                    window = treeWalker.GetParent(window);
                }

                if (window != null)
                {
                    var windowName = window.Current.Name;
                    if (!string.IsNullOrEmpty(windowName))
                    {
                        contextParts.Add($"Window: {windowName}");
                    }
                }
            }
            catch { /* Ignore parent traversal errors */ }

            var processId = focusedElement.Current.ProcessId;
            if (processId > 0)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(processId);
                    contextParts.Add($"Application: {process.ProcessName}");
                    
                    try
                    {
                        var workingDir = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(workingDir))
                        {
                            var dir = Path.GetDirectoryName(workingDir);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                contextParts.Add($"App Path: {dir}");
                            }
                        }
                    }
                    catch { /* Access denied to process info */ }
                }
                catch { /* Process may have exited */ }
            }

            if (contextParts.Count > 0)
            {
                return $"Active Focus (Accessibility): {string.Join(" | ", contextParts)}";
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Accessibility API context failed: {ex.Message}");
            return string.Empty;
        }
    }

}

