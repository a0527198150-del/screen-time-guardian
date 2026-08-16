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
builder.Services.AddSingleton<WindowsFirewallFqdnBlocker>();
builder.Services.AddSingleton<ProcessBlocker>();
builder.Services.AddSingleton<BrowserProcessDetector>();
builder.Services.AddSingleton<PortableBrowserEnforcer>();
builder.Services.AddSingleton<UpdateCoordinator>();
builder.Services.AddSingleton<BrowserPolicySynchronizer>();
builder.Services.AddHostedService<GuardianWorker>();
builder.Services.AddHostedService<GuardianCommandServer>();

await builder.Build().RunAsync();
