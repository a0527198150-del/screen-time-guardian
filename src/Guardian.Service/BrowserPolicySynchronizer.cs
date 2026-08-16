using Microsoft.Win32;

namespace ScreenTimeGuardian.Service;

public sealed class BrowserPolicySynchronizer
{
    private const string ChromePolicyPath = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";
    private bool? _lastGuestAllowed;

    public bool ApplyGuestModePolicy(bool guestAllowed)
    {
        if (_lastGuestAllowed == guestAllowed)
        {
            return false;
        }

        SetDword(Registry.LocalMachine, ChromePolicyPath, "BrowserGuestModeEnabled", guestAllowed ? 1 : 0);
        SetDword(Registry.LocalMachine, EdgePolicyPath, "BrowserGuestModeEnabled", guestAllowed ? 1 : 0);
        _lastGuestAllowed = guestAllowed;
        return true;
    }

    private static void SetDword(RegistryKey root, string path, string name, int value)
    {
        using var key = root.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry policy path {path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
