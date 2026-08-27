using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class GuardianWorker : BackgroundService
{
    // Maximum time to stay asleep even when the next scheduled transition is far
    // away. This is the resilience heartbeat: it is what notices a firewall rule
    // someone deleted by hand, or a policy value that was overwritten. Without it
    // the service could sleep for hours while its rules quietly stopped existing.
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(5);

    // Never sleep less than this. Protects against a burst of wakeups when several
    // transitions land within the same few seconds.
    private static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(2);

    private readonly ConfigurationStore _store;
    private readonly PolicyEngine _engine;
    private readonly SafetyEnvelope _safety;
    private readonly ApplicationNetworkBlocker _networkBlocker;
    private readonly WindowsFirewallFqdnBlocker _websiteBlocker;
    private readonly NetworkProtectionEnforcer _networkProtection;
    private readonly BrowserLaunchBlocker _launchBlocker;
    private readonly HiddenBrowserScanner _browserScanner;
    private readonly ChangeCoordinator _changes;
    private readonly BrowserPolicySynchronizer _browserPolicies;
    private readonly ServiceStatusHolder _status;
    private readonly ServiceLock _serviceLock;
    private readonly MaintenanceWindow _maintenance;
    private readonly ServiceWakeSignal _wakeSignal;
    private readonly NotificationScheduler _notifications;
    private readonly ILogger<GuardianWorker> _logger;

    private string _lastReason = string.Empty;
    private string _lastPolicyDescription = string.Empty;

    // Cheap-versus-expensive bookkeeping. Full enforcement runs when any of these
    // changes; otherwise a cycle only refreshes status after microsecond probes.
    private ConfigurationDocument? _lastConfiguration;
    private string _lastConfigurationJson = string.Empty;
    private bool _lastAppliedEnforcementAllowed;
    private int? _lastObservedFirewallRules;
    private DateTimeOffset _nextMandatoryFullUtc = DateTimeOffset.MinValue;
    private int _consecutiveApplyFailures;
    private bool _lastWasFullCycle;


    public GuardianWorker(
        ConfigurationStore store,
        PolicyEngine engine,
        SafetyEnvelope safety,
        ApplicationNetworkBlocker networkBlocker,
        WindowsFirewallFqdnBlocker websiteBlocker,
        NetworkProtectionEnforcer networkProtection,
        BrowserLaunchBlocker launchBlocker,
        HiddenBrowserScanner browserScanner,
        ChangeCoordinator changes,
        BrowserPolicySynchronizer browserPolicies,
        ServiceStatusHolder status,
        ServiceLock serviceLock,
        MaintenanceWindow maintenance,
        ServiceWakeSignal wakeSignal,
        NotificationScheduler notifications,
        ILogger<GuardianWorker> logger)
    {
        _store = store;
        _engine = engine;
        _safety = safety;
        _networkBlocker = networkBlocker;
        _websiteBlocker = websiteBlocker;
        _networkProtection = networkProtection;
        _launchBlocker = launchBlocker;
        _browserScanner = browserScanner;
        _changes = changes;
        _browserPolicies = browserPolicies;
        _status = status;
        _serviceLock = serviceLock;
        _maintenance = maintenance;
        _wakeSignal = wakeSignal;
        _notifications = notifications;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dataDirectorySecured = DataDirectoryHardening.Apply(_logger);
        _safety.Initialize();
        if (!dataDirectorySecured)
        {
            _safety.TripSafeMode("לא ניתן לאבטח את תיקיית הנתונים. האכיפה נשארת מושבתת.");
        }
        RegisterWakeTriggers();
        _logger.LogInformation("Screen Time Guardian service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Policy cycle failed");
            }

            var delay = CalculateSleep();
            // The wake signal is cancelled by config saves, resume from sleep, and
            // clock changes. Linking it to stoppingToken means either one ends the
            // wait; a fresh source is swapped in atomically on every early request.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, _wakeSignal.Current);
            try
            {
                await Task.Delay(delay, linked.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Woken early on purpose. The signal already holds a fresh token for
                // the next sleep, so nothing to reset here - run the cycle now.
                _logger.LogDebug("Early wake consumed; running an immediate cycle");
            }
        }

        _notifications.DeleteTask();
        await ShutdownCleanlyAsync();
    }

    /// <summary>
    /// Wall-clock events that must end a sleep immediately. SystemEvents owns its own
    /// hidden message-pump thread, so these broadcasts are delivered inside the
    /// service host; every delivery logs its reason, which is how firing is verified
    /// on a real machine during acceptance testing.
    /// </summary>
    private void RegisterWakeTriggers()
    {
        try
        {
            // Resume from sleep or hibernate. Timers measure awake time only, so a
            // transition that passed while the machine was suspended would otherwise
            // be missed until the remaining sleep ran out. This is the single most
            // likely source of "the block did not start on time" reports.
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == PowerModes.Resume)
                {
                    _wakeSignal.Request("resume from sleep");
                }
            };

            // Manual clock change or daylight saving transition.
            SystemEvents.TimeChanged += (_, _) => _wakeSignal.Request("system clock changed");

            // A user logging on or switching accounts changes whose session the
            // per-user policy artefacts apply to.
            SystemEvents.SessionSwitch += (_, _) => _wakeSignal.Request("session switch");

            _logger.LogInformation("System event wake triggers registered");
        }
        catch (Exception exception)
        {
            // Broadcasts unavailable is survivable: the resilience heartbeat still
            // bounds every late response at MaxSleep. Never let registration throw.
            _logger.LogWarning(exception,
                "System event triggers could not be registered; heartbeat-only waking");
        }
    }

    private TimeSpan CalculateSleep()
    {
        var now = DateTimeOffset.Now;
        var configuration = _lastConfiguration;
        var next = configuration is null
            ? null
            : NextTransitionCalculator.Calculate(configuration, now);

        var untilTransition = next is null
            ? MaxSleep
            : next.Value - now;

        // Grace periods end at known instants too - enforcement may only begin once
        // they elapse, so sleeping straight through their end would start blocks late.
        var systemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (configuration is not null && systemUptime < TimeSpan.FromSeconds(configuration.Safety.BootGraceSeconds))
        {
            untilTransition = Min(TimeSpan.FromSeconds(configuration.Safety.BootGraceSeconds) - systemUptime, untilTransition);
        }

        var serviceUptime = DateTimeOffset.UtcNow - _safety.ServiceStartedUtc;
        if (configuration is not null && serviceUptime < TimeSpan.FromSeconds(configuration.Safety.ServiceGraceSeconds))
        {
            untilTransition = Min(TimeSpan.FromSeconds(configuration.Safety.ServiceGraceSeconds) - serviceUptime, untilTransition);
        }

        // Land a second early rather than a second late: a block that starts at
        // 23:00 and is applied at 23:00:14 is a fourteen second hole in the schedule.
        untilTransition -= TimeSpan.FromSeconds(1);
        var chosen = untilTransition < MaxSleep ? untilTransition : MaxSleep;
        return chosen < MinSleep ? MinSleep : chosen;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var configuration = _store.Load();

        // A queued relaxation whose delay has elapsed is installed before anything is evaluated.
        if (_changes.ApplyDueChange(configuration, DateTimeOffset.Now))
        {
            configuration = _store.Load();
        }

        var configurationJson = JsonSerializer.Serialize(configuration, ConfigurationStore.JsonOptions);
        var configurationChanged = !string.Equals(
            configurationJson, _lastConfigurationJson, StringComparison.Ordinal);
        if (configurationChanged)
        {
            _notifications.Refresh(configuration);
        }

        // Enforce or release the service and folder locks every cycle.
        var maintenanceOpen = _maintenance.IsOpen();
        _serviceLock.Apply(locked: !maintenanceOpen);
        if (maintenanceOpen)
        {
            _serviceLock.UnlockInstallFolder();
        }
        else
        {
            _serviceLock.LockInstallFolder();
        }

        var safety = _safety.Evaluate(configuration.Safety);
        var firewallRuleCount = ResilienceProbe.CountGuardianFirewallRules();
        var safetyChanged = configurationChanged
            || safety.EnforcementAllowed != _lastAppliedEnforcementAllowed;
        var backoffElapsed = DateTimeOffset.UtcNow >= _nextMandatoryFullUtc;
        var fullCycleRequired = configurationChanged || safetyChanged || backoffElapsed || !_lastWasFullCycle;

        if (safety.Reason != _lastReason)
        {
            if (safety.EnforcementAllowed)
            {
                _logger.LogInformation("Safety state: {Reason}", safety.Reason);
            }
            else
            {
                _logger.LogWarning("Enforcement disabled: {Reason}", safety.Reason);
            }

            _lastReason = safety.Reason;
        }

        var snapshot = _engine.Evaluate(configuration, DateTimeOffset.Now);

        var expectedFirewallRules = snapshot.NetworkBlocks.Count;
        var firewallDrifted = firewallRuleCount is null || firewallRuleCount != expectedFirewallRules;
        fullCycleRequired = fullCycleRequired || firewallDrifted;
        if (!fullCycleRequired)
        {
            // Cheap resilience cycle: configuration untouched, safety posture
            // unchanged, microsecond registry probes say the firewall artefacts are
            // still exactly what the last full cycle left behind. Nothing expensive
            // runs - not PowerShell, not the scanner, not the blockers.
            PublishStatusAndLog(snapshot, safety);
            _lastWasFullCycle = false;
            return;
        }

        // Unapproved browsers found on disk are blocked around the clock, not on a
        // schedule: an escape hatch is an escape hatch at any hour.
        var hiddenBrowsers = _browserScanner.Scan(
            configuration.BrowserLockdown,
            safety.EnforcementAllowed,
            configuration.EnforceForAdministrators);
        var allNetworkBlocks = snapshot.NetworkBlocks.Concat(hiddenBrowsers).ToList();

        _launchBlocker.Apply(
            configuration.BrowserLockdown,
            configuration.Safety,
            safety.EnforcementAllowed,
            configuration.EnforceForAdministrators);

        try
        {
            await _networkBlocker.ApplyAsync(
                allNetworkBlocks,
                configuration.Safety,
                safety.EnforcementAllowed,
                cancellationToken);
            _consecutiveApplyFailures = 0;
            _nextMandatoryFullUtc = DateTimeOffset.UtcNow;
        }
        catch
        {
            _consecutiveApplyFailures++;
            _nextMandatoryFullUtc = DateTimeOffset.UtcNow + BackoffFor(_consecutiveApplyFailures);
            if (_consecutiveApplyFailures == 1 || (_consecutiveApplyFailures & (_consecutiveApplyFailures - 1)) == 0)
            {
                _logger.LogWarning("Policy application failed {Count} consecutive time(s); retry backoff is {Delay}",
                    _consecutiveApplyFailures, BackoffFor(_consecutiveApplyFailures));
            }
            throw;
        }

        var websiteMode = safety.EnforcementAllowed && configuration.AllowMachineWideWebsiteBlocking
            ? configuration.WebsiteEnforcement
            : WebsiteEnforcementMode.Disabled;
        if (websiteMode == WebsiteEnforcementMode.Enforced && !_networkProtection.EnsureEnabled())
        {
            websiteMode = WebsiteEnforcementMode.Disabled;
        }
        if (websiteMode != WebsiteEnforcementMode.Enforced && snapshot.BlockedDomains.Count > 0)
        {
            _logger.LogWarning(
                "{Count} domains are scheduled for blocking but website enforcement is off. " +
                "EnforcementAllowed={Allowed}, AllowMachineWideWebsiteBlocking={Allowed2}, WebsiteEnforcement={Mode}",
                snapshot.BlockedDomains.Count,
                safety.EnforcementAllowed,
                configuration.AllowMachineWideWebsiteBlocking,
                configuration.WebsiteEnforcement);
        }

        await _websiteBlocker.ApplyAsync(
            websiteMode,
            snapshot.BlockedDomains,
            configuration.Safety,
            cancellationToken);

        // Incognito and guest mode follow the schedule: closed while a block runs,
        // open the rest of the time.
        _browserPolicies.ApplyPrivateBrowsingPolicy(
            snapshot.IsAnyBlockActive,
            safety.EnforcementAllowed,
            configuration.EnforceForAdministrators);

        _lastConfiguration = ConfigurationStore.Clone(configuration);
        _lastConfigurationJson = configurationJson;
        _lastAppliedEnforcementAllowed = safety.EnforcementAllowed;
        _lastObservedFirewallRules = firewallRuleCount;
        _lastWasFullCycle = true;

        _status.Update(new GuardianStatus
        {
            EnforcementActive = safety.EnforcementAllowed,
            SafeMode = safety.SafeMode,
            Reason = safety.Reason,
            ActiveNetworkBlocks = _networkBlocker.ActiveRuleCount,
            BlockedBrowserLaunches = _launchBlocker.ActiveDenyCount,
            HiddenBrowsersFound = _browserScanner.LastFoundCount,
            PendingChangeSummary = configuration.PendingChange?.ToString() ?? string.Empty,
            ServiceStartedUtc = _safety.ServiceStartedUtc,
            Version = ServiceStatusHolder.Version
        });

        var description = string.Join("|", snapshot.ActiveRuleIds);
        if (description != _lastPolicyDescription)
        {
            _logger.LogInformation(
                "Policy changed. Active rules: {Count}; network blocks: {Blocks}; domains: {Domains}",
                snapshot.ActiveRuleIds.Count,
                snapshot.NetworkBlocks.Count,
                snapshot.BlockedDomains.Count);
            _lastPolicyDescription = description;
        }
    }

    private static TimeSpan BackoffFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;
        var minutes = Math.Min(30, Math.Pow(2, Math.Min(consecutiveFailures - 1, 5)));
        return TimeSpan.FromMinutes(minutes);
    }

    private void PublishStatusAndLog(PolicySnapshot snapshot, SafetyState safety)
    {
        _status.Update(new GuardianStatus
        {
            EnforcementActive = safety.EnforcementAllowed,
            SafeMode = safety.SafeMode,
            Reason = safety.Reason,
            ActiveNetworkBlocks = _networkBlocker.ActiveRuleCount,
            BlockedBrowserLaunches = _launchBlocker.ActiveDenyCount,
            HiddenBrowsersFound = _browserScanner.LastFoundCount,
            ServiceStartedUtc = _safety.ServiceStartedUtc,
            Version = ServiceStatusHolder.Version
        });
    }

    private async Task ShutdownCleanlyAsync()
    {
        try
        {
            // A cleanly stopped service must not leave locks in place.
            _serviceLock.Apply(locked: false);
            _serviceLock.UnlockInstallFolder();
            _maintenance.Close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not release locks during shutdown");
        }

        try
        {
            // A stopped service must not leave the machine cut off from the internet.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _networkBlocker.RemoveAllAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove firewall rules during shutdown");
        }

        try
        {
            // Website rules are machine-wide; a cleanly stopped service must not leave
            // them behind and unexpectedly cut off the machine.
            using var websiteTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _websiteBlocker.RemoveAllAsync(websiteTimeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove website firewall rules during shutdown");
        }

        try
        {
            // Leaving IFEO entries behind would make browsers unlaunchable after the
            // service is gone. They must come out on every clean stop.
            _launchBlocker.RemoveAll();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove launch blocking entries during shutdown");
        }

        try
        {
            // A stopped service must not leave incognito disabled forever.
            _browserPolicies.RestoreDefaults();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not restore browser policy defaults during shutdown");
        }

        _safety.Shutdown();
        _logger.LogInformation("Screen Time Guardian service stopped cleanly");
    }
}

public sealed class ServiceStatusHolder
{
    public const string Version = "0.5.17";

    private GuardianStatus _status = new() { Version = Version };

    public void Update(GuardianStatus status) => _status = status;

    public GuardianStatus Current => _status;
}
