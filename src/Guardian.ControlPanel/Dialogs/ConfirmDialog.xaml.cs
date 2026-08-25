using System.Windows;
using System.Windows.Input;

namespace ScreenTimeGuardian.ControlPanel;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public void Configure(string title, string message, string confirmText = "אישור",
        bool isDanger = false)
    {
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmText;
        if (isDanger)
        {
            ConfirmButton.Style = (Style)FindResource("ButtonPrimary");
            ConfirmButton.Foreground = System.Windows.Media.Brushes.White;
        }
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e) => Close();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
