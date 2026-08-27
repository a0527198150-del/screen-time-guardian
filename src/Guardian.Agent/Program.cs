namespace ScreenTimeGuardian.Agent;

public static class Program
{
    public static void Main(string[] args)
    {
        var message = ParseMessageArgument(args);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastNotifier.Show(message);
    }

    private static string? ParseMessageArgument(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--message", StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
