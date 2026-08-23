using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// The primary enforcement engine.
///
/// It cuts an application off from the internet by adding an outbound Windows Firewall
/// block rule scoped to that executable's full path, optionally restricted to specific
/// Windows user accounts via the -LocalUser SDDL filter.
///
/// Why this and not process termination:
///   * A firewall rule is a table entry. It cannot bugcheck Windows or trigger a reboot loop.
///   * Removing it requires administrator rights, so a standard user cannot bypass it.
///   * -LocalUser means the block applies to one Windows account and leaves other users alone.
///   * It works on Windows 10 Home, which has no AppLocker.
///   * It operates in a different layer than Netfree, so the two do not fight.
/// </summary>
public sealed class ApplicationNetworkBlocker
{
    private const string RulePrefix = "STG-App-";

    private readonly SafetyEnvelope _safety;
    private readonly ILogger<ApplicationNetworkBlocker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HashSet<string> _appliedSignatures = new(StringComparer.OrdinalIgnoreCase);
    private bool _everApplied;

    public ApplicationNetworkBlocker(SafetyEnvelope safety, ILogger<ApplicationNetworkBlocker> logger)
    {
        _safety = safety;
        _logger = logger;
    }

    public int ActiveRuleCount { get; private set; }

    public async Task ApplyAsync(
        IReadOnlyCollection<NetworkBlockTarget> targets,
        SafetySettings settings,
        bool enforcementAllowed,
        CancellationToken cancellationToken)
    {
        var desired = enforcementAllowed
            ? targets
                .Where(target => !ProtectedPaths.IsProtectedPath(target.ExecutablePath))
                .Where(target => FileStillExists(target.ExecutablePath))
                .GroupBy(target => target.Signature, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList()
            : new List<NetworkBlockTarget>();

        var signatures = desired.Select(target => target.Signature).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_everApplied && signatures.SetEquals(_appliedSignatures))
            {
                return;
            }

            if (!_safety.RegisterAction(settings, $"עדכון {desired.Count} חוקי חומת אש"))
            {
                return;
            }

            var script = BuildScript(desired);
            var result = await PowerShellRunner.RunAsync(script, TimeSpan.FromSeconds(90), _logger, cancellationToken);

            if (!result.Ok)
            {
                _logger.LogError("Firewall rule update failed: {Error}", result.Error);
                return;
            }

            _appliedSignatures = signatures;
            _everApplied = true;
            ActiveRuleCount = desired.Count;

            if (desired.Count == 0)
            {
                _logger.LogInformation("All application network blocks removed");
            }
            else
            {
                _logger.LogInformation(
                    "Applied {Count} application network blocks: {Apps}",
                    desired.Count,
                    string.Join(", ", desired.Select(target => target.DisplayName)));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes every rule this service owns. Used on clean shutdown and uninstall.</summary>
    public async Task RemoveAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PowerShellRunner.RunAsync(RemovalScript(), TimeSpan.FromSeconds(60), _logger, cancellationToken);
            _appliedSignatures.Clear();
            ActiveRuleCount = 0;
            _everApplied = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string RemovalScript() =>
        $"Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue";

    private static string BuildScript(IReadOnlyCollection<NetworkBlockTarget> targets)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");

        // Always start from a clean slate so removed rules disappear immediately.
        script.AppendLine(RemovalScript());

        foreach (var target in targets)
        {
            var ruleName = RulePrefix + StableId(target.Signature);
            var displayName = $"שומר זמן מסך: {Truncate(target.DisplayName, 60)}";

            script.Append("New-NetFirewallRule -Name ").Append(PowerShellRunner.Quote(ruleName));
            script.Append(" -DisplayName ").Append(PowerShellRunner.Quote(displayName));
            script.Append(" -Description ").Append(PowerShellRunner.Quote("נוצר אוטומטית. מחיקה ידנית תבוטל בסבב הבא."));
            script.Append(" -Direction Outbound -Action Block -Enabled True -Profile Any");
            script.Append(" -Program ").Append(PowerShellRunner.Quote(target.ExecutablePath));

            if (target.UserSids.Count > 0)
            {
                script.Append(" -LocalUser ").Append(PowerShellRunner.Quote(BuildSddl(target.UserSids)));
            }

            script.AppendLine(" | Out-Null");
        }

        return script.ToString();
    }

    /// <summary>Builds the SDDL that scopes a firewall rule to specific Windows accounts.</summary>
    private static string BuildSddl(IEnumerable<string> sids)
    {
        var builder = new StringBuilder("D:");
        foreach (var sid in sids.Where(ConfigurationValidation.IsValidUserSid))
        {
            builder.Append("(A;;CC;;;").Append(sid.Trim()).Append(')');
        }

        return builder.ToString();
    }

    private static bool FileStillExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("screen-time-guardian-app:" + value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..24];
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
