using System.Text;

namespace ScreenTimeGuardian.Contracts;

/// <summary>
/// Decides whether a configuration change tightens or loosens the restrictions.
///
/// Rather than trying to reason about the meaning of each edit, it simulates the next
/// seven days at fifteen minute steps and compares what is blocked at every step.
/// If the new configuration blocks a superset of the old one at EVERY sample, the change
/// only ever adds restriction and can be applied at once. Otherwise it relaxes something
/// somewhere and goes through the cooling off delay.
///
/// 7 days x 96 samples = 672 evaluations. Cheap, and it cannot be fooled by clever edits.
/// </summary>
public static class RestrictionComparer
{
    private const int SampleDays = 7;
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    public static ChangeDirection Compare(
        ConfigurationDocument current,
        ConfigurationDocument proposed,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);

        // Lowering the cooling off delay is itself a loosening change; otherwise it could
        // be set to zero instantly and the whole mechanism would be pointless.
        if (proposed.ChangeControl.CoolingOffHours < current.ChangeControl.CoolingOffHours
            || (current.EnforceForAdministrators && !proposed.EnforceForAdministrators))
        {
            return ChangeDirection.Loosening;
        }

        if (IsLockdownWeaker(current.BrowserLockdown, proposed.BrowserLockdown)
            || IsScheduleDelayWeaker(current, proposed))
        {
            return ChangeDirection.Loosening;
        }

        var engine = new PolicyEngine();
        var end = now.AddDays(SampleDays);

        for (var moment = now; moment < end; moment += Step)
        {
            var before = Fingerprint(engine.Evaluate(current, moment));
            var after = Fingerprint(engine.Evaluate(proposed, moment));

            if (!before.IsSubsetOf(after))
            {
                return ChangeDirection.Loosening;
            }
        }

        return ChangeDirection.Tightening;
    }

    /// <summary>Describes, in Hebrew, what the change actually does. Shown to the user.</summary>
    public static string Describe(
        ConfigurationDocument current,
        ConfigurationDocument proposed,
        DateTimeOffset now)
    {
        var engine = new PolicyEngine();
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var end = now.AddDays(SampleDays);

        for (var moment = now; moment < end; moment += Step)
        {
            var before = Fingerprint(engine.Evaluate(current, moment));
            var after = Fingerprint(engine.Evaluate(proposed, moment));

            foreach (var item in before.Except(after))
            {
                removed.Add(item);
            }

            foreach (var item in after.Except(before))
            {
                added.Add(item);
            }
        }

        var builder = new StringBuilder();
        if (removed.Count > 0)
        {
            builder.Append($"מבטל חסימה של {removed.Count} פריטים ({Sample(removed)})");
        }

        if (added.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append($"מוסיף חסימה של {added.Count} פריטים ({Sample(added)})");
        }

        if (proposed.EnforceForAdministrators != current.EnforceForAdministrators)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(proposed.EnforceForAdministrators
                ? "כולל משתמשים עם הרשאת מנהל"
                : "מחריג משתמשים עם הרשאת מנהל");
        }

        if (proposed.ChangeControl.CoolingOffHours != current.ChangeControl.CoolingOffHours)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append($"משנה זמן צינון מ־{current.ChangeControl.CoolingOffHours} ל־{proposed.ChangeControl.CoolingOffHours} שעות");
        }

        return builder.Length == 0 ? "שינוי הגדרות ללא השפעה על החסימות" : builder.ToString();
    }

    private static string Sample(IEnumerable<string> items)
    {
        var list = items
            .Select(item => item.Split('|').LastOrDefault() ?? item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        return string.Join(", ", list);
    }

    /// <summary>Everything blocked at one instant, as a comparable set of strings.</summary>
    private static HashSet<string> Fingerprint(PolicySnapshot snapshot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in snapshot.BlockedDomains)
        {
            set.Add("domain|" + domain);
        }

        foreach (var block in snapshot.NetworkBlocks)
        {
            var scope = block.UserSids.Count == 0
                ? "*"
                : string.Join(",", block.UserSids.OrderBy(sid => sid, StringComparer.Ordinal));

            // A machine wide block also covers every individual user, so record both forms.
            set.Add($"net|{scope}|{Path.GetFileName(block.ExecutablePath)}");
            if (block.UserSids.Count == 0)
            {
                set.Add($"net|any|{Path.GetFileName(block.ExecutablePath)}");
            }
        }

        foreach (var account in snapshot.GoogleAccounts)
        {
            foreach (var service in account.Services)
            {
                set.Add($"svc|{account.Email}|{service}");
            }

            foreach (var site in account.Sites)
            {
                set.Add($"site|{account.Email}|{site}");
            }
        }

        return set;
    }

    private static bool IsScheduleDelayWeaker(ConfigurationDocument current, ConfigurationDocument proposed)
    {
        var currentDelays = AllWindows(current)
            .ToDictionary(window => window.Id, window => window.ActivationDelaySeconds);

        return AllWindows(proposed).Any(window =>
            currentDelays.TryGetValue(window.Id, out var previous)
            && window.ActivationDelaySeconds > previous)
            || currentDelays.Keys.Except(AllWindows(proposed).Select(window => window.Id), StringComparer.OrdinalIgnoreCase).Any();
    }

    private static IEnumerable<ScheduleWindow> AllWindows(ConfigurationDocument configuration) =>
        configuration.Applications.SelectMany(rule => rule.Windows)
            .Concat(configuration.Websites.SelectMany(rule => rule.Windows))
            .Concat(configuration.GoogleAccounts.SelectMany(rule => rule.Windows));

    private static bool IsLockdownWeaker(BrowserLockdownSettings current, BrowserLockdownSettings proposed)
    {
        if (current.BlockUnapprovedBrowserLaunch && !proposed.BlockUnapprovedBrowserLaunch)
        {
            return true;
        }

        if (current.ScanForHiddenBrowsers && !proposed.ScanForHiddenBrowsers)
        {
            return true;
        }

        if (proposed.ScanIntervalMinutes > current.ScanIntervalMinutes)
        {
            return true;
        }

        // Enabling the approval switch with paths is a relaxation even when the paths
        // were already present in the saved list.
        if (!current.AllowApprovedBrowsersWithoutExtension
            && proposed.AllowApprovedBrowsersWithoutExtension
            && proposed.ApprovedBrowserPaths.Count > 0)
        {
            return true;
        }

        // Approving a browser that was previously unapproved is a relaxation.
        return proposed.ApprovedBrowserPaths
            .Any(path => !current.ApprovedBrowserPaths.Contains(path, StringComparer.OrdinalIgnoreCase));
    }
}
