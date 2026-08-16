using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.NativeHost;

internal static class Program
{
    private static async Task Main()
    {
        var path = Environment.GetEnvironmentVariable("SCREEN_TIME_GUARDIAN_CONFIG");
        var store = new ConfigurationStore(path);
        var engine = new PolicyEngine();
        var server = new NativeMessagingServer(store, engine);
        await server.RunAsync(CancellationToken.None);
    }
}
