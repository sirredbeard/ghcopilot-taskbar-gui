using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CopilotTaskbarApp;

public class CopilotCliDetector
{
    public static async Task<CopilotCliStatus> CheckCopilotCliAsync()
    {
        try
        {
            // Try to run copilot --version
            var psi = new ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return CopilotCliStatus.NotInstalled;
            }

            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                var version = await process.StandardOutput.ReadToEndAsync();
                return new CopilotCliStatus 
                { 
                    IsInstalled = true, 
                    Version = version.Trim() 
                };
            }

            return CopilotCliStatus.NotInstalled;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Command not found in PATH
            return CopilotCliStatus.NotInstalled;
        }
        catch (Exception ex)
        {
            return new CopilotCliStatus 
            { 
                IsInstalled = false, 
                Error = ex.Message 
            };
        }
    }

    public static async Task<bool> InstallCopilotCliAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install --id GitHub.Copilot --accept-source-agreements --accept-package-agreements",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

public class CopilotCliStatus
{
    public bool IsInstalled { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }

    public static CopilotCliStatus NotInstalled => new() { IsInstalled = false };
}
