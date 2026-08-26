using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class HomeView : UserControl
{
    public event EventHandler? NewRuleRequested;

    /// <summary>Raised when the operator confirms they want enforcement back on after a safe-mode trip.</summary>
    public event EventHandler? ConfirmSafeModeRequested;

    public HomeView()
    {
        InitializeComponent();
    }

    public void Show(ConfigurationDocument configuration, GuardianStatus? status,
        IReadOnlyList<UpcomingEvent>? upcoming)
    {
        var totalRules = configuration.Applications.Count
            + configuration.Websites.Count
            + configuration.GoogleAccounts.Count;

        // First-run: show welcome card, hide everything else
        if (totalRules == 0)
        {
            WelcomeCard.Visibility = Visibility.Visible;
            NormalContent.Visibility = Visibility.Collapsed;
            return;
        }

        WelcomeCard.Visibility = Visibility.Collapsed;
        NormalContent.Visibility = Visibility.Visible;

        var safe = status?.SafeMode == true;
        var active = status?.EnforcementActive == true;

        // 1. Status card — background changes based on state
        if (safe)
        {
            StatusCard.Background = new SolidColorBrush(Color.FromRgb(253, 243, 226)); // AmberSoft
            StatusTitle.Text = "האכיפה מושהית";
            StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(180, 119, 15)); // Amber
            StatusReason.Text = status?.Reason ?? "";
            ConfirmSafeModeButton.Visibility = Visibility.Visible;
        }
        else
        {
            ConfirmSafeModeButton.Visibility = Visibility.Collapsed;
        }
        else if (active)
        {
            var blockCount = configuration.Applications.Count(r => r.IsActive(DateTimeOffset.Now))
                + configuration.Websites.Count(r => r.IsActive(DateTimeOffset.Now))
                + configuration.GoogleAccounts.Count(r => r.IsActive(DateTimeOffset.Now));

            if (blockCount > 0)
            {
                StatusCard.Background = new SolidColorBrush(Color.FromRgb(224, 242, 241)); // TealSoft
                StatusTitle.Text = $"{blockCount} חסימות פעילות";
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(14, 124, 134)); // Teal
            }
            else
            {
                StatusCard.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // Surface
                StatusTitle.Text = "הכל פתוח כרגע";
                StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(20, 27, 46)); // Ink
            }
            StatusReason.Text = status?.Reason ?? "";
        }
        else
        {
            StatusCard.Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)); // Canvas
            StatusTitle.Text = "האכיפה אינה פעילה";
            StatusTitle.Foreground = new SolidColorBrush(Color.FromRgb(90, 103, 128)); // MutedInk
            StatusReason.Text = status?.Reason ?? "";
        }

        // 2. Active blocks
        var activeBlocks = new List<string>();
        activeBlocks.AddRange(
            configuration.Applications.Where(r => r.IsActive(DateTimeOffset.Now))
                .Select(r => $"📱 {r.Name}"));
        activeBlocks.AddRange(
            configuration.Websites.Where(r => r.IsActive(DateTimeOffset.Now))
                .Select(r => $"🌐 {r.Domain}"));
        activeBlocks.AddRange(
            configuration.GoogleAccounts.Where(r => r.IsActive(DateTimeOffset.Now))
                .Select(r => $"👤 {r.Email}"));

        ActiveBlocksList.ItemsSource = activeBlocks;
        NoActiveText.Visibility = activeBlocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 3. WeekGrid — all schedule windows combined, read-only
        var allWindows = configuration.Applications.SelectMany(r => r.Windows)
            .Concat(configuration.Websites.SelectMany(r => r.Windows))
            .Concat(configuration.GoogleAccounts.SelectMany(r => r.Windows))
            .ToList();
        StatusWeekGrid.Windows = allWindows;

        // 4. Upcoming
        var next = upcoming?.Where(e => e.IsBlockStarting)
            .OrderBy(e => e.StartsAtUtc).FirstOrDefault();
        UpcomingText.Text = next is null
            ? "אין אירוע מתוזמן קרוב"
            : $"🔴 {next.Title} ייחסם בעוד {FormatRemaining(next.StartsAtUtc - DateTimeOffset.UtcNow)}";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "עכשיו";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours} שעות ו־{remaining.Minutes} דקות";
        if (remaining.TotalMinutes >= 1)
            return $"{(int)remaining.TotalMinutes} דקות";
        return $"{Math.Max(1, (int)remaining.TotalSeconds)} שניות";
    }

    private void WelcomeNewRule_Click(object sender, RoutedEventArgs e)
    {
        NewRuleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ConfirmSafeMode_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSafeModeRequested?.Invoke(this, EventArgs.Empty);
    }
}
