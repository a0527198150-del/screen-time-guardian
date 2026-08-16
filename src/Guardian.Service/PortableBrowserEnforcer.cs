using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class PortableBrowserEnforcer
{
    private readonly BrowserProcessDetector _detector;
    private readonly ILogger<PortableBrowserEnforcer> _logger;

    public PortableBrowserEnforcer(
        BrowserProcessDetector detector,
        ILogger<PortableBrowserEnforcer> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    public Task ApplyAsync(
        bool blockPortableBrowsers,
        IReadOnlyCollection<BrowserApproval> approvals,
        bool strictMode,
        CancellationToken cancellationToken)
    {
        if (!blockPortableBrowsers)
        {
            return Task.CompletedTask;
        }

        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                if (process.Id == currentProcessId)
                {
                    continue;
                }

                var descriptor = _detector.Describe(process);
                if (descriptor is null)
                {
                    continue;
                }

                var isBrowser = _detector.LooksLikeBrowser(descriptor);
                var isApproved = isBrowser && _detector.IsApproved(descriptor, approvals);
                var isPortableLocation = IsUserWritableLocation(descriptor.ExecutablePath);
                var shouldBlock = !isApproved && (isBrowser || (strictMode && isPortableLocation));

                if (!shouldBlock)
                {
                    continue;
                }

                try
                {
                    _logger.LogWarning(
                        "Blocking unapproved {Kind} process {ProcessName} from {Path}",
                        isBrowser ? "browser" : "portable application",
                        descriptor.ProcessName,
                        descriptor.ExecutablePath);
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(500) && !process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited during inspection.
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    _logger.LogError(exception, "Could not block process {ProcessId}", descriptor.ProcessId);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsUserWritableLocation(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Desktop\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\AppData\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\ProgramData\\", StringComparison.OrdinalIgnoreCase);
    }
}
