using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Toggles the private-browsing escape hatches in step with the schedule.
///
/// Incognito and guest mode both hide which Google account is signed in, which is the
/// one thing the extension needs in order to decide anything. So while a block is
/// running they have to be off - otherwise every account rule is one keystroke away
/// from irrelevant.
///
/// But there is no reason to keep them off the rest of the time. When nothing is
/// blocked, both go back to normal. The restriction is only as wide as the schedule.
///
/// Chrome and Edge watch their policy keys in the registry and reload within a minute
/// or two of a change. Windows that are ALREADY OPEN are not closed by a policy change,
/// so an incognito window opened before the block began survives until it is closed.
/// </summary>
public sealed class BrowserPolicySynchronizer
{
    private const string ChromePolicyPath = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";

    // IncognitoModeAvailability: 0 = available, 1 = disabled, 2 = forced.
    private const int IncognitoAvailable = 0;
    private const int IncognitoDisabled = 1;

    private readonly ILogger<BrowserPolicySynchronizer> _logger;
    private bool? _lastPrivateBrowsingAllowed;

    public BrowserPolicySynchronizer(ILogger<BrowserPolicySynchronizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called on every policy cycle. <paramref name="anyBlockActive"/> is true whenever
    /// any rule is currently in force, whatever kind it is.
    /// </summary>
    public void ApplyPrivateBrowsingPolicy(bool anyBlockActive, bool enforcementAllowed)
    {
        // When enforcement is off entirely (safe mode, grace period), leave the browsers
        // fully open. A disabled service must not hold restrictions in place.
        var allowPrivateBrowsing = !enforcementAllowed || !anyBlockActive;

        if (_lastPrivateBrowsingAllowed == allowPrivateBrowsing)
        {
            return;
        }

        try
        {
            foreach (var path in new[] { ChromePolicyPath, EdgePolicyPath })
            {
                SetDword(path, "IncognitoModeAvailability",
                    allowPrivateBrowsing ? IncognitoAvailable : IncognitoDisabled);
                SetDword(path, "BrowserGuestModeEnabled",
                    allowPrivateBrowsing ? 1 : 0);
            }

            _lastPrivateBrowsingAllowed = allowPrivateBrowsing;

            _logger.LogInformation(
                allowPrivateBrowsing
                    ? "No block is active: incognito and guest mode re-enabled"
                    : "A block is active: incognito and guest mode disabled");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or System.Security.SecurityException
                                              or InvalidOperationException)
        {
            _logger.LogError(exception, "Could not update browser private browsing policy");
        }
    }

    /// <summary>Restores both browsers to their normal state. Used on clean shutdown.</summary>
    public void RestoreDefaults()
    {
        try
        {
            foreach (var path in new[] { ChromePolicyPath, EdgePolicyPath })
            {
                SetDword(path, "IncognitoModeAvailability", IncognitoAvailable);
                SetDword(path, "BrowserGuestModeEnabled", 1);
            }

            _lastPrivateBrowsingAllowed = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or System.Security.SecurityException
                                              or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Could not restore browser policy defaults");
        }
    }

    private static void SetDword(string path, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry policy path {path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
