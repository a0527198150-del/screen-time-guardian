using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Catches the case that launch blocking cannot: a browser that has been renamed.
///
/// IFEO matches on file name, so renaming firefox.exe to homework.exe defeats it.
/// This scanner walks the folders a standard user can actually write to, reads each
/// executable's version metadata, and reports anything that identifies as a browser
/// but is not on the approved list. The result becomes a firewall block: the program
/// still launches, it simply has no internet.
///
/// Cost control: results are cached by path, size and write time, the walk is depth
/// limited, and it runs on its own interval rather than on the policy loop.
/// </summary>
public sealed class HiddenBrowserScanner
{
    private const int MaximumDepth = 4;
    private const int MaximumFilesPerScan = 8000;

    private readonly ILogger<HiddenBrowserScanner> _logger;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private List<NetworkBlockTarget> _lastResult = new();

    public HiddenBrowserScanner(ILogger<HiddenBrowserScanner> logger)
    {
        _logger = logger;
    }

    public int LastFoundCount => _lastResult.Count;

    private sealed record CacheEntry(long Length, DateTime WriteUtc, string? BrowserName);

    public IReadOnlyList<NetworkBlockTarget> Scan(BrowserLockdownSettings settings, bool enforcementAllowed)
    {
        if (!enforcementAllowed || !settings.ScanForHiddenBrowsers)
        {
            _lastResult = new List<NetworkBlockTarget>();
            return _lastResult;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(settings.ScanIntervalMinutes, 1, 360));
        if (DateTimeOffset.UtcNow - _lastScan < interval)
        {
            return _lastResult;
        }

        _lastScan = DateTimeOffset.UtcNow;

        var approved = settings.ApprovedBrowserPaths
            .Concat(BrowserIdentification.DefaultApprovedPaths())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = new List<NetworkBlockTarget>();
        var budget = MaximumFilesPerScan;

        foreach (var folder in EnumerateScanRoots(settings))
        {
            if (budget <= 0)
            {
                break;
            }

            ScanFolder(folder, 0, approved, found, ref budget);
        }

        if (found.Count > 0)
        {
            _logger.LogWarning(
                "Hidden browser scan found {Count} unapproved browsers: {Names}",
                found.Count,
                string.Join(", ", found.Select(item => item.DisplayName).Take(5)));
        }

        _lastResult = found;
        return _lastResult;
    }

    private static IEnumerable<string> EnumerateScanRoots(BrowserLockdownSettings settings)
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        // Every user profile: this is a machine wide service, and a brother's Downloads
        // folder is just as good a hiding place as your own.
        var usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users");
        if (Directory.Exists(usersRoot))
        {
            foreach (var profile in SafeEnumerateDirectories(usersRoot))
            {
                foreach (var leaf in new[] { "Downloads", "Desktop", "Documents", @"AppData\Local", @"AppData\Roaming" })
                {
                    var candidate = Path.Combine(profile, leaf);
                    if (Directory.Exists(candidate))
                    {
                        roots.Add(candidate);
                    }
                }
            }
        }

        roots.AddRange(settings.ExtraScanFolders.Where(Directory.Exists));

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void ScanFolder(
        string folder,
        int depth,
        IReadOnlySet<string> approved,
        List<NetworkBlockTarget> found,
        ref int budget)
    {
        if (depth > MaximumDepth || budget <= 0)
        {
            return;
        }

        foreach (var file in SafeEnumerateFiles(folder))
        {
            if (--budget <= 0)
            {
                return;
            }

            if (approved.Contains(file) || ProtectedPaths.IsProtectedPath(file))
            {
                continue;
            }

            var name = IdentifyCached(file);
            if (name is null)
            {
                continue;
            }

            found.Add(new NetworkBlockTarget
            {
                ExecutablePath = file,
                DisplayName = $"דפדפן לא מאושר: {name}",
                UserSids = new List<string>()
            });
        }

        foreach (var child in SafeEnumerateDirectories(folder))
        {
            if (budget <= 0)
            {
                return;
            }

            ScanFolder(child, depth + 1, approved, found, ref budget);
        }
    }

    private string? IdentifyCached(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (_cache.TryGetValue(file, out var cached)
                && cached.Length == info.Length
                && cached.WriteUtc == info.LastWriteTimeUtc)
            {
                return cached.BrowserName;
            }

            var name = BrowserIdentification.Identify(file);
            _cache[file] = new CacheEntry(info.Length, info.LastWriteTimeUtc, name);
            return name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
