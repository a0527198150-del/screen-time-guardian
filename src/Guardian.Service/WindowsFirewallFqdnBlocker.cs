using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Machine wide website blocking through Windows Firewall dynamic FQDN keywords.
/// DNS configuration is never touched, so Netfree keeps working unchanged.
///
/// Note: this blocks a domain for EVERY user on the machine. Account specific blocking
/// (for example "my Gmail but not my brothers'") is handled by the browser extension,
/// which is the only component that knows which Google account is signed in.
/// </summary>
public sealed class WindowsFirewallFqdnBlocker
{
    private const string RulePrefix = "STG-Website-";

    private readonly SafetyEnvelope _safety;
    private readonly ILogger<WindowsFirewallFqdnBlocker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HashSet<string> _lastDomains = new(StringComparer.OrdinalIgnoreCase);
    private WebsiteEnforcementMode _lastMode = WebsiteEnforcementMode.Disabled;
    private bool _everApplied;

    public WindowsFirewallFqdnBlocker(SafetyEnvelope safety, ILogger<WindowsFirewallFqdnBlocker> logger)
    {
        _safety = safety;
        _logger = logger;
    }

    public async Task ApplyAsync(
        WebsiteEnforcementMode mode,
        IReadOnlyCollection<string> domains,
        SafetySettings settings,
        CancellationToken cancellationToken)
    {
        var normalized = domains
            .Select(PolicyEngine.NormalizeDomain)
            .Where(ConfigurationValidation.IsValidDomain)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_everApplied && mode == _lastMode && normalized.SetEquals(_lastDomains))
            {
                return;
            }

            if (!_safety.RegisterAction(settings, $"עדכון {normalized.Count} חוקי חסימת אתרים"))
            {
                return;
            }

            var script = mode == WebsiteEnforcementMode.Enforced && normalized.Count > 0
                ? BuildApplyScript(normalized)
                : RemovalScript();

            var result = await PowerShellRunner.RunAsync(script, TimeSpan.FromSeconds(90), _logger, cancellationToken);
            if (!result.Ok)
            {
                _logger.LogError("Website firewall policy failed: {Error}", result.Error);
                return;
            }

            _lastMode = mode;
            _lastDomains = normalized;
            _everApplied = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PowerShellRunner.RunAsync(RemovalScript(), TimeSpan.FromSeconds(60), _logger, cancellationToken);
            _lastDomains.Clear();
            _lastMode = WebsiteEnforcementMode.Disabled;
            _everApplied = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string RemovalScript()
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        script.AppendLine($"Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue");
        return script.ToString();
    }

    private static string BuildApplyScript(IEnumerable<string> domains)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine(RemovalScript());

        foreach (var domain in domains)
        {
            var id = StableGuid(domain).ToString("B");
            var ruleName = RulePrefix + StableGuid(domain).ToString("N")[..16];

            script.Append("Remove-NetFirewallDynamicKeywordAddress -Id ")
                .Append(PowerShellRunner.Quote(id))
                .AppendLine(" -ErrorAction SilentlyContinue");

            script.Append("New-NetFirewallDynamicKeywordAddress -Id ")
                .Append(PowerShellRunner.Quote(id))
                .Append(" -Keyword ")
                .Append(PowerShellRunner.Quote(domain))
                .AppendLine(" -AutoResolve $true | Out-Null");

            script.Append("New-NetFirewallRule -Name ")
                .Append(PowerShellRunner.Quote(ruleName))
                .Append(" -DisplayName ")
                .Append(PowerShellRunner.Quote("שומר זמן מסך: " + domain))
                .Append(" -Direction Outbound -Action Block -Enabled True -Profile Any -RemoteDynamicKeywordAddresses ")
                .Append(PowerShellRunner.Quote(id))
                .AppendLine(" | Out-Null");
        }

        return script.ToString();
    }

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("screen-time-guardian:" + value.ToLowerInvariant()));
        return new Guid(bytes[..16]);
    }
}
