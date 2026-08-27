using System.Windows;
using System.Windows.Input;

namespace ScreenTimeGuardian.ControlPanel;

public partial class ConfirmDialog : Window
{
    public string DialogTitleText
    {
        get => DialogTitle.Text;
        set => DialogTitle.Text = value;
    }

    public string Message
    {
        get => DialogMessage.Text;
        set => DialogMessage.Text = value;
    }

    public string ConfirmText
    {
        get => (string)ConfirmButton.Content;
        set => ConfirmButton.Content = value;
    }

    public string CancelText
    {
        get => (string)CancelButton.Content;
        set => CancelButton.Content = value;
    }

    public bool ShowPassword
    {
        get => DialogPassword.Visibility == Visibility.Visible;
        set => DialogPassword.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public string EnteredPassword => DialogPassword.Password;

    public string PasswordErrorText
    {
        get => PasswordError.Text;
        set => PasswordError.Text = value;
    }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e) => Close();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ShowPassword && string.IsNullOrWhiteSpace(EnteredPassword))
        {
            PasswordErrorText = "יש להזין סיסמה.";
            DialogPassword.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
