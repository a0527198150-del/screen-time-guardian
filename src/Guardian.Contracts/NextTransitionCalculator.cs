using System.Diagnostics;

namespace ScreenTimeGuardian.Contracts;

/// <summary>
/// Works out the next moment at which the applied policy would need to change.
///
/// Every rule carries a set of weekly ScheduleWindow entries, so the timeline of
/// transitions is fully known in advance: a window opening and a window closing are
/// the only two events that can alter what should be enforced. Everything between
/// them is dead time the service currently spends waking up for nothing.
///
/// Returns null when no rule has any window at all - the caller then falls back to
/// the resilience heartbeat alone.
/// </summary>
public static class NextTransitionCalculator
{
    public static DateTimeOffset? Calculate(ConfigurationDocument configuration,
        DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var rule in EnumerateScheduledRules(configuration))
        {
            if (!rule.Enabled)
            {
                continue;
            }
            foreach (var window in rule.Windows)
            {
                foreach (var edge in EnumerateEdges(window, now))
                {
                    if (edge > now && (earliest is null || edge < earliest))
                    {
                        earliest = edge;
                    }
                }
            }
        }

        var description = earliest is null
            ? "none within 8 days"
            : $"at {earliest.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss} (in {(earliest.Value - now).TotalSeconds:F0}s)";

        // Cheap, always-on diagnostic entry so a miscalculation can be spotted in
        // logs before this calculator ever drives real wake timing.
        Debug.WriteLine($"[NextTransitionCalculator] next transition: {description}");
        Trace.WriteLine($"[NextTransitionCalculator] next transition: {description}");

        return earliest;
    }

    // Each window contributes two edges per matching day: its start and its end.
    // Windows that cross midnight already split correctly inside
    // ScheduleWindow.GetEdgeInstants - the same arithmetic ScheduleWindow.Contains
    // runs when enforcement decides whether a moment inside the schedule. Both
    // consumers share that one implementation; none was rewritten here.
    private static IEnumerable<DateTimeOffset> EnumerateEdges(ScheduleWindow window,
        DateTimeOffset now)
    {
        return window.GetEdgeInstants(now);
    }

    private static IEnumerable<ScheduledRule>
        EnumerateScheduledRules(ConfigurationDocument configuration)
    {
        foreach (var rule in configuration.Applications) yield return rule;
        foreach (var rule in configuration.Websites) yield return rule;
        foreach (var rule in configuration.GoogleAccounts) yield return rule;
    }
}
