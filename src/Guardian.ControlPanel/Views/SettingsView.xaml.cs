using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class SettingsView : UserControl
{
    private ConfigurationDocument? _configuration;
    private string _applicationPassword = "";
    private bool _suppressEvents;

    public event EventHandler? PasswordChangeRequested;
    public event EventHandler? SettingsChanged;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void SetPassword(string password)
    {
        _applicationPassword = password;
    }

    public void Show(ConfigurationDocument config)
    {
        _suppressEvents = true;
        _configuration = config;

        BootGraceBox.Text = config.Safety.BootGraceSeconds.ToString();
        ServiceGraceBox.Text = config.Safety.ServiceGraceSeconds.ToString();
        MaxActionsBox.Text = config.Safety.MaxActionsPerMinute.ToString();

        BlockBrowserLaunchBox.IsChecked = config.BrowserLockdown.BlockUnapprovedBrowserLaunch;
        ScanHiddenBrowsersBox.IsChecked = config.BrowserLockdown.ScanForHiddenBrowsers;
        ScanIntervalBox.Text = config.BrowserLockdown.ScanIntervalMinutes.ToString();

        CoolingOffBox.Text = config.ChangeControl.CoolingOffHours.ToString();

        EnforceForAdministratorsBox.IsChecked = config.EnforceForAdministrators;

        // Pending change
        PendingChangeText.Text = config.PendingChange?.ToString() ?? "אין שינוי ממתין.";

        // Approved browsers
        AllowApprovedBrowsersBox.IsChecked = config.BrowserLockdown.AllowApprovedBrowsersWithoutExtension;
        ApprovedBrowsersList.ItemsSource = config.BrowserLockdown.ApprovedBrowserPaths.ToList();

        VersionText.Text = $"שומר זמן מסך · גרסה {config.SchemaVersion}";
        InstallPathText.Text = Installer.GetExeDirectory();

        _suppressEvents = false;
    }

    public void CollectInto(ConfigurationDocument config)
    {
        if (_suppressEvents || _configuration is null) return;

        config.Safety.BootGraceSeconds = Math.Clamp(ParseInt(BootGraceBox.Text, 120), 0, 86_400);
        config.Safety.ServiceGraceSeconds = Math.Clamp(ParseInt(ServiceGraceBox.Text, 30), 0, 86_400);
        config.Safety.MaxActionsPerMinute = Math.Clamp(ParseInt(MaxActionsBox.Text, 20), 1, 10_000);

        config.BrowserLockdown.BlockUnapprovedBrowserLaunch = BlockBrowserLaunchBox.IsChecked == true;
        config.BrowserLockdown.ScanForHiddenBrowsers = ScanHiddenBrowsersBox.IsChecked == true;
        config.BrowserLockdown.ScanIntervalMinutes = Math.Clamp(ParseInt(ScanIntervalBox.Text, 10), 1, 1440);

        config.ChangeControl.CoolingOffHours = Math.Clamp(ParseInt(CoolingOffBox.Text, 0), 0, 8760);
        config.EnforceForAdministrators = EnforceForAdministratorsBox.IsChecked == true;
        config.BrowserLockdown.AllowApprovedBrowsersWithoutExtension = AllowApprovedBrowsersBox.IsChecked == true;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value.Trim(), out var parsed) && parsed >= 0 ? parsed : fallback;
    }

    private void ToggleGroup_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border toggleBorder) return;

        // toggleBorder -> toggleHeader (StackPanel) -> cardStack (StackPanel) -> card (Border)
        var toggleHeader = toggleBorder.Parent as StackPanel;
        var cardStack = toggleHeader?.Parent as StackPanel;
        var card = cardStack?.Parent as Border;
        if (card?.Child is not StackPanel cardContent) return;

        // cardContent has two children: toggleHeader and contentPanel
        foreach (var child in cardContent.Children)
        {
            if (child is StackPanel content && child != toggleHeader)
            {
                content.Visibility = content.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                // Rotate arrow
                if (toggleHeader?.Children[0] is StackPanel arrowStack &&
                    arrowStack.Children.Count > 0 &&
                    arrowStack.Children[0] is TextBlock arrow)
                {
                    var rotate = arrow.RenderTransform as System.Windows.Media.RotateTransform;
                    if (rotate is not null)
                    {
                        rotate.Angle = content.Visibility == Visibility.Visible ? 0 : -90;
                    }
                }
                break;
            }
        }
    }

    private void NewPasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var password = NewPasswordBox.Password;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Any(char.IsUpper)) score++;
        if (password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) score++;
        PasswordStrength.Text = password.Length == 0
            ? string.Empty
            : $"חוזק: {(score <= 2 ? "חלשה" : score <= 4 ? "בינונית" : "חזקה")} · {password.Length} תווים";
    }

    private void ConfirmPasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        PasswordError.Text = NewPasswordBox.Password.Length > 0
            && !string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal)
            ? "הסיסמאות אינן תואמות."
            : string.Empty;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            PasswordError.Text = "הסיסמאות אינן תואמות.";
            return;
        }

        try
        {
            ApplicationPassword.Validate(NewPasswordBox.Password);
        }
        catch (ArgumentException ex)
        {
            PasswordError.Text = ex.Message;
            return;
        }

        PasswordChangeRequested?.Invoke(this, EventArgs.Empty);
    }

    public (string Current, string New) GetPasswordChange()
    {
        return (CurrentPasswordBox.Password, NewPasswordBox.Password);
    }

    public void ClearPasswordFields()
    {
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        PasswordStrength.Text = string.Empty;
        PasswordError.Text = string.Empty;
    }
}
