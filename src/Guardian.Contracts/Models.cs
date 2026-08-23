using System.Text.Json.Serialization;

namespace ScreenTimeGuardian.Contracts;

public enum WebsiteEnforcementMode
{
    Disabled,
    AuditOnly,
    Enforced
}

public sealed class ScheduleWindow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public List<DayOfWeek> Days { get; set; } = new();
    public string Start { get; set; } = "23:00";
    public string End { get; set; } = "07:00";
    public bool AllDay { get; set; }

    /// <summary>
    /// Seconds between the scheduled start and the beginning of enforcement.
    /// Zero preserves the original immediate behaviour.
    /// </summary>
    public int ActivationDelaySeconds { get; set; }

    public bool Contains(DateTimeOffset now)
    {
        if (!Enabled || Days.Count == 0)
        {
            return false;
        }

        var local = now.LocalDateTime;
        var start = default(TimeOnly);
        var end = default(TimeOnly);
        if (!AllDay
            && (!TimeOnly.TryParse(Start, out start) || !TimeOnly.TryParse(End, out end)))
        {
            return false;
        }

        var delay = TimeSpan.FromSeconds(Math.Clamp(ActivationDelaySeconds, 0, 86_400));
        for (var dayOffset = -1; dayOffset <= 0; dayOffset++)
        {
            var ruleDate = local.Date.AddDays(dayOffset);
            if (!Days.Contains(ruleDate.DayOfWeek))
            {
                continue;
            }

            var effectiveStart = AllDay
                ? ruleDate + delay
                : ruleDate + start.ToTimeSpan() + delay;
            var effectiveEnd = AllDay
                ? ruleDate.AddDays(1)
                : start < end
                    ? ruleDate + end.ToTimeSpan()
                    : ruleDate.AddDays(1) + end.ToTimeSpan();

            // Delaying the start must never move the end of a cross-midnight window
            // backward past the delayed start. A one-second polling boundary is
            // intentionally not promised; the service samples every 15 seconds.

            if (start == end && !AllDay)
            {
                // Keep the legacy meaning of equal start/end: the rule covers the day.
                effectiveStart = ruleDate + delay;
                effectiveEnd = ruleDate.AddDays(1);
            }

            if (effectiveStart < effectiveEnd && local >= effectiveStart && local < effectiveEnd)
            {
                return true;
            }
        }

        return false;
    }

    public string Describe()
    {
        var days = Days.Count == 0
            ? "ללא ימים"
            : string.Join(", ", Days.OrderBy(day => (int)day).Select(HebrewDays.Name));
        var time = AllDay ? "כל היום" : $"{Start}–{End}";
        var delay = ActivationDelaySeconds > 0 ? $" · השהיה {ActivationDelaySeconds} שנ׳" : string.Empty;
        var state = Enabled ? string.Empty : " (מושבת)";
        return $"{days} · {time}{delay}{state}";
    }

    public override string ToString() => Describe();
}

public static class HebrewDays
{
    public static string Name(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "ראשון",
        DayOfWeek.Monday => "שני",
        DayOfWeek.Tuesday => "שלישי",
        DayOfWeek.Wednesday => "רביעי",
        DayOfWeek.Thursday => "חמישי",
        DayOfWeek.Friday => "שישי",
        DayOfWeek.Saturday => "שבת",
        _ => day.ToString()
    };
}

public abstract class ScheduledRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<ScheduleWindow> Windows { get; set; } = new();

    /// <summary>Windows user SIDs this rule applies to. Empty means every user on the machine.</summary>
    public List<string> AppliesToUserSids { get; set; } = new();

    public bool IsActive(DateTimeOffset now) => Enabled && Windows.Any(window => window.Contains(now));
}

public sealed class WebsiteRule : ScheduledRule
{
    public string Domain { get; set; } = string.Empty;

    public override string ToString() => Enabled ? Domain : $"{Domain} (מושבת)";
}

public sealed class GoogleAccountRule : ScheduledRule
{
    public string Email { get; set; } = string.Empty;

    /// <summary>Google service keys such as gmail, drive, youtube.</summary>
    public List<string> Services { get; set; } = new();

