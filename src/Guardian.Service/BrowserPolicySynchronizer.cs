using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class BrowserPolicySynchronizer
{
    private const string ChromePolicyPath = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";
    private const int IncognitoAvailable = 0;
    private const int IncognitoDisabled = 1;

    private readonly ILogger<BrowserPolicySynchronizer> _logger;
    private bool? _lastPrivateBrowsingAllowed;
    private HashSet<string>? _lastBlockedPatterns;

    public BrowserPolicySynchronizer(ILogger<BrowserPolicySynchronizer> logger)
    {
        _logger = logger;
    }

    public void ApplyPrivateBrowsingPolicy(bool anyBlockActive, bool enforcementAllowed, bool enforceForAdministrators)
    {
        var allowPrivateBrowsing = !enforcementAllowed || !enforceForAdministrators || !anyBlockActive;
        if (_lastPrivateBrowsingAllowed == allowPrivateBrowsing) return;

        try
        {
            foreach (var path in PolicyPaths)
            {
                SetDword(path, "IncognitoModeAvailability", allowPrivateBrowsing ? IncognitoAvailable : IncognitoDisabled);
                SetDword(path, "BrowserGuestModeEnabled", allowPrivateBrowsing ? 1 : 0);
            }
            _lastPrivateBrowsingAllowed = allowPrivateBrowsing;
            _logger.LogInformation(allowPrivateBrowsing
                ? "No block is active: incognito and guest mode re-enabled"
                : "A block is active: incognito and guest mode disabled");
        }
        catch (Exception exception) when (IsRegistryWriteFailure(exception))
        {
            _logger.LogError(exception, "Could not update browser private browsing policy");
        }
    }

    /// <summary>Synchronizes the machine-wide URLBlocklist policy for Chrome and Edge.</summary>
    public void ApplyUrlBlocklist(IReadOnlyCollection<string> domains, bool enabled)
    {
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (enabled)
        {
            foreach (var domain in domains)
            {
                var pattern = PolicyEngine.NormalizeDomain(domain);
                if (ConfigurationValidation.IsValidDomain(pattern)) desired.Add(pattern);
            }
        }

        if (_lastBlockedPatterns is not null && _lastBlockedPatterns.SetEquals(desired)) return;

        try
        {
            foreach (var path in PolicyPaths) WriteBlocklist(path, desired);
            _lastBlockedPatterns = desired;
            _logger.LogInformation("Browser URL blocklist updated: {Count} patterns (enabled={Enabled})", desired.Count, enabled);
        }
        catch (Exception exception) when (IsRegistryWriteFailure(exception))
        {
            // Do not cache failures as success; the next policy cycle retries.
            _lastBlockedPatterns = null;
            _logger.LogError(exception, "Could not update the browser URL blocklist policy");
        }
    }

    public void RestoreDefaults()
    {
        try
        {
            foreach (var path in PolicyPaths)
            {
                SetDword(path, "IncognitoModeAvailability", IncognitoAvailable);
                SetDword(path, "BrowserGuestModeEnabled", 1);
                WriteBlocklist(path, Array.Empty<string>());
            }
            _lastPrivateBrowsingAllowed = true;
            _lastBlockedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsRegistryWriteFailure(exception))
        {
            _lastPrivateBrowsingAllowed = null;
            _lastBlockedPatterns = null;
            _logger.LogWarning(exception, "Could not restore browser policy defaults");
        }
    }

    private static IReadOnlyList<string> PolicyPaths => new[] { ChromePolicyPath, EdgePolicyPath };

    private static void WriteBlocklist(string policyPath, IReadOnlyCollection<string> patterns)
    {
        using var policyKey = Registry.LocalMachine.CreateSubKey(policyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry policy path {policyPath}");
        policyKey.DeleteSubKeyTree("URLBlocklist", throwOnMissingSubKey: false);
        if (patterns.Count == 0) return;

        using var listKey = policyKey.CreateSubKey("URLBlocklist", writable: true)
            ?? throw new InvalidOperationException($"Could not open {policyPath}\\URLBlocklist");
        var index = 1;
        foreach (var pattern in patterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            listKey.SetValue(index.ToString(CultureInfo.InvariantCulture), pattern, RegistryValueKind.String);
            index++;
        }
    }

    private static void SetDword(string path, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry policy path {path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static bool IsRegistryWriteFailure(Exception exception) => exception is
        UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException;
}
