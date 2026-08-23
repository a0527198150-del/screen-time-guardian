namespace ScreenTimeGuardian.Contracts;

public sealed class PolicyEngine
{
    public PolicySnapshot Evaluate(ConfigurationDocument configuration, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var activeWebsites = configuration.Websites.Where(rule => rule.IsActive(now)).ToList();
        var activeAccounts = configuration.GoogleAccounts.Where(rule => rule.IsActive(now)).ToList();
        var activeApplications = configuration.Applications.Where(rule => rule.IsActive(now)).ToList();

        var activeRuleIds = activeWebsites.Select(rule => rule.Id)
            .Concat(activeAccounts.Select(rule => rule.Id))
            .Concat(activeApplications.Select(rule => rule.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var networkBlocks = new List<NetworkBlockTarget>();

        foreach (var rule in activeApplications)
        {
            foreach (var target in rule.Targets)
            {
                if (ProtectedPaths.IsProtectedPath(target.ExecutablePath))
                {
                    continue;
                }

                networkBlocks.Add(new NetworkBlockTarget
                {
                    ExecutablePath = target.ExecutablePath,
                    DisplayName = string.IsNullOrWhiteSpace(target.DisplayName) ? rule.Name : target.DisplayName,
                    UserSids = rule.AppliesToUserSids.ToList(),
                    ExcludeAdministrators = !configuration.EnforceForAdministrators
                });
            }
        }

        var relevantExtensionBlock = activeAccounts.Count > 0;

        return new PolicySnapshot
        {
            GeneratedAtUtc = now.ToUniversalTime(),
            IsAnyBlockActive = activeRuleIds.Count > 0,
            BlockedDomains = activeWebsites
                .Select(rule => NormalizeDomain(rule.Domain))
                .Where(domain => domain.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            NetworkBlocks = Consolidate(networkBlocks),
            GoogleAccounts = activeAccounts
                .GroupBy(rule => rule.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new GoogleAccountPolicy
                {
                    Email = group.Key,
                    Services = group.SelectMany(rule => rule.Services)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Sites = group.SelectMany(rule => rule.Sites)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList(),
            BlockUnknownGoogleSessions = configuration.BlockUnknownGoogleSessionsDuringAccountSchedules
                && relevantExtensionBlock,
            GuestModeAllowed = configuration.GuestModeAllowedWhenNoRelevantBlock && !relevantExtensionBlock,
            ActiveRuleIds = activeRuleIds
        };
    }

    /// <summary>
    /// Removes exact duplicate targets without merging different user scopes. A target
    /// for all non-admin users cannot safely be merged with a target for one named user:
    /// collapsing those scopes would silently broaden a firewall rule.
    /// </summary>
    private static List<NetworkBlockTarget> Consolidate(IEnumerable<NetworkBlockTarget> targets)
    {
        return targets
            .GroupBy(target => target.Signature, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public AccountDecisionResponse Decide(
        ConfigurationDocument configuration,
        AccountDecisionRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = Evaluate(configuration, now);
        var email = request.Email.Trim();
        var service = request.Service.Trim().ToLowerInvariant();
        var origin = ConfigurationValidation.NormalizeOrigin(request.Origin ?? string.Empty);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new AccountDecisionResponse
            {
                Blocked = policy.BlockUnknownGoogleSessions,
                IdentityKnown = false,
                Reason = "לא ניתן לזהות באיזה חשבון Google אתה מחובר.",
                Policy = policy
            };
        }

        var account = policy.GoogleAccounts.FirstOrDefault(item =>
            string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            return new AccountDecisionResponse
            {
                Blocked = false,
                IdentityKnown = true,
                Reason = "החשבון אינו חסום כרגע.",
                Policy = policy
            };
        }

        // A site rule beats a service rule: it is the more specific statement.
        if (origin.Length > 0 && account.Sites.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return new AccountDecisionResponse
            {
                Blocked = true,
                IdentityKnown = true,
                Reason = $"האתר {origin} חסום עבור {email} לפי לוח הזמנים.",
                Policy = policy
            };
        }

        var blocked = service.Length > 0
            && account.Services.Contains(service, StringComparer.OrdinalIgnoreCase);

        return new AccountDecisionResponse
        {
            Blocked = blocked,
            IdentityKnown = true,
            Reason = blocked
                ? $"השירות {service} חסום עבור {email} לפי לוח הזמנים."
                : "החשבון אינו חסום כרגע.",
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