    /// <summary>Third party site origins (https://example.com) blocked while signed in with this account.</summary>
    public List<string> Sites { get; set; } = new();

    public override string ToString() => Enabled ? Email : $"{Email} (מושבת)";
}

public sealed class AppTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName)
        ? ExecutablePath
        : $"{DisplayName}  —  {ExecutablePath}";
}

public sealed class ApplicationRule : ScheduledRule
{
    public List<AppTarget> Targets { get; set; } = new();

    /// <summary>
    /// Executable names carried over from schema v2. Informational only; never enforced.
    /// This software does not terminate processes at all - see docs/Safety.md.
    /// </summary>
    public List<string> LegacyProcessNames { get; set; } = new();

    public override string ToString()
    {
        var label = string.IsNullOrWhiteSpace(Name) ? "כלל ללא שם" : Name;
        var count = Targets.Count == 1 ? "אפליקציה אחת" : $"{Targets.Count} אפליקציות";
        return Enabled ? $"{label}  ({count})" : $"{label}  ({count}) — מושבת";
    }
}

/// <summary>A site the browser extension saw a Google sign-in on. Awaiting a parent decision.</summary>
public sealed class DiscoveredSite
{
    public string Origin { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool Dismissed { get; set; }

    public override string ToString() => $"{Origin}  ←  {Email}";
}

public sealed class SafetySettings
{
    /// <summary>Seconds after Windows boots during which nothing is enforced, so you can always log in and fix things.</summary>
    public int BootGraceSeconds { get; set; } = 120;

    /// <summary>Seconds after the service starts during which nothing is enforced.</summary>
    public int ServiceGraceSeconds { get; set; } = 30;

    /// <summary>If more enforcement actions than this happen inside one minute, the service shuts enforcement down.</summary>
    public int MaxActionsPerMinute { get; set; } = 20;

}

public sealed class ConfigurationDocument
{
    public int SchemaVersion { get; set; } = 5;
    public WebsiteEnforcementMode WebsiteEnforcement { get; set; } = WebsiteEnforcementMode.AuditOnly;
    public bool BlockUnknownGoogleSessionsDuringAccountSchedules { get; set; } = true;
    public bool GuestModeAllowedWhenNoRelevantBlock { get; set; } = true;
    /// <summary>When true, scheduled enforcement also includes users in the local Administrators group.</summary>
    public bool EnforceForAdministrators { get; set; }

    /// <summary>Enables the signed update polling path. Disabled by default.</summary>
    public bool AutomaticUpdatesEnabled { get; set; }
    public string UpdateManifestUrl { get; set; } = string.Empty;
    public string UpdatePublicKeyPem { get; set; } = string.Empty;

    public ApplicationSecurity Security { get; set; } = new();
    public SafetySettings Safety { get; set; } = new();
    public BrowserLockdownSettings BrowserLockdown { get; set; } = new();
    public ChangeControlSettings ChangeControl { get; set; } = new();

    /// <summary>A loosening change waiting out its cooling off delay. At most one at a time.</summary>
    public PendingChange? PendingChange { get; set; }

    public List<WebsiteRule> Websites { get; set; } = new();
    public List<GoogleAccountRule> GoogleAccounts { get; set; } = new();
    public List<ApplicationRule> Applications { get; set; } = new();
    public List<DiscoveredSite> DiscoveredSites { get; set; } = new();

    [JsonIgnore]
    public static ConfigurationDocument Default => new();
}

public sealed class NetworkBlockTarget
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> UserSids { get; set; } = new();

    /// <summary>When true, machine-wide rules are materialized only for non-admin users.</summary>
    public bool ExcludeAdministrators { get; set; }

    public string Signature => $"{ExecutablePath.ToLowerInvariant()}|{string.Join(",", UserSids.OrderBy(sid => sid, StringComparer.Ordinal))}|{ExcludeAdministrators}";
}

public sealed class GoogleAccountPolicy
{
    public string Email { get; set; } = string.Empty;
    public List<string> Services { get; set; } = new();
    public List<string> Sites { get; set; } = new();
}

