using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CopilotTaskbarApp;

public class ContextService
{
    private readonly ScreenshotService _screenshotService = new();

    // ---------------------------------------------------------------------------
    // TTL cache entries
    // ---------------------------------------------------------------------------
    private string? _cachedWslStatus;
    private DateTime _wslCacheExpiry = DateTime.MinValue;
    private const int WslCacheTtlSeconds = 30;

    private string? _cachedProcesses;
    private DateTime _processCacheExpiry = DateTime.MinValue;
    private const int ProcessCacheTtlSeconds = 3;

    // ---------------------------------------------------------------------------
    // P/Invoke — Win32
    // ---------------------------------------------------------------------------
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

    // Clipboard
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint uFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // CreateToolhelp32Snapshot for fast process enumeration
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint CF_UNICODETEXT = 13;

    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "COM interop methods use dynamic for Shell.Application. This is isolated and optional functionality.")]
    public async Task<(string context, string? screenshot)> GetContextAsync()
    {
        return await Task.Run(async () =>
        {
            try
            {
                var contextBuilder = new System.Text.StringBuilder();

                // Single Shell.Application instance shared by Tier 1 + folder enumeration
                var (explorerMap, explorerFolders) = GetExplorerWindowsAndFolders();

                // Tier 1: Quick Win32 checks (10-50ms)
                var (activeContext, hasStrongContext, isIDEActive, isTerminalActive, cachedWslFromTier1) =
                    GetActiveContextWithConfidence(explorerMap);

                contextBuilder.AppendLine("[Active Focus]");
                contextBuilder.AppendLine(activeContext);
                contextBuilder.AppendLine();

                List<string>? windows = null;
                string? screenshot = null;

                // Tier 2: Medium operations (~100-200ms)
                if (!hasStrongContext)
                {
                    // No strong context — need visual + window list
                    var openWindowsTask = Task.Run(() => GetOpenWindowsByProcess());
                    var screenshotTask = Task.Run(() => _screenshotService.CaptureScreenBase64());
                    await Task.WhenAll(openWindowsTask, screenshotTask).ConfigureAwait(false);
                    windows = openWindowsTask.Result;
                    screenshot = screenshotTask.Result;
                }

                if (explorerFolders.Count > 0)
                {
                    contextBuilder.AppendLine("[Open Folders]");
                    foreach (var folder in explorerFolders)
                        contextBuilder.AppendLine($"- {folder}");
                    contextBuilder.AppendLine();
                }

                if (windows != null && windows.Count > 0)
                {
                    contextBuilder.AppendLine("[Open Applications]");
                    foreach (var w in windows)
                        contextBuilder.AppendLine($"- {w}");
                    contextBuilder.AppendLine();
                }

                // Clipboard
                var clipText = GetClipboardText();
                if (!string.IsNullOrEmpty(clipText))
                {
                    contextBuilder.AppendLine("[Clipboard]");
                    contextBuilder.AppendLine(clipText);
                    contextBuilder.AppendLine();
                }

                // Project type detection
                bool isDeveloperFolder = false;
                foreach (var folder in explorerFolders)
                {
                    var projectType = SniffProjectType(folder);
                    if (!string.IsNullOrEmpty(projectType))
                    {
                        isDeveloperFolder = true;
                        contextBuilder.AppendLine($"[Project: {Path.GetFileName(folder.TrimEnd('\\'))}]");
                        contextBuilder.AppendLine(projectType);
                        contextBuilder.AppendLine();
                    }
                    else if (IsDevFolder(folder))
                    {
                        isDeveloperFolder = true;
                    }
                }

                // Git branch for dev folders
                if (isDeveloperFolder || isIDEActive || isTerminalActive)
                {
                    var gitResults = new System.Text.StringBuilder();
                    foreach (var folder in explorerFolders)
                    {
                        var branch = GetGitBranch(folder);
                        if (branch != null)
                            gitResults.AppendLine(branch);
                    }
                    if (gitResults.Length > 0)
                    {
                        contextBuilder.AppendLine("[Git]");
                        contextBuilder.Append(gitResults);
                        contextBuilder.AppendLine();
                    }
                }

                string? wslInfo = null;
                string? servicesInfo = null;

                // Tier 3: Heavy operations — only for developer context
                bool runTier3 = isDeveloperFolder || isIDEActive || isTerminalActive;
                if (runTier3)
                {
                    // Re-use WSL result from Tier 1 if it was already fetched (avoids double spawn)
                    var wslTask = cachedWslFromTier1 != null
                        ? Task.FromResult(cachedWslFromTier1)
                        : Task.Run(() => GetWSLStatusCached());
                    var processesTask = Task.Run(() => GetInterestingProcessesFast());
                    await Task.WhenAll(wslTask, processesTask).ConfigureAwait(false);
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

                // Relevant environment variables
                var envVars = new[] { "PATH", "PYTHONPATH", "NODE_ENV", "JAVA_HOME", "GOPATH", "CARGO_HOME", "DOTNET_ROOT", "DOTNET_CLI_HOME", "DOTNET_INSTALL_DIR", "MSBuildSDKsPath" };
                var presentVars = envVars
                    .Select(v => new { Name = v, Value = Environment.GetEnvironmentVariable(v) })
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToList();

                if (presentVars.Count > 0)
                {
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("[Environment Variables]");
                    foreach (var env in presentVars)
                    {
                        var displayValue = env.Value;
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
                            if (displayValue.Length > 300)
                                displayValue = displayValue[..300] + "...";
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
        }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------
    // WSL — cached, max once per 30 s
    // ---------------------------------------------------------------------------
    private string GetWSLStatusCached()
    {
        if (_cachedWslStatus != null && DateTime.UtcNow < _wslCacheExpiry)
            return _cachedWslStatus;

        var result = RunWSLList();
        _cachedWslStatus = result;
        _wslCacheExpiry = DateTime.UtcNow.AddSeconds(WslCacheTtlSeconds);
        return result;
    }

    private static string RunWSLList()
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
                process.WaitForExit(1000);
                return output.Trim();
            }
        }
        catch { /* WSL not enabled/installed */ }
        return "";
    }

    // ---------------------------------------------------------------------------
    // Process scan — CreateToolhelp32Snapshot (no managed Process objects)
    // Cached for 3 s to avoid repeated scans on follow-up requests
    // ---------------------------------------------------------------------------
    private string GetInterestingProcessesFast()
    {
        if (_cachedProcesses != null && DateTime.UtcNow < _processCacheExpiry)
            return _cachedProcesses;

        var result = ScanInterestingProcesses();
        _cachedProcesses = result;
        _processCacheExpiry = DateTime.UtcNow.AddSeconds(ProcessCacheTtlSeconds);
        return result;
    }

    private static string ScanInterestingProcesses()
    {
        var interestingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "docker", "dockerd", "com.docker.backend", "wslservice",
            "python", "python3", "node", "java", "ruby",
            "postgres", "mysqld", "sqlservr", "mongod", "redis-server",
            "nginx", "httpd", "caddy",
            "adb", "ollama"
        };

        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1))
            return "";

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    // Strip .exe if present
                    var name = entry.szExeFile;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        name = name[..^4];
                    if (interestingNames.Contains(name))
                        found.Add(name.ToLowerInvariant());
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return found.Count > 0 ? string.Join(", ", found.OrderBy(x => x)) : "";
    }

    // ---------------------------------------------------------------------------
    // Shell.Application — single call per request, returns both HWND map + list
    // ---------------------------------------------------------------------------
    [RequiresDynamicCode("COM interop with Shell.Application requires dynamic code generation")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "COM interop for Shell.Application is isolated and optional")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:UnrecognizedReflectionPattern", Justification = "GetTypeFromProgID for Shell.Application is well-known COM type")]
    private static (Dictionary<IntPtr, string> hwndToPath, List<string> folders) GetExplorerWindowsAndFolders()
    {
        var hwndToPath = new Dictionary<IntPtr, string>();
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
                        if (!Path.GetFileNameWithoutExtension(fullName)
                                .Equals("explorer", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string locationUrl = window.LocationURL ?? "";
                        if (!string.IsNullOrEmpty(locationUrl) && locationUrl.StartsWith("file:///"))
                        {
                            var path = Uri.UnescapeDataString(locationUrl.Replace("file:///", ""))
                                          .Replace('/', '\\');
                            if (Directory.Exists(path))
                            {
                                long hwndLong = window.HWND;
                                var hwnd = new IntPtr(hwndLong);
                                hwndToPath[hwnd] = path;
                                if (!folders.Contains(path))
                                    folders.Add(path);
                            }
                        }
                    }
                    catch { continue; }
                }
            }
        }
        catch { /* Shell automation failure */ }
        return (hwndToPath, folders);
    }

    // ---------------------------------------------------------------------------
    // Tier 1: active window detection — returns extra flags for tier gating
    // ---------------------------------------------------------------------------
    [RequiresDynamicCode("COM interop with Shell.Application requires dynamic code generation")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "COM interop for Shell.Application is isolated and optional")]
    private (string context, bool hasStrongContext, bool isIDEActive, bool isTerminalActive, string? wslInfo)
        GetActiveContextWithConfidence(Dictionary<IntPtr, string> explorerMap)
    {
        try
        {
            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr currentHwnd = GetTopWindow(IntPtr.Zero);
            int maxIterations = 100;
            int i = 0;

            while (currentHwnd != IntPtr.Zero && i < maxIterations)
            {
                if (IsWindowVisible(currentHwnd))
                {
                    if (explorerMap.TryGetValue(currentHwnd, out var explorerPath))
                        return ($"Active Explorer Path: {explorerPath}", true, false, false, null);

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
                                    if (Regex.IsMatch(windowTitle, @"^[\w.-]+@[\w.-]+[:/~]"))
                                    {
                                        // WSL/SSH session — fetch WSL list now; will be re-used in Tier 3
                                        var wslInfo = GetWSLStatusCached();
                                        var runningDistros = ParseRunningWslDistros(wslInfo);

                                        string ctx = runningDistros.Count == 1
                                            ? $"Active Application: Windows Terminal (running {runningDistros[0]} shell: {windowTitle})"
                                            : runningDistros.Count > 1
                                                ? $"Active Application: Windows Terminal (running WSL shell: {windowTitle}, possible distros: {string.Join(", ", runningDistros)})"
                                                : $"Active Application: Windows Terminal (running shell: {windowTitle})";
                                        return (ctx, true, false, true, wslInfo);
                                    }

                                    var shellParts = windowTitle.Split('-', 2, StringSplitOptions.TrimEntries);
                                    var shellName = shellParts.Length > 0 ? shellParts[0] : "Unknown Shell";
                                    return ($"Active Application: Windows Terminal (running {shellName})", true, false, true, null);
                                }

                                // Legacy cmd.exe console host
                                if (process.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
                                {
                                    // cmd title formats:
                                    //   "C:\Users\foo\myproject"     (CWD set via PROMPT or cd)
                                    //   "Administrator: Command Prompt - myapp.bat"
                                    //   "Command Prompt"
                                    var cmdCtx = ParseLegacyConsoleTitle(windowTitle, "cmd");
                                    return (cmdCtx, true, false, true, null);
                                }

                                // Windows PowerShell (powershell.exe) and PowerShell 7+ (pwsh.exe)
                                if (process.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
                                {
                                    // powershell title formats:
                                    //   "C:\Users\foo\myproject"
                                    //   "Windows PowerShell"
                                    //   "Administrator: Windows PowerShell"
                                    //   "PowerShell 7 (x64)"
                                    var psLabel = process.ProcessName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
                                        ? "PowerShell 7"
                                        : "Windows PowerShell";
                                    var psCtx = ParseLegacyConsoleTitle(windowTitle, psLabel);
                                    return (psCtx, true, false, true, null);
                                }

                                // ConEmu / Cmder (both host cmd/pwsh but present as their own process)
                                if (process.ProcessName.Equals("ConEmuC64", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("ConEmu64", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("Cmder", StringComparison.OrdinalIgnoreCase))
                                {
                                    return ($"Active Terminal: {process.ProcessName} - {windowTitle}", true, false, true, null);
                                }

                                // IDEs
                                if (process.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase))
                                {
                                    var ideCtx = ParseVSCodeTitle(windowTitle, insiders: false);
                                    return (ideCtx, true, true, false, null);
                                }

                                // VS Code Insiders process is named "Code - Insiders"
                                if (process.ProcessName.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase))
                                {
                                    var ideCtx = ParseVSCodeTitle(windowTitle, insiders: true);
                                    return (ideCtx, true, true, false, null);
                                }

                                if (process.ProcessName.Equals("devenv", StringComparison.OrdinalIgnoreCase))
                                {
                                    var vsCtx = ParseVisualStudioTitle(windowTitle);
                                    return (vsCtx, true, true, false, null);
                                }

                                if (process.ProcessName.Equals("rider64", StringComparison.OrdinalIgnoreCase))
                                {
                                    var riderCtx = ParseRiderTitle(windowTitle);
                                    return (riderCtx, true, true, false, null);
                                }

                                // Browser — try to get URL via UIA
                                if (process.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                                    process.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                                {
                                    var url = GetBrowserUrlViaUia(currentHwnd, process.ProcessName);
                                    var browserCtx = !string.IsNullOrEmpty(url)
                                        ? $"Active Browser: {process.ProcessName} | URL: {url} | Title: {windowTitle}"
                                        : $"Active Browser: {process.ProcessName} - {windowTitle}";
                                    return (browserCtx, false, false, false, null);
                                }
                            }
                        }
                        catch { /* Ignore process access errors */ }
                    }
                }

                currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
                i++;
            }

            if (explorerMap.Count > 0)
            {
                foreach (var path in explorerMap.Values)
                    return ($"Active Explorer Path (Fallback): {path}", true, false, false, null);
            }

            var accessibilityContext = GetAccessibilityContext();
            if (!string.IsNullOrEmpty(accessibilityContext))
                return (accessibilityContext, false, false, false, null);

            return ($"Current Directory: {Environment.CurrentDirectory}", false, false, false, null);
        }
        catch (Exception ex)
        {
            return ($"Error getting active context: {ex.Message}", false, false, false, null);
        }
    }

    // ---------------------------------------------------------------------------
    // Legacy console title parser (cmd.exe, powershell.exe, pwsh.exe)
    // The window title is set to the CWD when the user navigates, e.g.:
    //   "C:\Users\foo\myproject"
    //   "Administrator: Command Prompt - script.bat"
    //   "Windows PowerShell"
    // ---------------------------------------------------------------------------
    private static string ParseLegacyConsoleTitle(string title, string shellLabel)
    {
        // Strip common title prefixes for elevated consoles
        var working = title;
        foreach (var prefix in new[] { "Administrator: ", "SYSTEM: ", "Elevated: " })
        {
            if (working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                working = working[prefix.Length..].Trim();
                shellLabel = $"{shellLabel} (Administrator)";
                break;
            }
        }

        // If the title is (or ends with) a rooted Windows path, treat it as the CWD
        // Formats: "C:\some\path" or "cmd.exe - C:\some\path" or "C:\path - extra"
        // Try to extract a path segment
        var pathMatch = Regex.Match(working, @"([A-Za-z]:\\[^\n""*?<>|]*)");
        if (pathMatch.Success)
        {
            var path = pathMatch.Groups[1].Value.TrimEnd('\\', ' ', '-');
            if (Directory.Exists(path))
                return $"Active Terminal: {shellLabel} | CWD: {path}";
            // Even if the directory no longer exists (e.g., temp), still report it
            return $"Active Terminal: {shellLabel} | CWD: {path}";
        }

        // Unix-style path in title (WSL launched via legacy host — rare but possible)
        if (Regex.IsMatch(working, @"^[\w.-]+@[\w.-]+[:/~]"))
            return $"Active Terminal: {shellLabel} (WSL shell: {working})";

        // Generic: just report the title as-is if it's not the default shell name
        var defaultNames = new[] { "command prompt", "windows powershell", "powershell", "pwsh" };
        if (!defaultNames.Any(d => working.Equals(d, StringComparison.OrdinalIgnoreCase)))
            return $"Active Terminal: {shellLabel} - {working}";

        return $"Active Terminal: {shellLabel}";
    }

    // ---------------------------------------------------------------------------
    // VS Code title parser: "filename • workspace — Visual Studio Code"
    // ---------------------------------------------------------------------------
    private static string ParseVSCodeTitle(string title, bool insiders = false)
    {
        // Format variants:
        //   "filename • workspace — Visual Studio Code"
        //   "workspace — Visual Studio Code"
        //   "Visual Studio Code"
        //   "filename • workspace — Visual Studio Code - Insiders"
        var label = insiders ? "VS Code Insiders" : "VS Code";
        var longSuffix = insiders ? " — Visual Studio Code - Insiders" : " — Visual Studio Code";
        var shortSuffix = insiders ? " - Visual Studio Code - Insiders" : " - Visual Studio Code";

        var working = title.EndsWith(longSuffix, StringComparison.OrdinalIgnoreCase)
            ? title[..^longSuffix.Length]
            : title.Replace(shortSuffix, "", StringComparison.OrdinalIgnoreCase).Trim();

        var parts = working.Split('•', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return $"Active IDE: {label} | Workspace: {parts[1]} | File: {parts[0]}";
        if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0]))
            return $"Active IDE: {label} | Workspace: {parts[0]}";
        return $"Active IDE: {label} - {title}";
    }

    // ---------------------------------------------------------------------------
    // Visual Studio title parser (devenv.exe)
    // Formats:
    //   "FileName - ProjectName - Microsoft Visual Studio"
    //   "FileName - ProjectName - Microsoft Visual Studio 2022"
    //   "ProjectName (Running) - Microsoft Visual Studio 2022"
    //   "ProjectName - Microsoft Visual Studio 2022 (Administrator)"
    //   "Microsoft Visual Studio"
    // ---------------------------------------------------------------------------
    private static string ParseVisualStudioTitle(string title)
    {
        // Extract optional year and admin flag from "... - Microsoft Visual Studio 2022 (Administrator)"
        // \d{4,} matches any 4+ digit year (2019, 2022, 2026, ...)
        var suffixMatch = Regex.Match(
            title,
            @"\s*-\s*Microsoft Visual Studio(?:\s+(\d{4,}))?(?:\s+\(Administrator\))?$",
            RegexOptions.IgnoreCase);

        var vsLabel = suffixMatch.Success && suffixMatch.Groups[1].Success
            ? $"Visual Studio {suffixMatch.Groups[1].Value}"
            : "Visual Studio";

        var working = suffixMatch.Success
            ? title[..suffixMatch.Index].Trim()
            : title.Replace("Microsoft Visual Studio", "", StringComparison.OrdinalIgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(working))
            return $"Active IDE: {vsLabel}";

        // Strip trailing run-state annotation: "ProjectName (Running)" → "ProjectName"
        var stateMatch = Regex.Match(working, @"^(.+?)\s*\((Running|Debugging|Building|Not Responding)\)\s*$", RegexOptions.IgnoreCase);
        if (stateMatch.Success)
        {
            var project = stateMatch.Groups[1].Value.Trim();
            var state = stateMatch.Groups[2].Value;
            return $"Active IDE: {vsLabel} | Project: {project} | State: {state}";
        }

        // "FileName - ProjectName" → split on last dash
        var dashIdx = working.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            var file = working[..dashIdx].Trim();
            var project = working[(dashIdx + 3)..].Trim();
            return $"Active IDE: {vsLabel} | Project: {project} | File: {file}";
        }

        return $"Active IDE: {vsLabel} | Project: {working}";
    }

    // ---------------------------------------------------------------------------
    // JetBrains Rider title parser (rider64.exe)
    // Formats:
    //   "ProjectName – JetBrains Rider"
    //   "FileName [ProjectName] – JetBrains Rider"
    // ---------------------------------------------------------------------------
    private static string ParseRiderTitle(string title)
    {
        // Em-dash separator used by JetBrains products
        var working = Regex.Replace(title, @"\s*[–—]\s*JetBrains Rider\s*", "", RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(working))
            return "Active IDE: JetBrains Rider";

        // "FileName [ProjectName]" format
        var bracketMatch = Regex.Match(working, @"^(.+?)\s*\[(.+?)\]\s*$");
        if (bracketMatch.Success)
            return $"Active IDE: JetBrains Rider | Project: {bracketMatch.Groups[2].Value.Trim()} | File: {bracketMatch.Groups[1].Value.Trim()}";

        return $"Active IDE: JetBrains Rider | Project: {working}";
    }

    // ---------------------------------------------------------------------------
    // Browser URL via UI Automation
    // ---------------------------------------------------------------------------
    private static string? GetBrowserUrlViaUia(IntPtr hWnd, string processName)
    {
        try
        {
            var rootElement = AutomationElement.FromHandle(hWnd);
            if (rootElement == null) return null;

            // Address bar automation IDs differ across browsers
            string[] addressBarIds = processName.ToLowerInvariant() switch
            {
                "msedge" => ["view_7", "addressEditBox"],
                "chrome" => ["omnibox"],
                "firefox" => ["urlbar-input"],
                _ => ["omnibox", "addressEditBox"]
            };

            foreach (var id in addressBarIds)
            {
                var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
                var element = rootElement.FindFirst(TreeScope.Descendants, condition);
                if (element != null &&
                    element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) &&
                    vp is ValuePattern vPattern)
                {
                    var url = vPattern.Current.Value;
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
        }
        catch { /* UIA can throw on protected windows */ }
        return null;
    }

    // ---------------------------------------------------------------------------
    // Clipboard text (Win32, stays on the background thread)
    // ---------------------------------------------------------------------------
    private static string? GetClipboardText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            return null;
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return null;
            try
            {
                var hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero) return null;
                var ptr = GlobalLock(hData);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    var text = Marshal.PtrToStringUni(ptr) ?? "";
                    // Only include short, non-binary clipboard content
                    if (text.Length > 500 || text.Length == 0)
                        return null;
                    return text.Trim();
                }
                finally { GlobalUnlock(hData); }
            }
            finally { CloseClipboard(); }
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------
    // Project type detection — File.Exists only, zero process spawning
    // ---------------------------------------------------------------------------
    private static string? SniffProjectType(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var detected = new List<string>();

        var checks = new (string file, string label)[]
        {
            ("package.json",       "Node.js"),
            ("tsconfig.json",      "TypeScript"),
            ("*.csproj",           "C#/.NET"),
            ("*.fsproj",           "F#/.NET"),
            ("*.vbproj",           "VB.NET"),
            ("Cargo.toml",         "Rust"),
            ("go.mod",             "Go"),
            ("pyproject.toml",     "Python (pyproject)"),
            ("requirements.txt",   "Python"),
            ("Gemfile",            "Ruby"),
            ("pom.xml",            "Java (Maven)"),
            ("build.gradle",       "Java (Gradle)"),
            ("CMakeLists.txt",     "C/C++ (CMake)"),
            ("Makefile",           "Make"),
            ("docker-compose.yml", "Docker Compose"),
            ("docker-compose.yaml","Docker Compose"),
            ("Dockerfile",         "Docker"),
            (".terraform",         "Terraform"),
        };

        foreach (var (file, label) in checks)
        {
            if (detected.Contains(label)) continue;
            bool found = file.Contains('*')
                ? Directory.EnumerateFiles(folder, file, SearchOption.TopDirectoryOnly).Any()
                : File.Exists(Path.Combine(folder, file)) || Directory.Exists(Path.Combine(folder, file));
            if (found)
                detected.Add(label);
        }

        return detected.Count > 0 ? string.Join(", ", detected) : null;
    }

    // ---------------------------------------------------------------------------
    // Git branch for a folder
    // ---------------------------------------------------------------------------
    private static string? GetGitBranch(string folder)
    {
        // Fast path: read .git/HEAD directly — no process spawn needed
        try
        {
            var headPath = Path.Combine(folder, ".git", "HEAD");
            if (!File.Exists(headPath))
            {
                // Walk up one level for subfolders of a repo root
                var parent = Directory.GetParent(folder)?.FullName;
                if (parent != null)
                    headPath = Path.Combine(parent, ".git", "HEAD");
                if (!File.Exists(headPath)) return null;
                folder = parent!;
            }
            var headContent = File.ReadAllText(headPath).Trim();
            string branch;
            if (headContent.StartsWith("ref: refs/heads/"))
                branch = headContent["ref: refs/heads/".Length..];
            else
                branch = headContent.Length >= 7 ? headContent[..7] : headContent; // detached HEAD

            var repoName = Path.GetFileName(folder.TrimEnd('\\', '/'));
            return $"{repoName} [branch: {branch}]";
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------
    // Is this a likely developer folder?
    // ---------------------------------------------------------------------------
    private static bool IsDevFolder(string folder) =>
        folder.Contains("\\dev\\", StringComparison.OrdinalIgnoreCase) ||
        folder.Contains("\\projects\\", StringComparison.OrdinalIgnoreCase) ||
        folder.Contains("\\source\\", StringComparison.OrdinalIgnoreCase) ||
        folder.Contains("\\repos\\", StringComparison.OrdinalIgnoreCase) ||
        folder.Contains("\\src\\", StringComparison.OrdinalIgnoreCase) ||
        folder.Contains("\\code\\", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------
    // Parse running WSL distros from `wsl --list --verbose` output
    // ---------------------------------------------------------------------------
    private static List<string> ParseRunningWslDistros(string wslInfo)
    {
        var running = new List<string>();
        if (string.IsNullOrEmpty(wslInfo)) return running;
        foreach (var line in wslInfo.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim().TrimStart('*').Trim();
            if (!trimmed.Contains("Running", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) running.Add(parts[0]);
        }
        return running;
    }

    // ---------------------------------------------------------------------------
    // Open windows deduplicated by process name
    // ---------------------------------------------------------------------------
    private List<string> GetOpenWindowsByProcess()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Skip noisy/system process names
        var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
            "TextInputHost", "SystemSettings", "ApplicationFrameHost", "Program Manager"
        };

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == currentPid) return true;
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var name = proc.ProcessName;
                if (skipNames.Contains(name)) return true;
                if (!seen.Add(name)) return true; // deduplicate by process name

                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    result.Add($"{name}: {title}");
            }
            catch { /* ignore inaccessible process */ }
            return true;
        }, IntPtr.Zero);

        return result;
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

