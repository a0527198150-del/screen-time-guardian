using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenTimeGuardian.ControlPanel;

public partial class Snackbar : UserControl
{
    private System.Windows.Threading.DispatcherTimer? _hideTimer;
    private Action? _undoAction;

    public Snackbar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the snackbar with a message. Optionally provides an undo action.
    /// The snackbar auto-hides after the specified duration.
    /// </summary>
    public void Show(string message, Action? undoAction = null, int durationMs = 6000)
    {
        _undoAction = undoAction;
        MessageText.Text = message;
        UndoButton.Visibility = undoAction is not null ? Visibility.Visible : Visibility.Collapsed;

        // Stop any existing timer
        _hideTimer?.Stop();
        _hideTimer?.Dispose();

        // Make visible
        IsHitTestVisible = true;
        Opacity = 0;

        // Animate in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Bar.RenderTransform = new TranslateTransform(0, 0);
        BeginAnimation(OpacityProperty, fadeIn);
        Bar.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

        // Start auto-hide timer
        _hideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };
        _hideTimer.Start();
    }

    public void Hide()
    {
        _hideTimer?.Stop();
        FadeOut();
    }

    private void FadeOut()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            IsHitTestVisible = false;
            _undoAction = null;
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        _hideTimer?.Stop();
        _undoAction?.Invoke();
        FadeOut();
    }
}
