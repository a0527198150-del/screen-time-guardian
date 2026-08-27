namespace ScreenTimeGuardian.Service;

/// <summary>
/// The shared early-wake channel between everyone who wants the policy loop to run
/// now (command-server saves, power transitions, clock changes, session switches)
/// and GuardianWorker, which waits on it between cycles.
///
/// A cancellation token is single-use: once cancelled it stays cancelled forever, so
/// the holder swaps in a fresh source atomically on every request. A caller that
/// captured the CURRENT token and arrives late to wait on it observes an already
/// cancelled token - Task.Delay completes immediately, which is exactly "run now".
///
/// Cancellation of a source nobody waits on anymore is harmless.
/// </summary>
public sealed class ServiceWakeSignal
{
    private readonly object _sync = new();
    private CancellationTokenSource _cts = new();

    /// <summary>The live token for the NEXT sleep. Read once per loop iteration.</summary>
    public CancellationToken Current => Volatile.Read(ref _cts).Token;

    /// <summary>Diagnostic hook for tests and for proving triggers actually fire.</summary>
    public event Action<string>? Requested;

    /// <summary>Cancel the current sleep and re-arm a fresh source, atomically.</summary>
    public void Request(string reason)
    {
        CancellationTokenSource spent;
        lock (_sync)
        {
            spent = _cts;
            _cts = new CancellationTokenSource();
            var handler = Requested;
            if (handler is not null)
            {
                try
                {
                    handler(reason);
                }
                catch
                {
                    // Listeners must never break waking.
                }
            }
        }

        try
        {
            spent.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Raced with nothing - kept for safety with exotic callers only.
        }
    }
}
