namespace ScreenTimeGuardian.Contracts;

public static class ConfigurationMigrator
{
    public const int CurrentSchemaVersion = 5;

    public static ConfigurationDocument Migrate(ConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Applications ??= new List<ApplicationRule>();
        configuration.Websites ??= new List<WebsiteRule>();
        configuration.GoogleAccounts ??= new List<GoogleAccountRule>();
        configuration.DiscoveredSites ??= new List<DiscoveredSite>();

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
        configuration.Security ??= new ApplicationSecurity();
        configuration.Security.Iterations = Math.Clamp(configuration.Security.Iterations, 100_000, 1_000_000);
        configuration.BrowserLockdown ??= new BrowserLockdownSettings();
        configuration.ChangeControl ??= new ChangeControlSettings();
        configuration.SchemaVersion = CurrentSchemaVersion;

        configuration.Safety.BootGraceSeconds = Math.Clamp(configuration.Safety.BootGraceSeconds, 30, 900);
        configuration.Safety.ServiceGraceSeconds = Math.Clamp(configuration.Safety.ServiceGraceSeconds, 10, 600);
        configuration.Safety.MaxActionsPerMinute = Math.Clamp(configuration.Safety.MaxActionsPerMinute, 1, 200);
        configuration.ChangeControl.CoolingOffHours = Math.Clamp(configuration.ChangeControl.CoolingOffHours, 0, 720);
        configuration.BrowserLockdown.ScanIntervalMinutes = Math.Clamp(configuration.BrowserLockdown.ScanIntervalMinutes, 1, 1440);
        configuration.UpdateManifestUrl = configuration.UpdateManifestUrl?.Trim() ?? string.Empty;
        configuration.UpdatePublicKeyPem = configuration.UpdatePublicKeyPem?.Trim() ?? string.Empty;
        if (!ConfigurationValidation.IsValidHttpsUrl(configuration.UpdateManifestUrl)
            || !ConfigurationValidation.IsValidRsaPublicKeyPem(configuration.UpdatePublicKeyPem))
        {
            configuration.UpdateManifestUrl = string.Empty;
            configuration.AutomaticUpdatesEnabled = false;
        }
        configuration.BrowserLockdown.ApprovedBrowserPaths ??= new List<string>();
        configuration.BrowserLockdown.ApprovedBrowserPaths = configuration.BrowserLockdown.ApprovedBrowserPaths
            .Select(path => path.Trim())
            .Where(path => path.Length > 0
                && Path.IsPathFullyQualified(path)
                && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        configuration.BrowserLockdown.ExtraBlockedBrowserNames ??= new List<string>();
        configuration.BrowserLockdown.ExtraBlockedBrowserNames = configuration.BrowserLockdown.ExtraBlockedBrowserNames
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && BrowserIdentification.CanDenyByName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        configuration.BrowserLockdown.ExtraScanFolders ??= new List<string>();
        configuration.BrowserLockdown.ExtraScanFolders = configuration.BrowserLockdown.ExtraScanFolders
            .Select(path => path.Trim())
            .Where(path => path.Length > 0 && Path.IsPathFullyQualified(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        foreach (var rule in configuration.Applications)
        {
            rule.Windows ??= new List<ScheduleWindow>();
            rule.Targets ??= new List<AppTarget>();
            rule.AppliesToUserSids ??= new List<string>();
            foreach (var window in rule.Windows)
            {
                window.Days ??= new List<DayOfWeek>();
                window.ActivationDelaySeconds = Math.Clamp(window.ActivationDelaySeconds, 0, 86_400);
            }

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

        foreach (var rule in configuration.Websites)
        {
            rule.Windows ??= new List<ScheduleWindow>();
            foreach (var window in rule.Windows)
            {
                window.Days ??= new List<DayOfWeek>();
                window.ActivationDelaySeconds = Math.Clamp(window.ActivationDelaySeconds, 0, 86_400);
            }
        }

        configuration.Websites = configuration.Websites
            .Where(rule => ConfigurationValidation.IsValidDomain(rule.Domain))
            .ToList();

        configuration.GoogleAccounts = configuration.GoogleAccounts
            .Where(rule => ConfigurationValidation.IsValidEmail(rule.Email))
            .ToList();

        foreach (var account in configuration.GoogleAccounts)
        {
            account.Windows ??= new List<ScheduleWindow>();
            account.Services ??= new List<string>();
            account.Sites ??= new List<string>();
            foreach (var window in account.Windows)
            {
                window.Days ??= new List<DayOfWeek>();
                window.ActivationDelaySeconds = Math.Clamp(window.ActivationDelaySeconds, 0, 86_400);
            }

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

        NormalizeWindowIds(configuration.Applications.Cast<ScheduledRule>()
            .Concat(configuration.Websites)
            .Concat(configuration.GoogleAccounts));
    }

    private static void NormalizeWindowIds(IEnumerable<ScheduledRule> rules)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            foreach (var window in rule.Windows)
            {
                if (string.IsNullOrWhiteSpace(window.Id) || !ids.Add(window.Id))
                {
                    window.Id = Guid.NewGuid().ToString("N");
                    ids.Add(window.Id);
                }
            }
        }
    }
}
