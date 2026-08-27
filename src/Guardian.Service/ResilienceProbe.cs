using Microsoft.Win32;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Microsecond-scale probes that answer one question: do the enforcement artifacts
/// still exist on disk/registry the way the last full cycle left them?
///
/// Reading a value from the registry is microseconds; it never launches a process.
/// The probe cannot CREATE anything - when it reports drift, the caller performs a
/// full enforcement cycle and repairs reality.
///
/// Firewall rules written by ApplicationNetworkBlocker are stored as values under
/// HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\
/// FirewallRules with names prefixed STG-App- / STG-Website- (the same prefixes
/// every other Guardian component uses).
/// </summary>
public static class ResilienceProbe
{
    private const string FirewallRulesKeyPath =
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    /// <summary>
    /// Count of live firewall rules carrying a Guardian prefix. Returns null when the
    /// key cannot be read at all (which itself counts as drift upstream).
    /// </summary>
    public static int? CountGuardianFirewallRules()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(FirewallRulesKeyPath);
            if (key is null)
            {
                return null;
            }

            var count = 0;
            foreach (var name in key.GetValueNames())
            {
                if (name.StartsWith("STG-App-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("STG-Website-", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }
}
