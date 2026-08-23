using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

/// <summary>
/// Implements the cooling off rule.
///
/// Tightening changes apply at once - the software never stands between you and a
/// stricter setting. Loosening changes are parked until the delay elapses.
///
/// When the delay is zero, every change applies immediately and this class does
/// nothing but record what happened. The mechanism is present and can be armed later
/// by raising the delay, which is itself a change that applies immediately.
/// </summary>
public sealed class ChangeCoordinator
{
    private readonly ConfigurationStore _store;
    private readonly ILogger<ChangeCoordinator> _logger;

    public ChangeCoordinator(ConfigurationStore store, ILogger<ChangeCoordinator> logger)
    {
        _store = store;
        _logger = logger;
    }

    public sealed record SubmitResult(bool AppliedImmediately, string Message, PendingChange? Pending);

    public SubmitResult Submit(ConfigurationDocument current, ConfigurationDocument proposed, DateTimeOffset now)
    {
        // The security block is never carried across from a client payload.
        proposed.Security = current.Security;
        ConfigurationMigrator.Sanitize(proposed);

        var direction = RestrictionComparer.Compare(current, proposed, now);
        var description = RestrictionComparer.Describe(current, proposed, now);
        var delayHours = Math.Max(0, current.ChangeControl.CoolingOffHours);

        if (direction == ChangeDirection.Tightening)
        {
            // Any queued relaxation is discarded: you have just decided the other way.
            proposed.PendingChange = null;
            _store.Save(proposed);
            _logger.LogInformation("Tightening change applied immediately: {Description}", description);
            return new SubmitResult(true, $"השינוי הוחל מיד. {description}", null);
        }

        if (delayHours == 0)
        {
            proposed.PendingChange = null;
            _store.Save(proposed);
            _logger.LogInformation("Loosening change applied immediately (cooling off is zero): {Description}", description);
            return new SubmitResult(true, $"השינוי הוחל מיד. {description}", null);
        }

        var pending = new PendingChange
        {
            RequestedAtUtc = now.ToUniversalTime(),
            EffectiveAtUtc = now.ToUniversalTime().AddHours(delayHours),
            Summary = description,
            PayloadJson = JsonSerializer.Serialize(proposed, ConfigurationStore.JsonOptions)
        };

        // Only the pending record is stored. The live configuration is untouched.
        current.PendingChange = pending;
        _store.Save(current);

        _logger.LogWarning(
            "Loosening change queued until {EffectiveAt}: {Description}",
            pending.EffectiveAtUtc.ToLocalTime(),
            description);

        return new SubmitResult(
            false,
            $"השינוי מקל, ולכן הוא ממתין. ייכנס לתוקף ב־{pending.EffectiveAtUtc.ToLocalTime():dd/MM HH:mm}. {description}",
            pending);
    }

    /// <summary>Called on every policy cycle. Installs a queued change once its time arrives.</summary>
    public bool ApplyDueChange(ConfigurationDocument configuration, DateTimeOffset now)
    {
        var pending = configuration.PendingChange;
        if (pending is null || !pending.IsDue(now))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ConfigurationDocument>(
                pending.PayloadJson, ConfigurationStore.JsonOptions);

            if (payload is null)
            {
                _logger.LogError("Queued change {Id} could not be read; discarding it", pending.Id);
                configuration.PendingChange = null;
                _store.Save(configuration);
                return false;
            }

            payload.Security = configuration.Security;
            payload.PendingChange = null;
            _store.Save(payload);

            _logger.LogWarning("Queued change applied after cooling off: {Summary}", pending.Summary);
            return true;
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Queued change {Id} was malformed; discarding it", pending.Id);
            configuration.PendingChange = null;
            _store.Save(configuration);
            return false;
        }
    }

    /// <summary>Cancelling a queued relaxation keeps the stricter setting, so it is always allowed.</summary>
    public void CancelPending(ConfigurationDocument configuration)
    {
        if (configuration.PendingChange is null)
        {
            return;
        }

        _logger.LogInformation("Queued change cancelled: {Summary}", configuration.PendingChange.Summary);
        configuration.PendingChange = null;
        _store.Save(configuration);
    }
}
