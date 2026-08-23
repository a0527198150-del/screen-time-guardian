using System.IO.Pipes;
using System.Text;
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

    private static async Task<IReadOnlyList<UpcomingEvent>> FetchUpcomingAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", PipeNames.Control, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(connectTimeout.Token);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, true, 4096, leaveOpen: true);

            var command = new GuardianCommand { Type = "getUpcoming" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(command, JsonOptions));

            using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var json = await reader.ReadLineAsync(readTimeout.Token);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<UpcomingEvent>();
            }

            var response = JsonSerializer.Deserialize<UpcomingResponse>(json, JsonOptions);
            return response?.Upcoming ?? (IReadOnlyList<UpcomingEvent>)Array.Empty<UpcomingEvent>();
        }
        catch (Exception exception) when (exception is IOException
                                              or TimeoutException
                                              or OperationCanceledException
                                              or UnauthorizedAccessException
                                              or JsonException)
        {
            return Array.Empty<UpcomingEvent>();
        }
    }

    private sealed class UpcomingResponse
    {
        public bool Ok { get; set; }
        public List<UpcomingEvent> Upcoming { get; set; } = new();
    }
}
