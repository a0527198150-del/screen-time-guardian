using System.Diagnostics;
using System.Text;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// First enforcement adapter for machine-wide website blocking.
/// It uses Windows Firewall dynamic FQDN keywords and never changes DNS configuration.
/// The service must run elevated for Enforced mode.
/// </summary>
public sealed class WindowsFirewallFqdnBlocker
{
    private const string RulePrefix = "STG-Website-";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string> _lastDomains = new(StringComparer.OrdinalIgnoreCase);
    private WebsiteEnforcementMode _lastMode = WebsiteEnforcementMode.Disabled;

    public async Task ApplyAsync(
        WebsiteEnforcementMode mode,
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken)
    {
        var normalized = domains
            .Select(PolicyEngine.NormalizeDomain)
            .Where(ConfigurationValidation.IsValidDomain)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (mode == _lastMode && normalized.SetEquals(_lastDomains))
            {
                return;
            }

            if (mode == WebsiteEnforcementMode.Disabled)
            {
                await RemoveManagedRulesAsync(cancellationToken);
            }
            else if (mode == WebsiteEnforcementMode.AuditOnly)
            {
                // Audit mode deliberately does not modify Windows Firewall.
                await RemoveManagedRulesAsync(cancellationToken);
            }
            else
            {
                await ReplaceManagedRulesAsync(normalized, cancellationToken);
            }

            _lastMode = mode;
            _lastDomains = normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ReplaceManagedRulesAsync(
        IEnumerable<string> domains,
        CancellationToken cancellationToken)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        script.AppendLine("$domains = @(");

        foreach (var domain in domains)
        {
            var escapedDomain = domain.Replace("'", "''", StringComparison.Ordinal);
            var safeId = StableGuid(domain).ToString("B");
            script.AppendLine($"  @{{ Domain = '{escapedDomain}'; Id = '{safeId}' }},");
        }

        script.AppendLine(");");
        script.AppendLine("foreach ($item in $domains) {");
        script.AppendLine("  Remove-NetFirewallDynamicKeywordAddress -Id $item.Id -ErrorAction SilentlyContinue");
        script.AppendLine("  New-NetFirewallDynamicKeywordAddress -Id $item.Id -Keyword $item.Domain -AutoResolve $true | Out-Null");
        script.AppendLine($"  New-NetFirewallRule -Name ('{RulePrefix}' + $item.Id.Trim('{{}}')) -DisplayName ('Screen Time Guardian: ' + $item.Domain) -Direction Outbound -Action Block -RemoteDynamicKeywordAddresses $item.Id | Out-Null");
        script.AppendLine("}");

        await RunPowerShellAsync(script.ToString(), cancellationToken);
    }

    private static async Task RemoveManagedRulesAsync(CancellationToken cancellationToken)
    {
        var script = $"Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule";
        await RunPowerShellAsync(script, cancellationToken);
    }

    private static async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start PowerShell for firewall policy");
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Windows Firewall policy failed: {error}");
        }
    }

    private static Guid StableGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("screen-time-guardian:" + value.ToLowerInvariant()));
        return new Guid(bytes[..16]);
    }
}
