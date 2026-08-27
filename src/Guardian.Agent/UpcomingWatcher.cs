namespace ScreenTimeGuardian.Agent;

/// <summary>
/// Retained as a compatibility type for older project references. Notifications are
/// now delivered by the one-shot Program entry point launched by Task Scheduler;
/// this agent does not poll, own a pipe, or remain resident.
/// </summary>
public sealed class UpcomingWatcher
{
    public Task<IReadOnlyList<Warning>> CollectWarningsAsync()
        => Task.FromResult<IReadOnlyList<Warning>>(Array.Empty<Warning>());
}

public sealed record Warning(string Title, string Detail, TimeSpan Remaining);
