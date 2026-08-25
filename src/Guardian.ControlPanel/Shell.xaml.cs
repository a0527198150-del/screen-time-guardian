using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class Shell : UserControl
{
    private static readonly SolidColorBrush TealBrush = new(Color.FromRgb(14, 124, 134)); // #0E7C86
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(148, 163, 184)); // #94A3B8
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

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

        HomeViewControl.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        RulesViewControl.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsViewControl.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        UpdateNavIndicator(NavHome, NavHomeBg, NavHomeIndicator, index == 0);
        UpdateNavIndicator(NavRules, NavRulesBg, NavRulesIndicator, index == 1);
        UpdateNavIndicator(NavSettings, NavSettingsBg, NavSettingsIndicator, index == 2);
    }

    private static void UpdateNavIndicator(Border nav, SolidColorBrush bgBrush,
        SolidColorBrush indicatorBrush, bool active)
    {
        bgBrush.Color = active ? Color.FromArgb(20, 255, 255, 255) : Colors.Transparent; // 8% white
        indicatorBrush.Color = active ? TealBrush.Color : Colors.Transparent;

        if (nav.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is StackPanel sp)
        {
            foreach (var tb in sp.Children.OfType<TextBlock>())
            {
                tb.Foreground = active ? WhiteBrush : MutedBrush;
            }
        }
    }

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
