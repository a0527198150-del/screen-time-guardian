using System.IO;
using System.Windows;
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
        LoadDefaultFields();
        DiscoverBrowsersButton_OnClick(this, new RoutedEventArgs());
        StatusText.Text = "יש להזין את סיסמת האפליקציה ולאמת אותה מול שירות Windows.";
    }

    private void LoadDefaultFields()
    {
        AccountEmailBox.Text = string.Empty;
        GoogleServicesBox.Text = "gmail, chat";
        BlockedDomainsBox.Text = string.Empty;
        BlockedApplicationsBox.Text = string.Empty;
        EnforceWebsitesBox.IsChecked = false;
        StrictPortableModeBox.IsChecked = false;
        ScheduleDaysBox.Text = "Sunday, Monday, Tuesday, Wednesday, Thursday";
        ScheduleStartBox.Text = "23:00";
        ScheduleEndBox.Text = "07:00";
        RefreshApprovedBrowserList();
    }

    private async void AuthenticateButton_OnClick(object sender, RoutedEventArgs e)
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
            ApplyConfigurationToFields();
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "האימות הצליח. אפשר לערוך ולשמור את ההגדרות.";
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException or TimeoutException)
        {
            _authenticated = false;
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
    }

    private void ApplyConfigurationToFields()
    {
        var account = _configuration.GoogleAccounts.FirstOrDefault();
        var website = _configuration.Websites.FirstOrDefault();
        var application = _configuration.Applications.FirstOrDefault();
        var window = account?.Windows.FirstOrDefault()
            ?? website?.Windows.FirstOrDefault()
            ?? application?.Windows.FirstOrDefault();

        AccountEmailBox.Text = account?.Email ?? string.Empty;
        GoogleServicesBox.Text = account is null ? "gmail, chat" : string.Join(", ", account.Services);
        BlockedDomainsBox.Text = string.Join(Environment.NewLine, _configuration.Websites.Select(item => item.Domain));
        BlockedApplicationsBox.Text = string.Join(
            Environment.NewLine,
            _configuration.Applications.SelectMany(item => item.ExecutableNames));
        EnforceWebsitesBox.IsChecked = _configuration.WebsiteEnforcement == WebsiteEnforcementMode.Enforced;
        StrictPortableModeBox.IsChecked = _configuration.StrictPortableApplicationMode;
        ScheduleDaysBox.Text = window is null
            ? "Sunday, Monday, Tuesday, Wednesday, Thursday"
            : string.Join(", ", window.Days);
        ScheduleStartBox.Text = window?.Start ?? "23:00";
        ScheduleEndBox.Text = window?.End ?? "07:00";
        RefreshApprovedBrowserList();
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            var days = ParseDays(ScheduleDaysBox.Text);
            if (days.Count == 0)
            {
                throw new InvalidOperationException("יש לבחור לפחות יום אחד.");
            }

            if (!TimeOnly.TryParse(ScheduleStartBox.Text, out _)
                || !TimeOnly.TryParse(ScheduleEndBox.Text, out _))
            {
                throw new InvalidOperationException("שעות חייבות להיות בפורמט HH:mm.");
            }

            var window = new ScheduleWindow
            {
                Days = days,
                Start = ScheduleStartBox.Text.Trim(),
                End = ScheduleEndBox.Text.Trim()
            };

            _configuration.Websites = BlockedDomainsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(domain => PolicyEngine.NormalizeDomain(domain))
                .Where(ConfigurationValidation.IsValidDomain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(domain => new WebsiteRule
                {
                    Name = domain,
                    Domain = domain,
                    Windows = new List<ScheduleWindow> { CloneWindow(window) }
                })
                .ToList();

            _configuration.Applications = BlockedApplicationsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => Path.GetFileName(name.Trim()))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new ApplicationRule
                {
                    Name = name,
                    ExecutableNames = new List<string> { name },
                    Windows = new List<ScheduleWindow> { CloneWindow(window) }
                })
                .ToList();

            _configuration.GoogleAccounts = new List<GoogleAccountRule>();
            var email = AccountEmailBox.Text.Trim();
            if (email.Length > 0)
            {
                if (!ConfigurationValidation.IsValidEmail(email))
                {
                    throw new InvalidOperationException("כתובת חשבון Google אינה תקינה.");
                }

                _configuration.GoogleAccounts.Add(new GoogleAccountRule
                {
                    Name = email,
                    Email = email,
                    Services = GoogleServicesBox.Text
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(service => service.ToLowerInvariant())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Windows = new List<ScheduleWindow> { CloneWindow(window) }
                });
            }

            _configuration.WebsiteEnforcement = EnforceWebsitesBox.IsChecked == true
                ? WebsiteEnforcementMode.Enforced
                : WebsiteEnforcementMode.AuditOnly;
            _configuration.BlockPrivateAndGuestWhenExtensionUnavailable = true;
            _configuration.BlockPortableBrowsersDuringAnySchedule = true;
            _configuration.StrictPortableApplicationMode = StrictPortableModeBox.IsChecked == true;
            _configuration.GuestModeAllowedWhenNoRelevantBlock = true;

            var response = await _pipeClient.SaveConfigurationAsync(_applicationPassword, _configuration);
            if (!response.Ok)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "ההגדרות נשמרו באמצעות שירות Windows.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException or TimeoutException)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
    }

    private void DiscoverBrowsersButton_OnClick(object sender, RoutedEventArgs e)
    {
        DiscoveredBrowsersListBox.ItemsSource = BrowserInventory.Discover();
        StatusText.Text = "רשימת Chrome ו־Edge נטענה מרשומות Windows בלבד.";
    }

    private void DiscoveredBrowsersListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DiscoveredBrowsersListBox.SelectedItem is not BrowserInventoryItem browser)
        {
            return;
        }

        BrowserDisplayNameBox.Text = browser.DisplayName;
        BrowserPublisherBox.Text = browser.Publisher;
        BrowserProductBox.Text = browser.ProductName;
        BrowserPathBox.Text = browser.ExecutablePath;
    }

    private void AddBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            var path = BrowserPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("יש להזין נתיב מלא לקובץ exe.");
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException("קובץ הדפדפן לא נמצא.");
            }

            var approval = new BrowserApproval
            {
                DisplayName = BrowserDisplayNameBox.Text.Trim(),
                Publisher = BrowserPublisherBox.Text.Trim(),
                ProductName = BrowserProductBox.Text.Trim(),
                ExecutablePath = Path.GetFullPath(path)
            };

            if (string.IsNullOrWhiteSpace(approval.Publisher)
                || string.IsNullOrWhiteSpace(approval.ProductName))
            {
                throw new InvalidOperationException("יש למלא יצרן ושם מוצר.");
            }

            _configuration.ApprovedBrowsers.RemoveAll(item =>
                string.Equals(item.ExecutablePath, approval.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            _configuration.ApprovedBrowsers.Add(approval);
            RefreshApprovedBrowserList();
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "הדפדפן נוסף לרשימת האישור. יש לשמור את ההגדרות.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
    }

    private void RemoveBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            if (ApprovedBrowsersListBox.SelectedItem is BrowserApproval approval)
            {
                _configuration.ApprovedBrowsers.Remove(approval);
                RefreshApprovedBrowserList();
                StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                StatusText.Text = "האישור הוסר. יש לשמור את ההגדרות.";
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
    }

    private void RefreshApprovedBrowserList()
    {
        ApprovedBrowsersListBox.ItemsSource = null;
        ApprovedBrowsersListBox.ItemsSource = _configuration.ApprovedBrowsers;
    }

    private async void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            var response = await _pipeClient.GetConfigurationAsync(_applicationPassword);
            if (!response.Ok || response.Configuration is null)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            _configuration = response.Configuration;
            ApplyConfigurationToFields();
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "ההגדרות נטענו מחדש.";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or TimeoutException)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated || string.IsNullOrEmpty(_applicationPassword))
        {
            throw new UnauthorizedAccessException("יש לאמת קודם את סיסמת האפליקציה.");
        }
    }

    private static List<DayOfWeek> ParseDays(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Enum.TryParse<DayOfWeek>(item, true, out var day) ? day : (DayOfWeek?)null)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .Distinct()
            .ToList();
    }

    private static ScheduleWindow CloneWindow(ScheduleWindow source) => new()
    {
        Enabled = source.Enabled,
        Days = source.Days.ToList(),
        Start = source.Start,
        End = source.End,
        AllDay = source.AllDay
    };
}
