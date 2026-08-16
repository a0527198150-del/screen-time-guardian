namespace ScreenTimeGuardian.Contracts;

public sealed class PolicyEngine
{
    public PolicySnapshot Evaluate(ConfigurationDocument configuration, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var activeWebsites = configuration.Websites
            .Where(rule => rule.IsActive(now))
            .ToList();
        var activeAccounts = configuration.GoogleAccounts
            .Where(rule => rule.IsActive(now))
            .ToList();
        var activeApplications = configuration.Applications
            .Where(rule => rule.IsActive(now))
            .ToList();

        var activeRuleIds = activeWebsites
            .Select(rule => rule.Id)
            .Concat(activeAccounts.Select(rule => rule.Id))
            .Concat(activeApplications.Select(rule => rule.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relevantExtensionBlock = activeWebsites.Count > 0 || activeAccounts.Count > 0;

        return new PolicySnapshot
        {
            GeneratedAtUtc = now.ToUniversalTime(),
            IsAnyBlockActive = activeRuleIds.Count > 0,
            BlockAllWebsites = activeWebsites.Count > 0,
            BlockedDomains = activeWebsites
                .Select(rule => NormalizeDomain(rule.Domain))
                .Where(domain => domain.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BlockedApplications = activeApplications
                .SelectMany(rule => rule.ExecutableNames)
                .Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            GoogleAccounts = activeAccounts
                .GroupBy(rule => rule.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new GoogleAccountPolicy
                {
                    Email = group.Key,
                    Services = group.SelectMany(rule => rule.Services)
                        .Select(service => service.Trim().ToLowerInvariant())
                        .Where(service => service.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList(),
            BlockPrivateAndGuestWhenExtensionUnavailable = configuration.BlockPrivateAndGuestWhenExtensionUnavailable
                && relevantExtensionBlock,
            BlockPortableBrowsers = configuration.BlockPortableBrowsersDuringAnySchedule
                && activeRuleIds.Count > 0,
            GuestModeAllowed = configuration.GuestModeAllowedWhenNoRelevantBlock && !relevantExtensionBlock,
            ActiveRuleIds = activeRuleIds
        };
    }

    public AccountDecisionResponse Decide(
        ConfigurationDocument configuration,
        AccountDecisionRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = Evaluate(configuration, now);
        var normalizedEmail = request.Email.Trim();
        var normalizedService = request.Service.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new AccountDecisionResponse
            {
                Blocked = policy.BlockPrivateAndGuestWhenExtensionUnavailable
                    && configuration.BlockUnknownGoogleSessionsDuringAccountSchedules,
                IdentityKnown = false,
                Reason = "לא ניתן לאמת את חשבון Google.",
                Policy = policy
            };
        }

        var account = policy.GoogleAccounts.FirstOrDefault(item =>
            string.Equals(item.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

        var blocked = account is not null
            && (account.Services.Count == 0
                || account.Services.Contains(normalizedService, StringComparer.OrdinalIgnoreCase));

        return new AccountDecisionResponse
        {
            Blocked = blocked,
            IdentityKnown = true,
            Reason = blocked ? "החשבון חסום לפי לוח הזמנים." : "החשבון אינו חסום.",
            Policy = policy
        };
    }

    public static string NormalizeDomain(string value)
    {
        var domain = value.Trim().ToLowerInvariant();
        while (domain.StartsWith("*.", StringComparison.Ordinal))
        {
            domain = domain[2..];
        }

        return domain.TrimEnd('.');
    }
}