public sealed class PolicySnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsAnyBlockActive { get; set; }
    public List<string> BlockedDomains { get; set; } = new();
    public List<NetworkBlockTarget> NetworkBlocks { get; set; } = new();
    public List<GoogleAccountPolicy> GoogleAccounts { get; set; } = new();
    public bool BlockUnknownGoogleSessions { get; set; }
    public bool GuestModeAllowed { get; set; } = true;
    public List<string> ActiveRuleIds { get; set; } = new();
}

public sealed class AccountDecisionRequest
{
    public string Email { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
}

public sealed class AccountDecisionResponse
{
    public bool Blocked { get; set; }
    public bool IdentityKnown { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PolicySnapshot Policy { get; set; } = new();
}

public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string? SignatureUrl { get; set; }
    public string Signature { get; set; } = string.Empty;
    public bool RequiresAdministrator { get; set; } = true;
}

public sealed class NativeMessage
{
    public string Type { get; set; } = string.Empty;
    public AccountDecisionRequest? Account { get; set; }
    public string? Service { get; set; }
    public string? Origin { get; set; }
    public string? Email { get; set; }
}

// ============================================================================
// Browser lockdown
// ============================================================================

/// <summary>
/// Settings for keeping the managed extension in place and stopping an unapproved
/// browser from becoming an escape hatch.
/// </summary>
public sealed class BrowserLockdownSettings
{
    /// <summary>Deny launch of known browser executables by name via Image File Execution Options.</summary>
    public bool BlockUnapprovedBrowserLaunch { get; set; }

    /// <summary>Scan the disk for browser executables and cut their internet access.</summary>
    public bool ScanForHiddenBrowsers { get; set; }

    /// <summary>Minutes between disk scans.</summary>
    public int ScanIntervalMinutes { get; set; } = 10;

    /// <summary>Allow the listed browsers to run without the Guardian extension. Disabled by default.</summary>
    public bool AllowApprovedBrowsersWithoutExtension { get; set; }

    /// <summary>Full paths used only when AllowApprovedBrowsersWithoutExtension is enabled.</summary>
    public List<string> ApprovedBrowserPaths { get; set; } = new();

    /// <summary>Extra executable names to deny by name, on top of the built-in list.</summary>
    public List<string> ExtraBlockedBrowserNames { get; set; } = new();

    /// <summary>Additional folders to scan, on top of the built-in set.</summary>
    public List<string> ExtraScanFolders { get; set; } = new();
}

// ============================================================================
// Change control (cooling off)
// ============================================================================

public enum ChangeDirection
{
    /// <summary>The new configuration restricts at least as much at every moment. Applied at once.</summary>
    Tightening,

    /// <summary>The new configuration restricts less at some moment. Subject to the cooling off delay.</summary>
    Loosening
}

public sealed class ChangeControlSettings
{
    /// <summary>
    /// Hours a loosening change waits before it takes effect.
    /// Zero means changes apply immediately - the mechanism exists but is not armed.
    /// This value can be RAISED at once; lowering it is itself a loosening change.
    /// </summary>
    public int CoolingOffHours { get; set; }
}

/// <summary>A configuration change that reduces restriction and is waiting out its delay.</summary>
public sealed class PendingChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EffectiveAtUtc { get; set; }
    public string Summary { get; set; } = string.Empty;

    /// <summary>The full configuration document to install once the delay elapses, serialized as JSON.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public bool IsDue(DateTimeOffset now) => now >= EffectiveAtUtc;

    public override string ToString()
    {
        var remaining = EffectiveAtUtc - DateTimeOffset.UtcNow;
        var when = remaining <= TimeSpan.Zero
            ? "מוכן להחלה"
            : $"עוד {(int)remaining.TotalHours} שעות ו־{remaining.Minutes} דקות";
        return $"{Summary} — {when}";
    }
}

/// <summary>Something that is about to start or stop being blocked. Used for warning notifications.</summary>
public sealed class UpcomingEvent
{
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartsAtUtc { get; set; }
    public bool IsBlockStarting { get; set; }
}
