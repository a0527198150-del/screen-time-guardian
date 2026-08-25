using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenTimeGuardian.ControlPanel;

public partial class StatusDot : UserControl
{
    private static readonly SolidColorBrush TealBrush = new(Color.FromRgb(14, 124, 134));
    private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(180, 119, 15));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(120, 130, 150));

    public StatusDot()
    {
        InitializeComponent();
        // Pulse is started by the Loaded EventTrigger in XAML by default.
        // We'll stop it immediately and control it from code.
        PulseStoryboard.Stop(Dot);
        Dot.Opacity = 1.0;
    }

    public void Update(bool active, bool safeMode, string label)
    {
        Dot.Fill = safeMode ? AmberBrush : active ? TealBrush : GrayBrush;
        Label.Text = label;

        // Pulse only when enforcement is active (not safe mode, not disabled)
        if (active && !safeMode)
        {
            Dot.Opacity = 1.0;
            PulseStoryboard.Begin(Dot, true);
        }
        else
        {
            PulseStoryboard.Stop(Dot);
            Dot.Opacity = 1.0;
        }
    }
}
