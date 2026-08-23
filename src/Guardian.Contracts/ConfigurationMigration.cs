namespace ScreenTimeGuardian.Contracts;

public static class ConfigurationMigrator
{
    public const int CurrentSchemaVersion = 3;

    public static ConfigurationDocument Migrate(ConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.SchemaVersion < 3)
        {
            // Schema v2 matched applications by bare process name across every session.
            // That is exactly what allowed the machine to be destabilised, so every
            // migrated rule is DISABLED and the old names are kept for reference only.
            foreach (var rule in configuration.Applications)
            {
                if (rule.Targets.Count == 0 && rule.LegacyProcessNames.Count > 0)
                {
                    rule.Enabled = false;
                }

            }

            configuration.Safety = new SafetySettings();
            configuration.SchemaVersion = 3;
        }

        Sanitize(configuration);
        return configuration;
    }

    /// <summary>Strips anything unsafe or malformed, whatever wrote the file.</summary>
    public static void Sanitize(ConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Safety ??= new SafetySettings();
        configuration.Safety.BootGraceSeconds = Math.Clamp(configuration.Safety.BootGraceSeconds, 30, 900);
        configuration.Safety.ServiceGraceSeconds = Math.Clamp(configuration.Safety.ServiceGraceSeconds, 10, 600);
        configuration.Safety.MaxActionsPerMinute = Math.Clamp(configuration.Safety.MaxActionsPerMinute, 1, 200);

        foreach (var rule in configuration.Applications)
        {
            rule.Targets = rule.Targets
                .Where(target => !string.IsNullOrWhiteSpace(target.ExecutablePath))
                .Where(target => !ProtectedPaths.IsProtectedPath(target.ExecutablePath))
                .GroupBy(target => target.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            rule.AppliesToUserSids = rule.AppliesToUserSids
                .Where(ConfigurationValidation.IsValidUserSid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        configuration.Websites = configuration.Websites
            .Where(rule => ConfigurationValidation.IsValidDomain(rule.Domain))
            .ToList();

        configuration.GoogleAccounts = configuration.GoogleAccounts
            .Where(rule => ConfigurationValidation.IsValidEmail(rule.Email))
            .ToList();

        foreach (var account in configuration.GoogleAccounts)
        {
            account.Services = account.Services
                .Select(service => service.Trim().ToLowerInvariant())
                .Where(service => service.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            account.Sites = account.Sites
                .Select(ConfigurationValidation.NormalizeOrigin)
                .Where(origin => origin.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Cap the discovered list so a misbehaving extension can never grow the file without bound.
        configuration.DiscoveredSites = configuration.DiscoveredSites
            .Where(site => ConfigurationValidation.IsValidOrigin(site.Origin))
            .OrderByDescending(site => site.LastSeenUtc)
            .Take(500)
            .ToList();
    }
}
