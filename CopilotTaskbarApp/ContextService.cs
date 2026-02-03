using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CopilotTaskbarApp;

public class ContextService
{
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

    public async Task<string> GetContextAsync()
    {
        return await Task.Run(async () =>
        {
            try
            {
                var contextBuilder = new System.Text.StringBuilder();

                // Run context gathering tasks in parallel to improve responsiveness
                var activeContextTask = Task.Run(() => GetActiveContext());
                var openFoldersTask = Task.Run(() => GetAllExplorerFolders());
                var openWindowsTask = Task.Run(() => GetOpenWindows());
                var wslStatusTask = Task.Run(() => GetWSLStatus());
                var processesTask = Task.Run(() => GetInterestingProcesses());

                await Task.WhenAll(activeContextTask, openFoldersTask, openWindowsTask, wslStatusTask, processesTask);

                // 1. Focus Context (What is the user looking at RIGHT NOW?)
                // This is the most critical context for immediate user intent
                string activeContext = activeContextTask.Result;
                contextBuilder.AppendLine("[Active Focus]");
                contextBuilder.AppendLine(activeContext);
                contextBuilder.AppendLine();

                // 2. File System Context (All open Explorer windows)
                // Helps infer project scope if multiple folders are open
                var openFolders = openFoldersTask.Result;
                if (openFolders.Count > 0)
                {
                    contextBuilder.AppendLine("[Open Folders]");
                    foreach (var folder in openFolders)
                    {
                        contextBuilder.AppendLine($"- {folder}");
                    }
                    contextBuilder.AppendLine();
                }

                // 3. Application Context (Visible Windows & Terminal Tabs)
                // Note: Standard APIs only see the ACTIVE tab title of a Terminal window.
                contextBuilder.AppendLine("[Open Applications]");
                var windows = openWindowsTask.Result;
                if (windows.Count > 0)
                {
                    foreach (var w in windows)
                    {
                        // Filter out empty or noise titles
                        if (!string.IsNullOrWhiteSpace(w) && w != "Program Manager")
                        {
                            contextBuilder.AppendLine($"- {w}");
                        }
                    }
                }
                else
                {
                    contextBuilder.AppendLine("No other visible windows detected.");
                }
                contextBuilder.AppendLine();

                // 4. WSL Context
                // Important for developer workflows involving Linux subsystems
                string wslInfo = wslStatusTask.Result;
                if (!string.IsNullOrEmpty(wslInfo))
                {
                    contextBuilder.AppendLine("[WSL Distros]");
                    contextBuilder.AppendLine(wslInfo);
                    contextBuilder.AppendLine();
                }

                // 5. Background Services/Processes (Developer Focused)
                // Detects headless tools like Docker, databases, language servers
                string servicesInfo = processesTask.Result;
                if (!string.IsNullOrEmpty(servicesInfo))
                {
                    contextBuilder.AppendLine("[Background Services]");
                    contextBuilder.AppendLine(servicesInfo);
                    contextBuilder.AppendLine();
                }

                // 6. System Environment
                // Basic OS details for troubleshooting or platform-specific commands
                contextBuilder.AppendLine("[System Environment]");
                contextBuilder.AppendLine($"OS: {Environment.OSVersion} (Windows 11 Desktop)");
                contextBuilder.AppendLine($"User: {Environment.UserName}");
                
                return contextBuilder.ToString();
            }
            catch (Exception ex)
            {
                return $"Error retrieving context: {ex.Message}";
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

    private string GetActiveContext()
    {
        try
        {
            // Store found explorer windows: HWND -> Path
            var openExplorers = new Dictionary<IntPtr, string>();

            // 1. Collect all open Explorer windows
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

            // 2. Walk Z-Order to find the most relevant context (Terminal or Explorer)
            // We skip our own process to find what's "under" or recently active
            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr currentHwnd = GetTopWindow(IntPtr.Zero);
            int maxIterations = 500;
            int i = 0;

            while (currentHwnd != IntPtr.Zero && i < maxIterations)
            {
                if (IsWindowVisible(currentHwnd))
                {
                    // A. Check if it's a known Explorer window
                    if (openExplorers.ContainsKey(currentHwnd))
                    {
                        return $"Active Explorer Path: {openExplorers[currentHwnd]}";
                    }

                    // B. Check if it's Windows Terminal
                    GetWindowThreadProcessId(currentHwnd, out uint pid);
                    if (pid != currentPid)
                    {
                        try 
                        {
                            var process = System.Diagnostics.Process.GetProcessById((int)pid);
                            // Also check for other common apps to report "Active Window"
                            var sb = new System.Text.StringBuilder(256);
                            GetWindowText(currentHwnd, sb, sb.Capacity);
                            string windowTitle = sb.ToString();

                            if (!string.IsNullOrEmpty(windowTitle))
                            {
                                if (process.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                                {
                                     return $"Active Terminal: {windowTitle}";
                                }
                                
                                // For the Z-order search, if we hit a substantial window that isn't us, that's likely the "Active" one
                                // But continue searching if it's just a small utility or empty title
                                // For now, return specific context if found, otherwise we'll fallback
                            }
                        }
                        catch { /* Ignore process access errors */ }
                    }
                }

                currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
                i++;
            }

            // Fallback: Return any explorer found if we missed it in Z-order
            if (openExplorers.Count > 0)
            {
                foreach (var path in openExplorers.Values) return $"Active Explorer Path (Fallback): {path}";
            }

            return $"Current Directory: {Environment.CurrentDirectory}";
        }
        catch (Exception ex)
        {
            return $"Error getting active context: {ex.Message}";
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

}

