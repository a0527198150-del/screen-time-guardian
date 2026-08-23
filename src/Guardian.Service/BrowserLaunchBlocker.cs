using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Denies launch of unapproved browsers by name, using Image File Execution Options.
///
/// When an IFEO key for "firefox.exe" has a Debugger value, Windows launches that
/// debugger instead of the program. We point it at a tiny stub that shows a Hebrew
/// message and exits, so the browser simply never starts.
///
/// Why IFEO here:
///   * works on Windows 10 Home, which has no AppLocker;
///   * applies to every user account on the machine;
///   * keyed by file NAME, so a freshly installed or portable copy is caught too;
///   * requires administrator rights to remove.
///
/// Safety rules enforced below, without exception:
///   * only names that pass BrowserIdentification.CanDenyByName are ever written;
///   * every key this class writes is tagged, and it only ever removes its own tags;
///   * if the stub is missing from disk, NOTHING is written at all - a dangling
///     Debugger value would make an application permanently unlaunchable.
/// </summary>
public sealed class BrowserLaunchBlocker
{
    private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string OwnerMarker = "ScreenTimeGuardian";
    private const string OwnerValueName = "STGOwned";

    private readonly SafetyEnvelope _safety;
    private readonly ILogger<BrowserLaunchBlocker> _logger;

    private HashSet<string> _applied = new(StringComparer.OrdinalIgnoreCase);
    private bool _everApplied;

    public BrowserLaunchBlocker(SafetyEnvelope safety, ILogger<BrowserLaunchBlocker> logger)
    {
        _safety = safety;
        _logger = logger;
    }

    public int ActiveDenyCount { get; private set; }

    public static string StubPath => Path.Combine(
        AppContext.BaseDirectory,
        "ScreenTimeGuardian.LaunchBlocker.exe");

    public void Apply(BrowserLockdownSettings settings, SafetySettings safety, bool enforcementAllowed)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (enforcementAllowed && settings.BlockUnapprovedBrowserLaunch)
        {
            if (!File.Exists(StubPath))
            {
                _logger.LogError(
                    "Launch blocking is enabled but the stub is missing at {Path}. " +
                    "Refusing to write any IFEO entries - a dangling Debugger value would make applications unlaunchable.",
                    StubPath);
                return;
            }

            var approvedNames = settings.ApprovedBrowserPaths
                .Select(path => Path.GetFileName(path))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var stem in BrowserIdentification.KnownBrowserExecutables
                         .Concat(settings.ExtraBlockedBrowserNames))
            {
                var fileName = Path.GetFileNameWithoutExtension(stem) + ".exe";

                if (!BrowserIdentification.CanDenyByName(fileName) || approvedNames.Contains(fileName))
                {
                    continue;
                }

                desired.Add(fileName);
            }
        }

        if (_everApplied && desired.SetEquals(_applied))
        {
            return;
        }

        if (!_safety.RegisterAction(safety, $"עדכון {desired.Count} חסימות הפעלה של דפדפנים"))
        {
            return;
        }

        try
        {
            RemoveOwnedEntries(except: desired);

            foreach (var fileName in desired)
            {
                WriteEntry(fileName);
            }

            _applied = desired;
            _everApplied = true;
            ActiveDenyCount = desired.Count;

            _logger.LogInformation("Launch blocking active for {Count} browser executables", desired.Count);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogError(exception, "Could not update IFEO entries; administrator rights are required");
        }
    }

    /// <summary>Removes every entry this class owns. Called on clean shutdown and by the uninstaller.</summary>
    public void RemoveAll()
    {
        try
        {
            RemoveOwnedEntries(except: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _applied.Clear();
            ActiveDenyCount = 0;
            _everApplied = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogError(exception, "Could not clear IFEO entries");
        }
    }

    private static void WriteEntry(string fileName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey($@"{IfeoRoot}\{fileName}", writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue("Debugger", $"\"{StubPath}\"", RegistryValueKind.String);
        key.SetValue(OwnerValueName, OwnerMarker, RegistryValueKind.String);
    }

    /// <summary>
    /// Only deletes keys carrying our marker. IFEO is shared with debuggers and other
    /// tools; deleting someone else's entry would break their software.
    /// </summary>
    private void RemoveOwnedEntries(HashSet<string> except)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var root = baseKey.OpenSubKey(IfeoRoot, writable: true);
        if (root is null)
        {
            return;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            if (except.Contains(name))
            {
                continue;
            }

            bool owned;
            using (var child = root.OpenSubKey(name))
            {
                owned = child?.GetValue(OwnerValueName) as string == OwnerMarker;
            }

            if (!owned)
            {
                continue;
            }

            try
            {
                root.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(exception, "Could not remove IFEO entry {Name}", name);
            }
        }
    }
}
