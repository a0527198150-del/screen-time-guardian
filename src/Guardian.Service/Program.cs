using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenTimeGuardian.Contracts;
using ScreenTimeGuardian.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Screen Time Guardian";
});

// Pin the event log source name. Without this, .NET derives it from the assembly
// name, so a rename can split diagnostics across two Application log sources.
builder.Logging.AddEventLog(new EventLogSettings
{
    SourceName = "Screen Time Guardian",
    LogName = "Application"
});

// Secure the shared directory before any hosted service can load configuration.
DataDirectoryHardening.Apply(NullLogger.Instance);
builder.Services.AddSingleton<ConfigurationStore>();
builder.Services.AddSingleton<PolicyEngine>();
builder.Services.AddSingleton<SafetyEnvelope>();
builder.Services.AddSingleton<ServiceStatusHolder>();
builder.Services.AddSingleton<ApplicationNetworkBlocker>();
builder.Services.AddSingleton<WindowsFirewallFqdnBlocker>();
builder.Services.AddSingleton<NetworkProtectionEnforcer>();
builder.Services.AddSingleton<BrowserLaunchBlocker>();
builder.Services.AddSingleton<HiddenBrowserScanner>();
builder.Services.AddSingleton<ChangeCoordinator>();
builder.Services.AddSingleton<BrowserPolicySynchronizer>();
builder.Services.AddSingleton<ServiceLock>();
builder.Services.AddSingleton<ServiceWakeSignal>();
builder.Services.AddSingleton<NotificationScheduler>();
builder.Services.AddSingleton<MaintenanceWindow>();
builder.Services.AddHostedService<GuardianWorker>();
builder.Services.AddHostedService<UpdateCoordinator>();
builder.Services.AddHostedService<GuardianCommandServer>();

await builder.Build().RunAsync();
