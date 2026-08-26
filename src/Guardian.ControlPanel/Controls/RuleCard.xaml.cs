using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class RuleCard : UserControl
{
    private ScheduledRule? _rule;
    private bool _suppressEvents;

    public event EventHandler? EditRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? ToggleChanged;

    public RuleCard()
    {
        InitializeComponent();
        CardBorder.MouseEnter += (_, _) => ActionButtons.Visibility = Visibility.Visible;
        CardBorder.MouseLeave += (_, _) => ActionButtons.Visibility = Visibility.Collapsed;
        CardBorder.MouseLeftButtonDown += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Bind(ScheduledRule rule)
    {
        _rule = rule;
        _suppressEvents = true;

        RuleName.Text = string.IsNullOrWhiteSpace(rule.Name) ? "כלל ללא שם" : rule.Name;

        // Type icon
        TypeIcon.Text = rule switch
        {
            ApplicationRule => "📱",
            WebsiteRule => "🌐",
            GoogleAccountRule => "👤",
            _ => "📋"
        };

        // Technical identifier
        RuleIdentifier.Text = rule switch
        {
            WebsiteRule w => w.Domain,
            GoogleAccountRule a => a.Email,
            ApplicationRule app => string.Join(", ",
                app.Targets.Take(3).Select(t => t.DisplayName)),
            _ => ""
        };

        // What exactly is blocked (account rules)
        BlockScope.Visibility = Visibility.Collapsed;
        if (rule is GoogleAccountRule account)
        {
            var labels = account.Services
                .Where(key => GoogleServices.Names.ContainsKey(key))
                .Select(GoogleServices.Label)
                .ToList();
            if (labels.Count > 0)
            {
                BlockScope.Text = "חסום: " + string.Join(" · ", labels);
                BlockScope.Visibility = Visibility.Visible;
            }
        }

        // Schedule summary
        var activeWindows = rule.Windows.Where(w => w.Enabled && w.Days.Count > 0).ToList();
        var now = DateTimeOffset.Now;
        var isCurrentlyBlocked = rule.IsActive(now);
        ScheduleSummary.Text = activeWindows.Count > 0
            ? string.Join(" · ", activeWindows.Take(2).Select(w => w.Describe()))
            : "אין לוח זמנים";

        // Status tag
        if (isCurrentlyBlocked)
        {
            StatusTag.Visibility = Visibility.Visible;
            StatusTag.Background = new SolidColorBrush(Color.FromRgb(14, 124, 134)); // Teal
            StatusTagText.Text = "חסום כרגע";
            StatusTagText.Foreground = Brushes.White;
        }
        else if (rule.Enabled)
        {
            StatusTag.Visibility = Visibility.Visible;
            StatusTag.Background = new SolidColorBrush(Color.FromRgb(224, 242, 241)); // TealSoft
            StatusTagText.Text = "פעיל";
            StatusTagText.Foreground = new SolidColorBrush(Color.FromRgb(14, 124, 134)); // Teal
        }
        else
        {
            StatusTag.Visibility = Visibility.Collapsed;
        }

        RuleSwitch.IsChecked = rule.Enabled;
        _suppressEvents = false;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void RuleSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _rule is null) return;
        _rule.Enabled = RuleSwitch.IsChecked == true;
        ToggleChanged?.Invoke(this, EventArgs.Empty);
    }
}
