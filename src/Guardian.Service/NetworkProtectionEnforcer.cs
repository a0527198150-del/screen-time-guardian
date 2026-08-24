using System.Security;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Keeps the Windows Defender capability required by dynamic FQDN firewall rules enabled.
/// This is deliberately a policy write, not a shell command, and it only runs while an
/// administrator has explicitly enabled machine-wide website enforcement.
/// </summary>
public sealed class NetworkProtectionEnforcer
{
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows Defender\Policy Manager";
    private const string PolicyValue = "EnableNetworkProtection";

    private readonly ILogger<NetworkProtectionEnforcer> _logger;

    public NetworkProtectionEnforcer(ILogger<NetworkProtectionEnforcer> logger)
    {
        _logger = logger;
    }

    public bool EnsureEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyPath, writable: true);
            if (key is null)
            {
                _logger.LogWarning("Could not open the Defender Network Protection policy key");
                return false;
            }

            var current = key.GetValue(PolicyValue);
            if (current is int value && value == 1)
            {
                return true;
            }

            key.SetValue(PolicyValue, 1, RegistryValueKind.DWord);
            _logger.LogWarning(
                "Defender Network Protection was not enabled; restored the machine policy to Block mode");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            _logger.LogError(
                exception,
                "Could not enable Defender Network Protection; machine-wide website enforcement will stay off");
            return false;
        }
    }
}
