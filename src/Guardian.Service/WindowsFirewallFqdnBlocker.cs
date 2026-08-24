using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Machine-wide website blocking through Windows Firewall dynamic FQDN keywords.
/// DNS configuration is never touched, so Netfree keeps working unchanged.
///
/// Note: this blocks a domain for every user on the machine. Account-specific blocking
/// is handled by the browser extension, which is the only component that knows which
/// Google account is signed in.
///
/// The FQDN mechanism depends on Defender's Network Protection callout driver observing
/// DNS responses. If that component is unavailable, or DNS over HTTPS is in use, the
/// keywords resolve to nothing and the rules are not enforced. This class verifies the
/// rules it created actually exist and refuses to cache a failed apply, so the condition
/// appears in the log instead of looking like success.
/// </summary>
public sealed class WindowsFirewallFqdnBlocker
{
    private const string RulePrefix = "STG-Website-";
    private const string CountMarker = "STG-RULECOUNT=";

    private readonly SafetyEnvelope _safety;
    private readonly ILogger<WindowsFirewallFqdnBlocker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HashSet<string> _lastDomains = new(StringComparer.OrdinalIgnoreCase);
    private WebsiteEnforcementMode _lastMode = WebsiteEnforcementMode.Disabled;
    private bool _everApplied;

    /// <summary>Number of website rules that actually exist after the last successful apply.</summary>
    public int ActiveRuleCount { get; private set; }

    /// <summary>Empty when the last apply succeeded; otherwise contains the diagnostic error.</summary>
    public string LastError { get; private set; } = string.Empty;

    public WindowsFirewallFqdnBlocker(
        SafetyEnvelope safety,
        ILogger<WindowsFirewallFqdnBlocker> logger)
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

            var enforcing = mode == WebsiteEnforcementMode.Enforced && normalized.Count > 0;
            var expectedRules = enforcing ? normalized.Count * 2 : 0;
            var script = enforcing ? BuildApplyScript(normalized) : BuildRemovalScript();
            var result = await PowerShellRunner.RunAsync(
                script,
                TimeSpan.FromSeconds(90),
                _logger,
                cancellationToken);
            var actualRules = ParseRuleCount(result.Output);

            if (!result.Ok || actualRules != expectedRules)
            {
                LastError = string.IsNullOrWhiteSpace(result.Error)
                    ? $"נוצרו {actualRules} חוקים מתוך {expectedRules} מצופים"
                    : result.Error.Trim();
                ActiveRuleCount = Math.Max(actualRules, 0);
                _logger.LogError(
                    "Website firewall policy failed. Expected {Expected} rules, found {Actual}. Error: {Error}",
                    expectedRules,
                    actualRules,
                    LastError);

                // Deliberately do not update the cache. The next cycle retries instead
                // of assuming that a partial or failed apply succeeded.
                return;
            }

            LastError = string.Empty;
            ActiveRuleCount = actualRules;
            _lastMode = mode;
            _lastDomains = normalized;
            _everApplied = true;
            _logger.LogInformation(
                "Website firewall policy applied. Mode={Mode}, domains={Domains}, rules={Rules}",
                mode,
                normalized.Count,
                actualRules);
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
            var result = await PowerShellRunner.RunAsync(
                BuildRemovalScript(),
                TimeSpan.FromSeconds(60),
                _logger,
                cancellationToken);
            var actualRules = ParseRuleCount(result.Output);
            if (!result.Ok || actualRules != 0)
            {
                LastError = string.IsNullOrWhiteSpace(result.Error)
                    ? $"ניקוי חסימת האתרים השאיר {actualRules} חוקים"
                    : result.Error.Trim();
                ActiveRuleCount = Math.Max(actualRules, 0);
                _logger.LogError("Could not remove website firewall rules: {Error}", LastError);
                return;
            }

            _lastDomains.Clear();
            _lastMode = WebsiteEnforcementMode.Disabled;
            _everApplied = true;
            ActiveRuleCount = 0;
            LastError = string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the rule count the script reports. A missing marker is itself a failure
    /// signal, so it returns -1 rather than treating empty output as success.
    /// </summary>
    private static int ParseRuleCount(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return -1;
        }

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(CountMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[CountMarker.Length..].Trim();
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : -1;
        }

        return -1;
    }

    /// <summary>
    /// Removal commands suppress only their own errors. They do not change the caller's
    /// error preference, so failures in the apply script remain terminating.
    /// </summary>
    private static string RemovalLines()
    {
        var script = new StringBuilder();
        script.AppendLine(
            $"Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue | " +
            "Remove-NetFirewallRule -ErrorAction SilentlyContinue");
        return script.ToString();
    }

    private static string CountLines()
    {
        var script = new StringBuilder();
        script.AppendLine(
            $"$stgCount = @(Get-NetFirewallRule -Name '{RulePrefix}*' -ErrorAction SilentlyContinue).Count");
        script.AppendLine($"Write-Output ('{CountMarker}' + $stgCount)");
        return script.ToString();
    }

    private static string BuildRemovalScript()
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Continue'");
        script.Append(RemovalLines());
        script.Append(CountLines());
        return script.ToString();
    }

    private static string BuildApplyScript(IEnumerable<string> domains)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("try {");
        script.Append(RemovalLines());

        foreach (var domain in domains)
        {
            // A bare keyword does not cover subdomains. Create both forms so a rule
            // for example.com also covers www.example.com and deeper subdomains.
            AppendKeywordRule(script, domain, domain);
            AppendKeywordRule(script, "*." + domain, domain + " (subdomains)");
        }

        script.Append(CountLines());
        script.AppendLine("} catch {");
        script.AppendLine("    [Console]::Error.WriteLine($_.Exception.ToString())");
        script.AppendLine("    exit 1");
        script.AppendLine("}");
        return script.ToString();
    }

    private static void AppendKeywordRule(StringBuilder script, string keyword, string label)
    {
        var id = StableGuid(keyword).ToString("B");
        var ruleName = RulePrefix + StableGuid(keyword).ToString("N")[..16];

        script.Append("Remove-NetFirewallDynamicKeywordAddress -Id ")
            .Append(PowerShellRunner.Quote(id))
            .AppendLine(" -ErrorAction SilentlyContinue");
        script.Append("New-NetFirewallDynamicKeywordAddress -Id ")
            .Append(PowerShellRunner.Quote(id))
            .Append(" -Keyword ")
            .Append(PowerShellRunner.Quote(keyword))
            .AppendLine(" -AutoResolve $true | Out-Null");
        script.Append("New-NetFirewallRule -Name ")
            .Append(PowerShellRunner.Quote(ruleName))
            .Append(" -DisplayName ")
            .Append(PowerShellRunner.Quote("שומר זמן מסך: " + label))
            .Append(" -Direction Outbound -Action Block -Enabled True -Profile Any -RemoteDynamicKeywordAddresses ")
            .Append(PowerShellRunner.Quote(id))
            .AppendLine(" | Out-Null");
    }

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes("screen-time-guardian:" + value.ToLowerInvariant()));
        return new Guid(bytes[..16]);
    }
}
