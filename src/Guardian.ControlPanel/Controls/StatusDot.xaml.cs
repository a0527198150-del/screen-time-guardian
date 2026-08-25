using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTimeGuardian.ControlPanel;

public partial class StatusDot : UserControl
{
    private static readonly SolidColorBrush TealBrush = new(Color.FromRgb(14, 124, 134));
    private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(180, 119, 15));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(120, 130, 150));

    public StatusDot()
    {
        InitializeComponent();
    }

    public void Update(bool active, bool safeMode, string label)
    {
        Dot.Fill = safeMode ? AmberBrush : active ? TealBrush : GrayBrush;
        Label.Text = label;
    }
}
