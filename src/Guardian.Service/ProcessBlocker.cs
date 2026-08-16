using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ScreenTimeGuardian.Service;

public sealed class ProcessBlocker
{
    private readonly ILogger<ProcessBlocker> _logger;

    public ProcessBlocker(ILogger<ProcessBlocker> logger)
    {
        _logger = logger;
    }

    public Task ApplyAsync(IReadOnlyCollection<string> executableNames, CancellationToken cancellationToken)
    {
        foreach (var executableName in executableNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processName = Path.GetFileNameWithoutExtension(executableName);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            foreach (var process in FindProcesses(processName))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            _logger.LogWarning(
                                "Closing scheduled blocked application {ProcessName} with PID {ProcessId}",
                                process.ProcessName,
                                process.Id);
                            process.CloseMainWindow();
                            if (!process.WaitForExit(1500) && !process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between enumeration and enforcement.
                    }
                    catch (System.ComponentModel.Win32Exception exception)
                    {
                        _logger.LogError(exception, "Could not close blocked application {ProcessName}", processName);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<Process> FindProcesses(string processName)
    {
        return Process.GetProcessesByName(processName);
    }
}
