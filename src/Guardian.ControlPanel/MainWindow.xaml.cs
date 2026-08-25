using System.Collections.ObjectModel;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class MainWindow : Window
{
    private readonly GuardianPipeClient _pipeClient = new();
    private ConfigurationDocument _configuration = ConfigurationDocument.Default;
    private string _applicationPassword = string.Empty;
    private bool _authenticated;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;

        // Wire up Shell navigation
        ShellControl.Settings.PasswordChangeRequested += Settings_PasswordChangeRequested;
        ShellControl.Rules.NewRuleRequested += Rules_NewRuleRequested;
        ShellControl.Rules.EditAppRuleRequested += Rules_EditAppRuleRequested;
        ShellControl.Rules.EditSiteRuleRequested += Rules_EditSiteRuleRequested;
        ShellControl.Rules.EditAccountRuleRequested += Rules_EditAccountRuleRequested;
        ShellControl.Rules.DeleteRuleRequested += Rules_DeleteRuleRequested;
        ShellControl.Rules.ToggleRuleRequested += Rules_ToggleRuleRequested;
        ShellControl.Rules.Snackbar = ShellControl.Snackbar;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Adapt window size to the current screen
        var screen = SystemParameters.WorkArea;
        var targetWidth = Math.Min(1060, screen.Width * 0.9);
        var targetHeight = Math.Min(640, screen.Height * 0.92);
        Width = Math.Max(MinWidth, targetWidth);
        Height = Math.Max(MinHeight, targetHeight);
        Left = (screen.Width - Width) / 2 + screen.Left;
        Top = (screen.Height - Height) / 2 + screen.Top;

        SetHeaderStatus("יש להזין את סיסמת האפליקציה. בהפעלה הראשונה הסיסמה תיקבע כסיסמת הניהול.", false);
    }

    // ==================== Authentication ====================

    private async void AuthButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = AppPasswordBox.Password;
            ApplicationPassword.Validate(password);

            var response = await _pipeClient.GetConfigurationAsync(password);
            if (!response.Ok && response.NeedsInitialization)
            {
                response = await _pipeClient.InitializePasswordAsync(password);
            }

            if (!response.Ok || response.Configuration is null)
            {
                throw new UnauthorizedAccessException(response.Error.Length == 0
                    ? "אימות סיסמת האפליקציה נכשל."
                    : response.Error);
            }

            _applicationPassword = password;
            _authenticated = true;
            _configuration = response.Configuration;

            // Show Shell, hide auth bar
            AuthBar.Visibility = Visibility.Collapsed;
            ShellControl.Visibility = Visibility.Visible;

            // Set password for child views
            ShellControl.Settings.SetPassword(password);

            // Load data into all views
            LoadAllViews();
            await RefreshStatusAsync();

            SetHeaderStatus("האימות הצליח.", false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _authenticated = false;
            SetHeaderStatus(ex.Message, true);
        }
    }

    // ==================== Load / Save ====================

    private void LoadAllViews()
    {
        ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
        ShellControl.Rules.Show(_configuration);
        ShellControl.Settings.Show(_configuration);
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Not used in new UI — save is now implicit from settings
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            EnsureAuthenticated();

            // Collect from all views
            ShellControl.Settings.CollectInto(_configuration);

            var response = await _pipeClient.SaveConfigurationAsync(_applicationPassword, _configuration);
            if (!response.Ok || response.Configuration is null)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            _configuration = response.Configuration;
            LoadAllViews();
            await RefreshStatusAsync();

            SetHeaderStatus(
                response.Notice.Length > 0 ? response.Notice
                : "ההגדרות נשמרו. השירות יחיל אותן תוך 15 שניות.",
                response.Notice.Contains("ממתין"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            SetHeaderStatus(ex.Message, true);
        }
    }

    // ==================== Rule management ====================

    private void Rules_NewRuleRequested(object? sender, EventArgs e)
    {
        var wizard = new RuleWizard { Owner = this };
        if (wizard.ShowDialog() == true && wizard.Result is not null)
        {
            switch (wizard.Result)
            {
                case ApplicationRule app:
                    _configuration.Applications.Add(app);
                    break;
                case WebsiteRule site:
                    _configuration.Websites.Add(site);
                    break;
                case GoogleAccountRule account:
                    _configuration.GoogleAccounts.Add(account);
                    break;
            }
            ShellControl.Rules.Show(_configuration);
            ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
        }
    }

    private void Rules_EditAppRuleRequested(object? sender, ApplicationRule rule)
    {
        // Open RuleWizard in edit mode for app rules
        var wizard = new RuleWizard { Owner = this };
        if (wizard.ShowDialog() == true && wizard.Result is ApplicationRule edited)
        {
            rule.Name = edited.Name;
            rule.Enabled = edited.Enabled;
            rule.Windows = edited.Windows;
            rule.Targets = edited.Targets;
            ShellControl.Rules.Show(_configuration);
            ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
        }
    }

    private void Rules_EditSiteRuleRequested(object? sender, WebsiteRule rule)
    {
        var wizard = new RuleWizard { Owner = this };
        if (wizard.ShowDialog() == true && wizard.Result is WebsiteRule edited)
        {
            rule.Name = edited.Name;
            rule.Domain = edited.Domain;
            rule.Enabled = edited.Enabled;
            rule.Windows = edited.Windows;
            ShellControl.Rules.Show(_configuration);
            ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
        }
    }

    private void Rules_EditAccountRuleRequested(object? sender, GoogleAccountRule rule)
    {
        var wizard = new RuleWizard { Owner = this };
        if (wizard.ShowDialog() == true && wizard.Result is GoogleAccountRule edited)
        {
            rule.Email = edited.Email;
            rule.Enabled = edited.Enabled;
            rule.Windows = edited.Windows;
            rule.Services = edited.Services;
            rule.Sites = edited.Sites;
            ShellControl.Rules.Show(_configuration);
            ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
        }
    }

    private void Rules_DeleteRuleRequested(object? sender, ScheduledRule rule)
    {
        switch (rule)
        {
            case ApplicationRule app:
                _configuration.Applications.Remove(app);
                break;
            case WebsiteRule site:
                _configuration.Websites.Remove(site);
                break;
            case GoogleAccountRule account:
                _configuration.GoogleAccounts.Remove(account);
                break;
        }
        ShellControl.Rules.Show(_configuration);
        ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
    }

    private void Rules_ToggleRuleRequested(object? sender, ScheduledRule rule)
    {
        ShellControl.Home.Show(_configuration, null, Array.Empty<UpcomingEvent>());
    }

    // ==================== Keyboard navigation ====================

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _ = SaveAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // Close any open dialog — handled by the dialog itself
        }

        base.OnKeyDown(e);
    }

    // ==================== Status ====================

    private async Task RefreshStatusAsync()
    {
        if (!_authenticated) return;

        try
        {
            var statusResponse = await _pipeClient.GetStatusAsync(_applicationPassword);
            var upcomingResponse = await _pipeClient.GetUpcomingAsync(_applicationPassword);

            var status = statusResponse.Ok ? statusResponse.Status : null;
            var upcoming = upcomingResponse.Ok ? upcomingResponse.Upcoming : Array.Empty<UpcomingEvent>();

            // Update Shell status dot
            ShellControl.UpdateStatus(
                status?.EnforcementActive ?? false,
                status?.SafeMode ?? false,
                status?.Reason ?? "");

            // Update HomeView
            ShellControl.Home.Show(_configuration, status, upcoming);

            // Update header status
            if (status?.SafeMode == true)
            {
                SetHeaderStatus("⚠ השירות במצב בטוח — האכיפה מושבתת", true);
            }
            else
            {
                HeaderStatus.Text = string.Empty;
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            SetHeaderStatus(ex.Message, true);
        }
    }

    // ==================== Settings: Password Change ====================

    private async void Settings_PasswordChangeRequested(object? sender, EventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            var (current, newPass) = ShellControl.Settings.GetPasswordChange();

            var response = await _pipeClient.ChangePasswordAsync(current, newPass);
            if (!response.Ok)
            {
                SetHeaderStatus(response.Error, true);
                return;
            }

            _applicationPassword = newPass;
            ShellControl.Settings.ClearPasswordFields();
            SetHeaderStatus("הסיסמה שונתה בהצלחה.", false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            SetHeaderStatus(ex.Message, true);
        }
    }

    // ==================== Helpers ====================

    private void EnsureAuthenticated()
    {
        if (!_authenticated || string.IsNullOrEmpty(_applicationPassword))
        {
            throw new UnauthorizedAccessException("יש לאמת קודם את סיסמת האפליקציה.");
        }
    }

    private void SetHeaderStatus(string message, bool isError)
    {
        HeaderStatus.Text = message;
        HeaderStatus.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(169, 50, 38))  // Rose
            : new SolidColorBrush(Color.FromRgb(90, 103, 128)); // MutedInk
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException
            or UnauthorizedAccessException
            or ArgumentException
            or IOException
            or TimeoutException;
}
