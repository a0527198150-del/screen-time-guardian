using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ScreenTimeGuardian.Service;

public sealed record PowerShellResult(bool Ok, string Output, string Error);

/// <summary>
/// Runs a PowerShell script with a hard timeout. The old code could block the policy
/// loop indefinitely if PowerShell hung; here the process is killed after the timeout.
/// </summary>
public static class PowerShellRunner
{
    public static async Task<PowerShellResult> RunAsync(
        string script,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        if (!process.Start())
        {
            return new PowerShellResult(false, string.Empty, "לא ניתן להפעיל PowerShell.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("PowerShell exceeded its {Timeout} timeout and was terminated", timeout);
            TryKill(process, logger);
            return new PowerShellResult(false, string.Empty, "פקודת PowerShell חרגה מזמן ההמתנה ונעצרה.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process, logger);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        return new PowerShellResult(process.ExitCode == 0, output, error);
    }

    private static void TryKill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "Could not terminate the PowerShell process");
        }
    }

    /// <summary>Escapes a value for use inside a PowerShell single quoted string.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
