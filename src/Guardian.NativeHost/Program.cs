using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.NativeHost;

internal static class Program
{
    private static async Task Main()
    {
        // Native messaging is launched by the browser, so never let its environment
        // choose the configuration file. The service-owned default path is authoritative.
        var store = new ConfigurationStore();
        var engine = new PolicyEngine();
        var server = new NativeMessagingServer(store, engine);
        await server.RunAsync(CancellationToken.None);
    }
}
