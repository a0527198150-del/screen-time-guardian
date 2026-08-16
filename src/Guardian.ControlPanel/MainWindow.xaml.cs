using System.IO;
using System.Windows;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class MainWindow : Window
{
    private readonly ConfigurationStore _store = new();
    private ConfigurationDocument _configuration = ConfigurationDocument.Default;

    public MainWindow()
    {
        InitializeComponent();
        LoadConfiguration();
        DiscoverBrowsersButton_OnClick(this, new RoutedEventArgs());
    }

    private void LoadConfiguration()
    {
        _configuration = _store.Load();
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
        StatusText.Text = AdminGuard.IsAdministrator()
            ? "ההגדרות נטענו."
            : "ההגדרות נטענו. שמירה דורשת הרשאת מנהל Windows.";
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!AdminGuard.IsAdministrator())
            {
                throw new UnauthorizedAccessException("שמירת מדיניות דורשת חשבון מנהל Windows.");
            }

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
            _store.Save(_configuration);
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "ההגדרות נשמרו. שירות המערכת יעדכן את המדיניות.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException)
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
            if (!AdminGuard.IsAdministrator())
            {
                throw new UnauthorizedAccessException("הוספת אישור דפדפן דורשת חשבון מנהל Windows.");
            }

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
        if (!AdminGuard.IsAdministrator())
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = "הסרת אישור דורשת חשבון מנהל Windows.";
            return;
        }

        if (ApprovedBrowsersListBox.SelectedItem is BrowserApproval approval)
        {
            _configuration.ApprovedBrowsers.Remove(approval);
            RefreshApprovedBrowserList();
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            StatusText.Text = "האישור הוסר. יש לשמור את ההגדרות.";
        }
    }

    private void RefreshApprovedBrowserList()
    {
        ApprovedBrowsersListBox.ItemsSource = null;
        ApprovedBrowsersListBox.ItemsSource = _configuration.ApprovedBrowsers;
    }

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        LoadConfiguration();
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
