using Microsoft.Toolkit.Uwp.Notifications;

namespace ScreenTimeGuardian.Agent;

public static class ToastNotifier
{
    private const string AppId = "ScreenTimeGuardian.Agent";

    public static void Show(string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("שומר זמן מסך")
                .AddText(message)
                .Show(toast => toast.ExpirationTime = DateTimeOffset.UtcNow.AddMinutes(2));
        }
        catch
        {
            // Notifications must never crash the scheduler-launched process.
        }
    }
}
