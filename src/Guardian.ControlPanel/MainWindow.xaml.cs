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
    }

    private void LoadConfiguration()
    {
        _configuration = _store.Load();
        var account = _configuration.GoogleAccounts.FirstOrDefault();
        var website = _configuration.Websites.FirstOrDefault();
        var window = account?.Windows.FirstOrDefault() ?? website?.Windows.FirstOrDefault();

        AccountEmailBox.Text = account?.Email ?? string.Empty;
        GoogleServicesBox.Text = account is null ? "gmail, chat" : string.Join(", ", account.Services);
        BlockedDomainsBox.Text = string.Join(Environment.NewLine, _configuration.Websites.Select(item => item.Domain));
        EnforceWebsitesBox.IsChecked = _configuration.WebsiteEnforcement == WebsiteEnforcementMode.Enforced;
        ScheduleDaysBox.Text = window is null
            ? "Sunday, Monday, Tuesday, Wednesday, Thursday"
            : string.Join(", ", window.Days);
        ScheduleStartBox.Text = window?.Start ?? "23:00";
        ScheduleEndBox.Text = window?.End ?? "07:00";
        StatusText.Text = "ההגדרות נטענו.";
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
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
            _configuration.GuestModeAllowedWhenNoRelevantBlock = true;
            _store.Save(_configuration);
            StatusText.Text = "ההגדרות נשמרו. שירות המערכת יעדכן את המדיניות.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            StatusText.Text = exception.Message;
        }
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
