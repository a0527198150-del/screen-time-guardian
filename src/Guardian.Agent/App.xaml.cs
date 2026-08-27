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
    // The scheduler starts Program.Main directly. This legacy App remains only so
    // the existing WPF resources continue to compile; it has no startup polling.
    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }
}
