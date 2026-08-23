using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenTimeGuardian.Contracts;
using ScreenTimeGuardian.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Screen Time Guardian";
});

builder.Services.AddSingleton<ConfigurationStore>();
builder.Services.AddSingleton<PolicyEngine>();
builder.Services.AddSingleton<SafetyEnvelope>();
builder.Services.AddSingleton<ServiceStatusHolder>();
builder.Services.AddSingleton<ApplicationNetworkBlocker>();
builder.Services.AddSingleton<WindowsFirewallFqdnBlocker>();
builder.Services.AddSingleton<BrowserLaunchBlocker>();
builder.Services.AddSingleton<HiddenBrowserScanner>();
builder.Services.AddSingleton<ChangeCoordinator>();
builder.Services.AddSingleton<BrowserPolicySynchronizer>();
builder.Services.AddHostedService<GuardianWorker>();
builder.Services.AddHostedService<UpdateCoordinator>();
builder.Services.AddHostedService<GuardianCommandServer>();

await builder.Build().RunAsync();
