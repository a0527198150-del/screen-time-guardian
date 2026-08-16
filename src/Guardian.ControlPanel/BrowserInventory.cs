using System.IO;
using Microsoft.Win32;

namespace ScreenTimeGuardian.ControlPanel;

public sealed class BrowserInventoryItem
{
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
}

public static class BrowserInventory
{
    private static readonly string[] BrowserNames = { "Google Chrome", "Microsoft Edge" };

    public static IReadOnlyList<BrowserInventoryItem> Discover()
    {
        var result = new List<BrowserInventoryItem>();
        var roots = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        };

        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string ?? string.Empty;
                var browserName = BrowserNames.FirstOrDefault(name =>
                    displayName.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (browserName is null)
                {
                    continue;
                }

                var location = subKey?.GetValue("InstallLocation") as string ?? string.Empty;
                var executableName = browserName.Contains("Edge", StringComparison.OrdinalIgnoreCase)
                    ? "msedge.exe"
                    : "chrome.exe";
                var executablePath = ResolveExecutable(location, executableName);
                if (executablePath is null)
                {
                    continue;
                }

                var item = new BrowserInventoryItem
                {
                    DisplayName = displayName,
                    Publisher = browserName.Contains("Edge", StringComparison.OrdinalIgnoreCase)
                        ? "Microsoft"
                        : "Google",
                    ProductName = browserName,
                    ExecutablePath = executablePath
                };

                if (!result.Any(existing => string.Equals(
                        existing.ExecutablePath,
                        item.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private static string? ResolveExecutable(string location, string executableName)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var direct = Path.Combine(location, executableName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var parent = Directory.GetParent(location)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var parentCandidate = Path.Combine(parent, executableName);
            if (File.Exists(parentCandidate))
            {
                return parentCandidate;
            }
        }

        return null;
    }
}
