using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class GuardianWorker : BackgroundService
{
    // Policy changes are intentionally sampled every 15 seconds. Activation delays
    // shorter than this are not promised to be sub-second precise.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly ConfigurationStore _store;
    private readonly PolicyEngine _engine;
    private readonly SafetyEnvelope _safety;
    private readonly ApplicationNetworkBlocker _networkBlocker;
    private readonly WindowsFirewallFqdnBlocker _websiteBlocker;
    private readonly BrowserLaunchBlocker _launchBlocker;
    private readonly HiddenBrowserScanner _browserScanner;
    private readonly ChangeCoordinator _changes;
    private readonly BrowserPolicySynchronizer _browserPolicies;
    private readonly ServiceStatusHolder _status;
    private readonly ILogger<GuardianWorker> _logger;

    private string _lastReason = string.Empty;
    private string _lastPolicyDescription = string.Empty;

    public GuardianWorker(
        ConfigurationStore store,
        PolicyEngine engine,
        SafetyEnvelope safety,
        ApplicationNetworkBlocker networkBlocker,
        WindowsFirewallFqdnBlocker websiteBlocker,
        BrowserLaunchBlocker launchBlocker,
        HiddenBrowserScanner browserScanner,
        ChangeCoordinator changes,
        BrowserPolicySynchronizer browserPolicies,
        ServiceStatusHolder status,
        ILogger<GuardianWorker> logger)
    {
        _store = store;
        _engine = engine;
        _safety = safety;
        _networkBlocker = networkBlocker;
        _websiteBlocker = websiteBlocker;
        _launchBlocker = launchBlocker;
        _browserScanner = browserScanner;
        _changes = changes;
        _browserPolicies = browserPolicies;
        _status = status;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _safety.Initialize();
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

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await ShutdownCleanlyAsync();
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var configuration = _store.Load();

        // A queued relaxation whose delay has elapsed is installed before anything is evaluated.
        if (_changes.ApplyDueChange(configuration, DateTimeOffset.Now))
        {
            configuration = _store.Load();
        }

        var safety = _safety.Evaluate(configuration.Safety);

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

        await _networkBlocker.ApplyAsync(
            allNetworkBlocks,
            configuration.Safety,
            safety.EnforcementAllowed,
            cancellationToken);

        var websiteMode = safety.EnforcementAllowed && configuration.AllowMachineWideWebsiteBlocking
            ? configuration.WebsiteEnforcement
            : WebsiteEnforcementMode.Disabled;
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

    private async Task ShutdownCleanlyAsync()
    {
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
    public const string Version = "0.4.7";

    private GuardianStatus _status = new() { Version = Version };

    public void Update(GuardianStatus status) => _status = status;

    public GuardianStatus Current => _status;
}
