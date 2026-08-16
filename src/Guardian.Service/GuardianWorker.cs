using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.Service;

public sealed class GuardianWorker : BackgroundService
{
    private readonly ConfigurationStore _store;
    private readonly PolicyEngine _engine;
    private readonly WindowsFirewallFqdnBlocker _websiteBlocker;
    private readonly ProcessBlocker _processBlocker;
    private readonly PortableBrowserEnforcer _portableBrowserEnforcer;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly BrowserPolicySynchronizer _browserPolicies;
    private readonly ILogger<GuardianWorker> _logger;
    private PolicySnapshot? _lastSnapshot;

    public GuardianWorker(
        ConfigurationStore store,
        PolicyEngine engine,
        WindowsFirewallFqdnBlocker websiteBlocker,
        ProcessBlocker processBlocker,
        PortableBrowserEnforcer portableBrowserEnforcer,
        UpdateCoordinator updateCoordinator,
        BrowserPolicySynchronizer browserPolicies,
        ILogger<GuardianWorker> logger)
    {
        _store = store;
        _engine = engine;
        _websiteBlocker = websiteBlocker;
        _processBlocker = processBlocker;
        _portableBrowserEnforcer = portableBrowserEnforcer;
        _updateCoordinator = updateCoordinator;
        _browserPolicies = browserPolicies;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Screen Time Guardian service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var configuration = _store.Load();
                var snapshot = _engine.Evaluate(configuration, DateTimeOffset.Now);

                await _websiteBlocker.ApplyAsync(
                    configuration.WebsiteEnforcement,
                    snapshot.BlockedDomains,
                    stoppingToken);

                await _processBlocker.ApplyAsync(snapshot.BlockedApplications, stoppingToken);
                await _portableBrowserEnforcer.ApplyAsync(
                    snapshot.BlockPortableBrowsers,
                    configuration.ApprovedBrowsers,
                    configuration.StrictPortableApplicationMode,
                    stoppingToken);

                await _updateCoordinator.CheckAsync(configuration, stoppingToken);

                var policyChanged = _browserPolicies.ApplyGuestModePolicy(snapshot.GuestModeAllowed);
                if (policyChanged)
                {
                    _logger.LogWarning(
                        "Browser Guest policy changed. Existing browser processes may need to restart for the policy to apply.");
                }

                if (!PolicyEquivalent(_lastSnapshot, snapshot))
                {
                    _logger.LogInformation(
                        "Policy changed. Active rules: {Rules}; blocked domains: {Domains}",
                        string.Join(",", snapshot.ActiveRuleIds),
                        string.Join(",", snapshot.BlockedDomains));
                    _lastSnapshot = snapshot;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Could not apply the current policy");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected policy loop failure");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private static bool PolicyEquivalent(PolicySnapshot? left, PolicySnapshot right)
    {
        if (left is null)
        {
            return false;
        }

        return left.IsAnyBlockActive == right.IsAnyBlockActive
            && left.BlockAllWebsites == right.BlockAllWebsites
            && left.GuestModeAllowed == right.GuestModeAllowed
            && left.BlockPortableBrowsers == right.BlockPortableBrowsers
            && left.BlockedDomains.SequenceEqual(right.BlockedDomains, StringComparer.OrdinalIgnoreCase)
            && left.BlockedApplications.SequenceEqual(right.BlockedApplications, StringComparer.OrdinalIgnoreCase)
            && left.ActiveRuleIds.SequenceEqual(right.ActiveRuleIds, StringComparer.OrdinalIgnoreCase);
    }
}
