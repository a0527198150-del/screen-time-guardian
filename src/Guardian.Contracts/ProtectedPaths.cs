namespace ScreenTimeGuardian.Contracts;

/// <summary>
/// A hard, non-configurable deny list. Nothing here may ever be blocked or closed,
/// no matter what the configuration says. This is the guard rail that stops a bad
/// rule from taking the machine down or breaking the network filter.
/// </summary>
public static class ProtectedPaths
{
    private static readonly string[] ProtectedDirectoryFragments =
    {
        @"\windows\",
        @"\program files\windows defender\",
        @"\program files (x86)\windows defender\",
        @"\programdata\microsoft\windows defender\",
        // Netfree / Netspark content filter components must never be touched.
        @"\netfree\",
        @"\net free\",
        @"\netspark\",
        @"\programdata\netfree\",
        @"\programdata\netspark\",
        @"\program files\netfree\",
        @"\program files (x86)\netfree\",
        @"\program files\netspark\",
        @"\program files (x86)\netspark\"
    };

    private static readonly string[] ProtectedProcessNames =
    {
        "system", "registry", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "lsaiso", "svchost", "dwm", "explorer", "fontdrvhost", "sihost",
        "logonui", "userinit", "ctfmon", "runtimebroker", "dllhost", "conhost",
        "taskhostw", "searchhost", "searchindexer", "shellexperiencehost",
        "startmenuexperiencehost", "textinputhost", "wudfhost", "spoolsv",
        "audiodg", "msmpeng", "nissrv", "securityhealthservice",
        "securityhealthsystray", "mpdefendercoreservice", "wmiprvse", "taskeng",
        "sppsvc", "trustedinstaller", "tiworker", "wuauclt", "consent",
        "applicationframehost", "systemsettings", "lockapp", "widgets",
        // Netfree / Netspark user mode components.
        "netfree", "netfreeservice", "netfreefilter", "nfservice", "nfagent",
        "netspark", "netsparkservice", "sparkservice"
    };

    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = path.Replace('/', '\\').ToLowerInvariant();
        if (!normalized.StartsWith(@"\", StringComparison.Ordinal) && !normalized.Contains(":\\", StringComparison.Ordinal))
        {
            // Not a full path. Refuse: name-only matching is what broke the machine before.
            return true;
        }

        if (ProtectedDirectoryFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
        {
            return true;
        }

        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return ProtectedProcessNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsProtectedProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        return ProtectedProcessNames.Contains(
            Path.GetFileNameWithoutExtension(processName),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? DescribeRejection(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "לא נבחר קובץ.";
        }

        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "יש לבחור קובץ הפעלה מסוג exe.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "יש לבחור נתיב מלא לקובץ, לא שם קובץ בלבד.";
        }

        if (IsProtectedPath(path))
        {
            return "הקובץ הזה שייך ל־Windows, ל־Windows Defender או לתוכנת הסינון, ולכן לא ניתן לחסום אותו.";
        }

        return null;
    }
}
