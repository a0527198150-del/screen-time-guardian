using System.Windows;
using System.Windows.Threading;

namespace ScreenTimeGuardian.Agent;

/// <summary>
/// A countdown toast in the bottom corner of the screen.
///
/// Deliberately not modal and not focus stealing: it must not interrupt whatever the
/// user is typing. It closes itself after the countdown reaches zero, or forty seconds,
/// whichever comes first.
/// </summary>
public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _deadline;

    private NotificationWindow()
    {
        InitializeComponent();
    }

    public static void ShowWarning(Warning warning)
    {
        var window = new NotificationWindow
        {
            _deadline = DateTimeOffset.UtcNow + warning.Remaining
        };

        window.TitleText.Text = warning.Title;
        window.DetailText.Text = warning.Detail;
        window.UpdateCountdown();

        window.Loaded += (_, _) => window.PositionBottomCorner();
        window._timer.Tick += (_, _) => window.UpdateCountdown();
        window._timer.Start();

        // Never steal focus from what the user is doing.
        window.ShowActivated = false;
        window.Show();

        var autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
        autoClose.Tick += (_, _) =>
        {
            autoClose.Stop();
            window.Close();
        };
        autoClose.Start();
    }

    private void PositionBottomCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Bottom - ActualHeight - 24;
    }

    private void UpdateCountdown()
    {
        var remaining = _deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            CountdownText.Text = "החסימה החלה";
            return;
        }

        CountdownText.Text = remaining.TotalMinutes >= 1
            ? $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}"
            : $"{remaining.Seconds} שניות";
    }

    private void DismissButton_OnClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}
