using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Looks ahead and finds the moments where something starts being blocked, so the
/// user-session agent can warn before it happens rather than after.
///
/// It steps forward one minute at a time and records every transition from
/// "not blocked" to "blocked" for each individual item.
/// </summary>
public static class UpcomingCalculator
{
    private static readonly TimeSpan Horizon = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(1);

    public static List<UpcomingEvent> Calculate(ConfigurationDocument configuration, DateTimeOffset now)
    {
        var engine = new PolicyEngine();
        var events = new List<UpcomingEvent>();

        var currentlyBlocked = Describe(engine.Evaluate(configuration, now));
        var seen = new HashSet<string>(currentlyBlocked, StringComparer.OrdinalIgnoreCase);

        for (var moment = now + Step; moment <= now + Horizon; moment += Step)
        {
            var blocked = Describe(engine.Evaluate(configuration, moment));

            foreach (var item in blocked)
            {
                if (seen.Add(item))
                {
                    events.Add(new UpcomingEvent
                    {
                        Title = item,
                        StartsAtUtc = moment.ToUniversalTime(),
                        IsBlockStarting = true
                    });
                }
            }
        }

        return events;
    }

    private static List<string> Describe(PolicySnapshot snapshot)
    {
        var items = new List<string>();

        items.AddRange(snapshot.NetworkBlocks.Select(block =>
            string.IsNullOrWhiteSpace(block.DisplayName)
                ? Path.GetFileNameWithoutExtension(block.ExecutablePath)
                : block.DisplayName));

        items.AddRange(snapshot.BlockedDomains);

        items.AddRange(snapshot.GoogleAccounts.SelectMany(account =>
            account.Services.Select(service => $"{service} ({account.Email})")));

        return items.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
