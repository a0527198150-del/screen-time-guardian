using System.Diagnostics;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public sealed class RunningApp
{
    public string DisplayName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;

    public override string ToString() => $"{DisplayName}  —  {Path.GetFileName(ExecutablePath)}";
}

/// <summary>
/// Lists applications that currently have a visible window, so you can pick one
/// without hunting for its exe on disk. Read only: nothing here touches a process.
/// </summary>
public static class RunningApps
{
    public static IReadOnlyList<RunningApp> Discover()
    {
        var apps = new List<RunningApp>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId <= 0 || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        continue;
                    }

                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) || ProtectedPaths.IsProtectedPath(path))
                    {
                        continue;
                    }

                    var name = TryProductName(path) ?? process.ProcessName;
                    if (!apps.Any(app => string.Equals(app.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        apps.Add(new RunningApp { DisplayName = name, ExecutablePath = path });
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                      or System.ComponentModel.Win32Exception
                                                      or NotSupportedException
                                                      or UnauthorizedAccessException)
                {
                    // Another user's process or one that exited. Skip it.
                }
            }
        }

        return apps.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string? TryProductName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = info.FileDescription;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = info.ProductName;
            }

            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
