using System.Text.Json.Serialization;

namespace ScreenTimeGuardian.Contracts;

public enum BlockRuleKind
{
    Website,
    GoogleAccount,
    Application
}

public enum WebsiteEnforcementMode
{
    Disabled,
    AuditOnly,
    Enforced
}

public sealed class ScheduleWindow
{
    public bool Enabled { get; set; } = true;
    public List<DayOfWeek> Days { get; set; } = new();
    public string Start { get; set; } = "23:00";
    public string End { get; set; } = "07:00";
    public bool AllDay { get; set; }

    public bool Contains(DateTimeOffset now)
    {
        if (!Enabled || Days.Count == 0)
        {
            return false;
        }

        var local = now.LocalDateTime;
        if (AllDay && Days.Contains(local.DayOfWeek))
        {
            return true;
        }

        if (!TimeOnly.TryParse(Start, out var start) || !TimeOnly.TryParse(End, out var end))
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(local);
        if (start < end)
        {
            return Days.Contains(local.DayOfWeek) && time >= start && time < end;
        }

        if (start > end)
        {
            var previousDay = local.Date.AddDays(-1).DayOfWeek;
            return (Days.Contains(local.DayOfWeek) && time >= start)
                || (Days.Contains(previousDay) && time < end);
        }

        return Days.Contains(local.DayOfWeek);
    }
}

public abstract class ScheduledRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<ScheduleWindow> Windows { get; set; } = new();

    public bool IsActive(DateTimeOffset now) => Enabled && Windows.Any(window => window.Contains(now));
}

public sealed class WebsiteRule : ScheduledRule
{
    public string Domain { get; set; } = string.Empty;
}

public sealed class GoogleAccountRule : ScheduledRule
{
    public string Email { get; set; } = string.Empty;
    public List<string> Services { get; set; } = new() { "gmail", "chat" };
}

public sealed class ApplicationRule : ScheduledRule
{
    public List<string> ExecutableNames { get; set; } = new();
}

public sealed class BrowserApproval
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public bool RequiresManagedExtension { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset ApprovedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConfigurationDocument
{
    public int SchemaVersion { get; set; } = 2;
    public WebsiteEnforcementMode WebsiteEnforcement { get; set; } = WebsiteEnforcementMode.AuditOnly;
    public bool BlockPrivateAndGuestWhenExtensionUnavailable { get; set; } = true;
    public bool BlockUnknownGoogleSessionsDuringAccountSchedules { get; set; } = true;
    public bool BlockPortableBrowsersDuringAnySchedule { get; set; } = true;
    public bool StrictPortableApplicationMode { get; set; }
    public bool AutomaticUpdatesEnabled { get; set; }
    public string UpdateManifestUrl { get; set; } = string.Empty;
    public bool GuestModeAllowedWhenNoRelevantBlock { get; set; } = true;
    public List<WebsiteRule> Websites { get; set; } = new();
    public List<GoogleAccountRule> GoogleAccounts { get; set; } = new();
    public List<ApplicationRule> Applications { get; set; } = new();
    public List<BrowserApproval> ApprovedBrowsers { get; set; } = new();

    [JsonIgnore]
    public static ConfigurationDocument Default => new();
}

public sealed class GoogleAccountPolicy
{
    public string Email { get; set; } = string.Empty;
    public List<string> Services { get; set; } = new();
}

public sealed class PolicySnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsAnyBlockActive { get; set; }
    public bool BlockAllWebsites { get; set; }
    public List<string> BlockedDomains { get; set; } = new();
    public List<string> BlockedApplications { get; set; } = new();
    public List<GoogleAccountPolicy> GoogleAccounts { get; set; } = new();
    public bool BlockPrivateAndGuestWhenExtensionUnavailable { get; set; }
    public bool BlockPortableBrowsers { get; set; }
    public bool GuestModeAllowed { get; set; }
    public List<string> ActiveRuleIds { get; set; } = new();
}

public sealed class AccountDecisionRequest
{
    public string Email { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
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
    public bool RequiresAdministrator { get; set; } = true;
}

public sealed class NativeMessage
{
    public string Type { get; set; } = string.Empty;
    public AccountDecisionRequest? Account { get; set; }
    public string? Service { get; set; }
}
