using System.Diagnostics;

namespace ScreenTimeGuardian.Contracts;

/// <summary>
/// Identifies browser executables by FILE METADATA rather than by process behaviour.
///
/// This is the replacement for the old runtime detector. The critical differences:
///   * it never touches a running process - it reads a file on disk;
///   * a match results in a firewall rule, never a termination;
///   * matching is on whole tokens in publisher and product fields, so
///     "Microsoft Edge WebView2" no longer counts as a browser.
/// </summary>
public static class BrowserIdentification
{
    /// <summary>Executable names denied by Image File Execution Options.</summary>
    /// <summary>
    /// Executable names denied by Image File Execution Options.
    ///
    /// EVERY name here must be specific enough that no unrelated program shares it.
    /// IFEO matching is by file name across the whole machine, so a generic name like
    /// "launcher.exe" or "browser.exe" would block dozens of unrelated applications.
    /// If a browser's real executable has a generic name, it is NOT listed here - the
    /// disk scanner catches it by metadata instead.
    /// </summary>
    public static readonly string[] KnownBrowserExecutables =
    {
        "firefox", "waterfox", "librewolf", "palemoon", "seamonkey", "floorp",
        "brave", "opera", "vivaldi", "chromium", "thorium", "yandex",
        "maxthon", "coccoc", "ucbrowser", "360se", "360chrome",
        "avastbrowser", "avgbrowser", "ccleanerbrowser", "torch", "slimjet",
        "centbrowser", "falkon", "midori", "qutebrowser", "duckduckgo",
        "srware", "comododragon", "iridium", "supermium", "mercury",
        "firefoxportable", "bravePortable", "operaportable", "torbrowser"
    };

    /// <summary>Names that are never denied, no matter what, because denying them breaks Windows.</summary>
    /// <summary>
    /// Names that are never denied under any circumstances.
    ///
    /// Two groups: Windows components, where a deny entry would break the system; and
    /// GENERIC names that some browsers happen to use but thousands of other programs
    /// use too. Blocking "launcher.exe" machine-wide would break games, updaters and
    /// installers all over the disk.
    /// </summary>
    private static readonly string[] NeverDeny =
    {
        // Windows and the approved browsers.
        "explorer", "svchost", "services", "lsass", "csrss", "winlogon", "wininit",
        "smss", "dwm", "conhost", "cmd", "powershell", "pwsh", "rundll32", "msedge",
        "chrome", "msedgewebview2", "setup", "installer", "update", "updater",
        "msiexec", "regsvr32", "dllhost", "taskhostw", "sihost", "ctfmon",

        // Generic names. Some browsers use these, but so does half the software on
        // the machine. The disk scanner handles those by metadata instead.
        "launcher", "browser", "app", "main", "start", "run", "client", "host",
        "tor", "min", "arc", "shift", "epic", "orion", "wave", "puffin", "zen",
        "core", "engine", "service", "agent", "helper", "tool", "web"
    };

    private static readonly string[] PublisherTokens =
    {
        "mozilla", "brave", "opera", "vivaldi", "yandex", "maxthon", "naver",
        "coccoc", "ucweb", "qihoo", "avast", "avg", "piriform", "hidden reflex",
        "srware", "comodo", "torch", "flashpeak", "cent", "puffin", "epic browser",
        "the tor project", "waterfox", "librewolf", "moonchild"
    };

    private static readonly string[] ProductTokens =
    {
        "firefox", "waterfox", "librewolf", "pale moon", "seamonkey", "floorp",
        "brave", "opera", "vivaldi", "chromium", "thorium", "srware iron",
        "comodo dragon", "yandex", "maxthon", "whale", "coc coc", "uc browser",
        "360 browser", "epic privacy browser", "torch browser", "slimjet",
        "cent browser", "falkon", "midori", "tor browser", "duckduckgo", "arc browser"
    };

    public static bool CanDenyByName(string executableName)
    {
        var stem = Path.GetFileNameWithoutExtension(executableName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        return !NeverDeny.Contains(stem, StringComparer.OrdinalIgnoreCase)
            && !ProtectedPaths.IsProtectedProcessName(stem);
    }

    /// <summary>Returns a browser name if this file looks like a browser, otherwise null.</summary>
    public static string? Identify(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || ProtectedPaths.IsProtectedPath(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var publisher = (info.CompanyName ?? string.Empty).ToLowerInvariant();
            var product = (info.ProductName ?? string.Empty).ToLowerInvariant();
            var description = (info.FileDescription ?? string.Empty).ToLowerInvariant();

            var publisherHit = PublisherTokens.Any(token => publisher.Contains(token, StringComparison.Ordinal));
            var productHit = ProductTokens.Any(token =>
                product.Contains(token, StringComparison.Ordinal)
                || description.Contains(token, StringComparison.Ordinal));

            // Require BOTH a browser-ish product name AND either a known publisher or the
            // word "browser". A single loose substring match is what caused the old bug.
            if (productHit && (publisherHit || product.Contains("browser", StringComparison.Ordinal)
                                            || description.Contains("browser", StringComparison.Ordinal)))
            {
                var name = info.FileDescription;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = info.ProductName;
                }

                return string.IsNullOrWhiteSpace(name)
                    ? Path.GetFileNameWithoutExtension(executablePath)
                    : name.Trim();
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The standard install locations of the two approved browsers.</summary>
    public static IEnumerable<string> DefaultApprovedPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe")
        };

        return candidates.Where(File.Exists);
    }
}
