using System.Windows;
using System.Windows.Threading;

namespace ScreenTimeGuardian.Agent;

/// <summary>
/// Runs inside each interactive user session, started at logon.
///
/// It exists because a Windows service lives in session 0 and cannot show anything
/// to a logged-in user. Notifications, countdowns and "this was just blocked" toasts
/// all have to come from here.
///
/// It has no enforcement powers of its own. It reads upcoming events from the service
/// and displays them. If the service is unreachable it simply shows nothing.
/// </summary>
public partial class App : Application
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly UpcomingWatcher _watcher = new();

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        try
        {
            var warnings = await _watcher.CollectWarningsAsync();
            foreach (var warning in warnings)
            {
                NotificationWindow.ShowWarning(warning);
            }
        }
        catch (Exception)
        {
            // The agent must never crash a user session over a failed poll.
        }
    }
}
