using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Agent;

public sealed record Warning(string Title, string Detail, TimeSpan Remaining);

/// <summary>
/// Asks the service what is about to be blocked and turns that into warnings.
///
/// Warnings fire once each at ten minutes, five minutes and one minute before a block
/// starts. The "already warned" set is keyed by event and threshold, so a poll every
/// thirty seconds does not produce a stream of duplicate popups.
/// </summary>
public sealed class UpcomingWatcher
{
    private static readonly int[] ThresholdsMinutes = { 10, 5, 1 };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HashSet<string> _alreadyWarned = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Warning>> CollectWarningsAsync()
    {
        var events = await FetchUpcomingAsync();
        var warnings = new List<Warning>();
        var now = DateTimeOffset.UtcNow;

        foreach (var upcoming in events.Where(item => item.IsBlockStarting))
        {
            var remaining = upcoming.StartsAtUtc - now;
            if (remaining <= TimeSpan.Zero)
            {
                continue;
            }

            foreach (var threshold in ThresholdsMinutes)
            {
                var window = TimeSpan.FromMinutes(threshold);
                if (remaining > window || remaining <= window - TimeSpan.FromMinutes(1))
                {
                    continue;
                }

                var key = $"{upcoming.Title}|{upcoming.StartsAtUtc:O}|{threshold}";
                if (!_alreadyWarned.Add(key))
                {
                    continue;
                }

                warnings.Add(new Warning(
                    upcoming.Title,
                    threshold == 1
                        ? "החסימה מתחילה בעוד דקה. סיים לשמור עכשיו."
                        : $"החסימה מתחילה בעוד {threshold} דקות.",
                    remaining));
            }
        }

        // Keep the memo small; entries for past events are useless.
        if (_alreadyWarned.Count > 200)
        {
            _alreadyWarned.Clear();
        }

        return warnings;
    }

    private static Task<IReadOnlyList<UpcomingEvent>> FetchUpcomingAsync()
    {
        // Upcoming data is authenticated at the service boundary. The agent has no
        // secure credential hand-off yet, so it must not probe the pipe anonymously:
        // that would count as a failed login for the interactive user's SID.
        return Task.FromResult<IReadOnlyList<UpcomingEvent>>(Array.Empty<UpcomingEvent>());
    }
}
