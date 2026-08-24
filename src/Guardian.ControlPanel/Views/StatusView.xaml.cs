using System.Windows.Controls;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class StatusView : UserControl
{
    public StatusView()
    {
        InitializeComponent();
    }

    public void Show(ConfigurationDocument configuration, GuardianStatus? status, IReadOnlyList<UpcomingEvent>? upcoming)
    {
        var safe = status?.SafeMode == true;
        EnforcementTitle.Text = safe
            ? "מצב בטוח — האכיפה מושבתת"
            : status?.EnforcementActive == true ? "אכיפה פעילה כרגע" : "האכיפה אינה פעילה כרגע";
        EnforcementReason.Text = status?.Reason ?? "לא התקבל מצב מהשירות.";
        EnforcementCard.Background = new System.Windows.Media.SolidColorBrush(
            safe ? System.Windows.Media.Color.FromRgb(250, 242, 220) : System.Windows.Media.Color.FromRgb(227, 245, 241));

        var active = configuration.Applications.Where(rule => rule.IsActive(DateTimeOffset.Now)).Select(rule => rule.Name)
            .Concat(configuration.Websites.Where(rule => rule.IsActive(DateTimeOffset.Now)).Select(rule => rule.Domain))
            .Concat(configuration.GoogleAccounts.Where(rule => rule.IsActive(DateTimeOffset.Now)).Select(rule => rule.Email))
            .Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        ActiveRulesListBox.ItemsSource = active;
        NoActiveText.Visibility = active.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        StatusWeekGrid.Windows = configuration.Applications.SelectMany(rule => rule.Windows)
            .Concat(configuration.Websites.SelectMany(rule => rule.Windows)).ToList();

        var next = upcoming?.Where(item => item.IsBlockStarting).OrderBy(item => item.StartsAtUtc).FirstOrDefault();
        UpcomingText.Text = next is null
            ? "אין אירוע מתוזמן קרוב."
            : $"{next.Title} ייחסם בעוד {FormatRemaining(next.StartsAtUtc - DateTimeOffset.UtcNow)}.";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "עכשיו";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours} שעות ו־{remaining.Minutes} דקות";
        if (remaining.TotalMinutes >= 1) return $"{(int)remaining.TotalMinutes} דקות";
        return $"{Math.Max(1, (int)remaining.TotalSeconds)} שניות";
    }
}
