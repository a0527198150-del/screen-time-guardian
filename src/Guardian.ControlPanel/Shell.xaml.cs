using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class Shell : UserControl
{
    private static readonly SolidColorBrush TealBrush = new(Color.FromRgb(14, 124, 134)); // #0E7C86
    private static readonly SolidColorBrush TealSoftBrush = new(Color.FromRgb(224, 242, 241)); // #E0F2F1
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(148, 163, 184)); // #94A3B8

    private int _currentNav = -1;

    public Shell()
    {
        InitializeComponent();
        NavHome.MouseLeftButtonDown += (_, _) => NavigateTo(0);
        NavRules.MouseLeftButtonDown += (_, _) => NavigateTo(1);
        NavSettings.MouseLeftButtonDown += (_, _) => NavigateTo(2);
        NavigateTo(0);
    }

    public void NavigateTo(int index)
    {
        _currentNav = index;
        ClearMessage();

        HomeScroll.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        RulesScroll.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        UpdateNavIndicator(NavHome, NavHomeBg, NavHomeText, index == 0);
        UpdateNavIndicator(NavRules, NavRulesBg, NavRulesText, index == 1);
        UpdateNavIndicator(NavSettings, NavSettingsBg, NavSettingsText, index == 2);
    }

    private static void UpdateNavIndicator(Border nav, SolidColorBrush bgBrush,
        TextBlock label, bool active)
    {
        bgBrush.Color = active ? TealSoftBrush.Color : Colors.Transparent;
        label.Foreground = active ? TealBrush : MutedBrush;
    }

    /// <summary>
    /// Shows a transient message strip under the header. Teal for information,
    /// rose for errors. Cleared automatically when navigating to another page.
    /// </summary>
    public void ShowMessage(string message, bool isError)
    {
        MessageText.Text = message;
        MessageBar.Background = isError
            ? new SolidColorBrush(Color.FromRgb(252, 233, 230)) // RoseSoft
            : TealSoftBrush;
        MessageText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(169, 50, 38))   // Rose
            : new SolidColorBrush(Color.FromRgb(20, 27, 46));   // Ink
        MessageBar.Visibility = Visibility.Visible;
    }

    public void ClearMessage() => MessageBar.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Updates the status dot color and label based on the service status.
    /// Teal = active, Amber = safe mode / grace, gray = off.
    /// Label is mandatory — color alone doesn't carry info.
    /// </summary>
    public void UpdateStatus(bool enforcementActive, bool safeMode, string reason)
    {
        if (safeMode)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(180, 119, 15)); // Amber
            StatusLabel.Text = "מצב בטוח";
        }
        else if (enforcementActive)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(14, 124, 134)); // Teal
            StatusLabel.Text = "אכיפה פעילה";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(120, 130, 150)); // Gray
            StatusLabel.Text = "כבוי";
        }
    }

    // Expose child views for MainWindow to populate
    public HomeView Home => HomeViewControl;
    public RulesView Rules => RulesViewControl;
    public SettingsView Settings => SettingsViewControl;
}
