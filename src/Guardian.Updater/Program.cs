namespace ScreenTimeGuardian.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = UpdateArguments.Parse(args);
            new UpdateInstaller().Apply(options);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
