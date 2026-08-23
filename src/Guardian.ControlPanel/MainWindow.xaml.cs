using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class MainWindow : Window
{
    private readonly GuardianPipeClient _pipeClient = new();
    private ConfigurationDocument _configuration = ConfigurationDocument.Default;
    private string _applicationPassword = string.Empty;
    private bool _authenticated;
    private bool _suppressEvents;

    private readonly ObservableCollection<ApplicationRule> _rules = new();
    private readonly ObservableCollection<AppTarget> _targets = new();
    private readonly ObservableCollection<ScheduleWindow> _windows = new();
    private readonly ObservableCollection<GoogleAccountRule> _accounts = new();
    private readonly ObservableCollection<string> _accountSites = new();
    private readonly ObservableCollection<ScheduleWindow> _accountWindows = new();
    private readonly ObservableCollection<WebsiteRule> _domains = new();
    private readonly ObservableCollection<DiscoveredSite> _discovered = new();
    private readonly ObservableCollection<string> _approvedBrowsers = new();

    private IReadOnlyList<LocalUser> _localUsers = Array.Empty<LocalUser>();

    public MainWindow()
    {
        InitializeComponent();

        RulesListBox.ItemsSource = _rules;
        TargetsListBox.ItemsSource = _targets;
        WindowsListBox.ItemsSource = _windows;
        AccountsListBox.ItemsSource = _accounts;
        AccountSitesListBox.ItemsSource = _accountSites;
        AccountWindowsListBox.ItemsSource = _accountWindows;
        DomainsListBox.ItemsSource = _domains;
        DiscoveredListBox.ItemsSource = _discovered;
        ApprovedBrowsersListBox.ItemsSource = _approvedBrowsers;

        _localUsers = UserAccounts.Discover();
        UsersListBox.ItemsSource = _localUsers;

        SetStatus("יש להזין את סיסמת האפליקציה. בהפעלה הראשונה הסיסמה שתוזן תיקבע כסיסמת הניהול.", false);
    }

    private ApplicationRule? SelectedRule => RulesListBox.SelectedItem as ApplicationRule;
    private ScheduleWindow? SelectedWindow => WindowsListBox.SelectedItem as ScheduleWindow;
    private GoogleAccountRule? SelectedAccount => AccountsListBox.SelectedItem as GoogleAccountRule;
    private ScheduleWindow? SelectedAccountWindow => AccountWindowsListBox.SelectedItem as ScheduleWindow;

    // ==================== authentication ====================

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
            LoadConfigurationIntoUi();
            await RefreshStatusAsync();
            SetStatus("האימות הצליח. אפשר לערוך ולשמור.", false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _authenticated = false;
            SetStatus(exception.Message, true);
        }
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
            LoadConfigurationIntoUi();
            await RefreshStatusAsync();
            SetStatus("ההגדרות נטענו מחדש מהשירות.", false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetStatus(exception.Message, true);
        }
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            CollectUiIntoConfiguration();

            var response = await _pipeClient.SaveConfigurationAsync(_applicationPassword, _configuration);
            if (!response.Ok || response.Configuration is null)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            _configuration = response.Configuration;
            LoadConfigurationIntoUi();
            await RefreshStatusAsync();

            var queued = response.Notice.Contains("ממתין", StringComparison.Ordinal);
            SetStatus(
                response.Notice.Length > 0
                    ? response.Notice
                    : "ההגדרות נשמרו. השירות יחיל אותן תוך 15 שניות.",
                queued);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetStatus(exception.Message, true);
        }
    }

    // ==================== load / collect ====================

    private void LoadConfigurationIntoUi()
    {
        _suppressEvents = true;
        try
        {
            _rules.Clear();
            foreach (var rule in _configuration.Applications)
            {
                _rules.Add(rule);
            }

            _accounts.Clear();
            foreach (var account in _configuration.GoogleAccounts)
            {
                _accounts.Add(account);
            }

            _domains.Clear();
            foreach (var website in _configuration.Websites)
            {
                _domains.Add(website);
            }

            _discovered.Clear();
            foreach (var site in _configuration.DiscoveredSites.Where(item => !item.Dismissed))
            {
                _discovered.Add(site);
            }

            EnforceWebsitesBox.IsChecked = _configuration.WebsiteEnforcement == WebsiteEnforcementMode.Enforced;

            BootGraceBox.Text = _configuration.Safety.BootGraceSeconds.ToString();
            ServiceGraceBox.Text = _configuration.Safety.ServiceGraceSeconds.ToString();
            MaxActionsBox.Text = _configuration.Safety.MaxActionsPerMinute.ToString();

            BlockBrowserLaunchBox.IsChecked = _configuration.BrowserLockdown.BlockUnapprovedBrowserLaunch;
            ScanHiddenBrowsersBox.IsChecked = _configuration.BrowserLockdown.ScanForHiddenBrowsers;
            ScanIntervalBox.Text = _configuration.BrowserLockdown.ScanIntervalMinutes.ToString();
            CoolingOffBox.Text = _configuration.ChangeControl.CoolingOffHours.ToString();
            WebsiteDelayBox.Text = "0";
            AccountWindowDelayBox.Text = "0";

            _approvedBrowsers.Clear();
            foreach (var path in _configuration.BrowserLockdown.ApprovedBrowserPaths)
            {
                _approvedBrowsers.Add(path);
            }

            PendingChangeText.Text = _configuration.PendingChange?.ToString() ?? "אין שינוי ממתין.";

            RuleEditorPanel.IsEnabled = false;
            AccountEditorPanel.IsEnabled = false;
            _targets.Clear();
            _windows.Clear();
            _accountSites.Clear();
            _accountWindows.Clear();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void CollectUiIntoConfiguration()
    {
        _configuration.Applications = _rules.ToList();
        _configuration.GoogleAccounts = _accounts.ToList();
        _configuration.Websites = _domains.ToList();

        _configuration.WebsiteEnforcement = EnforceWebsitesBox.IsChecked == true
            ? WebsiteEnforcementMode.Enforced
            : WebsiteEnforcementMode.AuditOnly;

        _configuration.Safety.BootGraceSeconds = ParseInt(BootGraceBox.Text, 120, "תקופת חסד אחרי הפעלת המחשב");
        _configuration.Safety.ServiceGraceSeconds = ParseInt(ServiceGraceBox.Text, 30, "תקופת חסד אחרי הפעלת השירות");
        _configuration.Safety.MaxActionsPerMinute = ParseInt(MaxActionsBox.Text, 20, "מקסימום פעולות בדקה");

        _configuration.BrowserLockdown.BlockUnapprovedBrowserLaunch = BlockBrowserLaunchBox.IsChecked == true;
        _configuration.BrowserLockdown.ScanForHiddenBrowsers = ScanHiddenBrowsersBox.IsChecked == true;
        _configuration.BrowserLockdown.ScanIntervalMinutes = ParseInt(ScanIntervalBox.Text, 10, "תדירות סריקה");
        _configuration.BrowserLockdown.ApprovedBrowserPaths = _approvedBrowsers.ToList();
        _configuration.ChangeControl.CoolingOffHours = ParseInt(CoolingOffBox.Text, 0, "שעות המתנה להקלה");

        foreach (var rule in _configuration.Applications)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                rule.Name = rule.Targets.FirstOrDefault()?.DisplayName ?? "כלל ללא שם";
            }

            if (rule.Enabled && rule.Targets.Count == 0)
            {
                throw new InvalidOperationException($"הכלל '{rule.Name}' פעיל אבל לא נבחרה בו אף אפליקציה.");
            }

            if (rule.Enabled && rule.Windows.Count(window => window.Enabled && window.Days.Count > 0) == 0)
            {
                throw new InvalidOperationException($"הכלל '{rule.Name}' פעיל אבל אין בו אף חלון זמן עם ימים.");
            }
        }

        foreach (var account in _configuration.GoogleAccounts)
        {
            if (!ConfigurationValidation.IsValidEmail(account.Email))
            {
                throw new InvalidOperationException($"כתובת המייל '{account.Email}' אינה תקינה.");
            }

            account.Name = account.Email;
        }
    }

    // ==================== application rules ====================

    private void RulesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var rule = SelectedRule;
        RuleEditorPanel.IsEnabled = rule is not null;

        _suppressEvents = true;
        try
        {
            _targets.Clear();
            _windows.Clear();

            if (rule is null)
            {
                RuleNameBox.Text = string.Empty;
                return;
            }

            RuleNameBox.Text = rule.Name;
            RuleEnabledBox.IsChecked = rule.Enabled;

            foreach (var target in rule.Targets)
            {
                _targets.Add(target);
            }

            foreach (var window in rule.Windows)
            {
                _windows.Add(window);
            }

            AllUsersBox.IsChecked = rule.AppliesToUserSids.Count == 0;
            UsersListBox.SelectedItems.Clear();
            foreach (var user in _localUsers.Where(user => rule.AppliesToUserSids.Contains(user.Sid, StringComparer.OrdinalIgnoreCase)))
            {
                UsersListBox.SelectedItems.Add(user);
            }

            UsersListBox.IsEnabled = rule.AppliesToUserSids.Count > 0;
        }
        finally
        {
            _suppressEvents = false;
        }

        WindowEditorPanel.IsEnabled = false;
    }

    private void AddRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var rule = new ApplicationRule
        {
            Name = "כלל חדש",
            Enabled = false,
            Windows = { NewWindow() }
        };

        _rules.Add(rule);
        RulesListBox.SelectedItem = rule;
        SetStatus("נוסף כלל חדש. בחר אפליקציות וקבע לוח זמנים, ואז סמן 'הכלל פעיל'.", false);
    }

    private void DuplicateRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } source)
        {
            return;
        }

        var copy = new ApplicationRule
        {
            Name = source.Name + " (עותק)",
            Enabled = false,
            AppliesToUserSids = source.AppliesToUserSids.ToList(),
            Targets = source.Targets
                .Select(target => new AppTarget { DisplayName = target.DisplayName, ExecutablePath = target.ExecutablePath })
                .ToList(),
            Windows = source.Windows.Select(CloneWindow).ToList()
        };

        _rules.Add(copy);
        RulesListBox.SelectedItem = copy;
    }

    private void DeleteRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule)
        {
            return;
        }

        if (MessageBox.Show($"למחוק את הכלל '{rule.Name}'?", "אישור מחיקה",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _rules.Remove(rule);
    }

    private void RuleNameBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedRule is not { } rule)
        {
            return;
        }

        rule.Name = RuleNameBox.Text.Trim();
        RulesListBox.Items.Refresh();
    }

    private void RuleEnabled_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedRule is not { } rule)
        {
            return;
        }

        rule.Enabled = RuleEnabledBox.IsChecked == true;
        RulesListBox.Items.Refresh();
    }

    // ==================== targets ====================

    private void BrowseTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "בחר קובץ הפעלה",
            Filter = "קובצי הפעלה (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddTarget(rule, dialog.FileName, RunningApps.TryProductName(dialog.FileName));
    }

    private void AddRunningButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule || RunningAppsListBox.SelectedItem is not RunningApp app)
        {
            return;
        }

        AddTarget(rule, app.ExecutablePath, app.DisplayName);
    }

    private void AddTarget(ApplicationRule rule, string path, string? displayName)
    {
        var rejection = ProtectedPaths.DescribeRejection(path);
        if (rejection is not null)
        {
            SetStatus(rejection, true);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (rule.Targets.Any(target => string.Equals(target.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("האפליקציה כבר נמצאת בכלל הזה.", true);
            return;
        }

        var target = new AppTarget
        {
            ExecutablePath = fullPath,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(fullPath) : displayName
        };

        rule.Targets.Add(target);
        _targets.Add(target);
        RulesListBox.Items.Refresh();
        SetStatus($"נוספה {target.DisplayName}.", false);
    }

    private void RemoveTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule || TargetsListBox.SelectedItem is not AppTarget target)
        {
            return;
        }

        rule.Targets.Remove(target);
        _targets.Remove(target);
        RulesListBox.Items.Refresh();
    }

    private void RefreshRunningButton_OnClick(object sender, RoutedEventArgs e)
    {
        RunningAppsListBox.ItemsSource = RunningApps.Discover();
        SetStatus("רשימת האפליקציות הפתוחות רועננה.", false);
    }

    // ==================== users ====================

    private void AllUsers_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedRule is not { } rule)
        {
            return;
        }

        var allUsers = AllUsersBox.IsChecked == true;
        UsersListBox.IsEnabled = !allUsers;

        if (allUsers)
        {
            rule.AppliesToUserSids.Clear();
            UsersListBox.SelectedItems.Clear();
        }
        else if (rule.AppliesToUserSids.Count == 0)
        {
            var current = _localUsers.FirstOrDefault(user => user.IsCurrentUser);
            if (current is not null)
            {
                rule.AppliesToUserSids.Add(current.Sid);
                UsersListBox.SelectedItems.Add(current);
            }
        }
    }

    private void UsersListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || SelectedRule is not { } rule || AllUsersBox.IsChecked == true)
        {
            return;
        }

        rule.AppliesToUserSids = UsersListBox.SelectedItems
            .OfType<LocalUser>()
            .Select(user => user.Sid)
            .ToList();
    }

    // ==================== schedule windows ====================

    private void WindowsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var window = SelectedWindow;
        WindowEditorPanel.IsEnabled = window is not null;
        if (window is null)
        {
            return;
        }

        _suppressEvents = true;
        try
        {
            DaySun.IsChecked = window.Days.Contains(DayOfWeek.Sunday);
            DayMon.IsChecked = window.Days.Contains(DayOfWeek.Monday);
            DayTue.IsChecked = window.Days.Contains(DayOfWeek.Tuesday);
            DayWed.IsChecked = window.Days.Contains(DayOfWeek.Wednesday);
            DayThu.IsChecked = window.Days.Contains(DayOfWeek.Thursday);
            DayFri.IsChecked = window.Days.Contains(DayOfWeek.Friday);
            DaySat.IsChecked = window.Days.Contains(DayOfWeek.Saturday);
            AllDayBox.IsChecked = window.AllDay;
            WindowStartBox.Text = window.Start;
            WindowEndBox.Text = window.End;
            WindowEnabledBox.IsChecked = window.Enabled;
            ActivationDelayBox.Text = window.ActivationDelaySeconds.ToString();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void WindowField_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedWindow is not { } window)
        {
            return;
        }

        var days = new List<DayOfWeek>();
        if (DaySun.IsChecked == true) days.Add(DayOfWeek.Sunday);
        if (DayMon.IsChecked == true) days.Add(DayOfWeek.Monday);
        if (DayTue.IsChecked == true) days.Add(DayOfWeek.Tuesday);
        if (DayWed.IsChecked == true) days.Add(DayOfWeek.Wednesday);
        if (DayThu.IsChecked == true) days.Add(DayOfWeek.Thursday);
        if (DayFri.IsChecked == true) days.Add(DayOfWeek.Friday);
        if (DaySat.IsChecked == true) days.Add(DayOfWeek.Saturday);

        window.Days = days;
        window.AllDay = AllDayBox.IsChecked == true;
        window.Enabled = WindowEnabledBox.IsChecked == true;
        if (!int.TryParse(ActivationDelayBox.Text.Trim(), out var activationDelay)
            || activationDelay is < 0 or > 86_400)
        {
            SetStatus("השהיית האכיפה חייבת להיות מספר בין 0 ל־86400 שניות.", true);
            return;
        }

        window.ActivationDelaySeconds = activationDelay;

        if (!window.AllDay)
        {
            if (!TimeOnly.TryParse(WindowStartBox.Text.Trim(), out _) || !TimeOnly.TryParse(WindowEndBox.Text.Trim(), out _))
            {
                SetStatus("שעה חייבת להיות בפורמט HH:mm, לדוגמה 23:00.", true);
                return;
            }

            window.Start = WindowStartBox.Text.Trim();
            window.End = WindowEndBox.Text.Trim();
        }

        WindowsListBox.Items.Refresh();
    }

    private void AddWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule)
        {
            return;
        }

        var window = NewWindow();
        rule.Windows.Add(window);
        _windows.Add(window);
        WindowsListBox.SelectedItem = window;
    }

    private void RemoveWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule || SelectedWindow is not { } window)
        {
            return;
        }

        rule.Windows.Remove(window);
        _windows.Remove(window);
    }

    private void PresetWeekdaysButton_OnClick(object sender, RoutedEventArgs e)
        => SetDays(true, true, true, true, true, false, false);

    private void PresetAllDaysButton_OnClick(object sender, RoutedEventArgs e)
        => SetDays(true, true, true, true, true, true, true);

    private void PresetClearDaysButton_OnClick(object sender, RoutedEventArgs e)
        => SetDays(false, false, false, false, false, false, false);

    private void SetDays(bool sun, bool mon, bool tue, bool wed, bool thu, bool fri, bool sat)
    {
        _suppressEvents = true;
        DaySun.IsChecked = sun;
        DayMon.IsChecked = mon;
        DayTue.IsChecked = tue;
        DayWed.IsChecked = wed;
        DayThu.IsChecked = thu;
        DayFri.IsChecked = fri;
        DaySat.IsChecked = sat;
        _suppressEvents = false;
        WindowField_OnChanged(this, new RoutedEventArgs());
    }

    // ==================== google accounts ====================

    private void AccountsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var account = SelectedAccount;
        AccountEditorPanel.IsEnabled = account is not null;

        _suppressEvents = true;
        try
        {
            _accountSites.Clear();
            _accountWindows.Clear();

            if (account is null)
            {
                AccountEmailBox.Text = string.Empty;
                return;
            }

            AccountEmailBox.Text = account.Email;
            AccountEnabledBox.IsChecked = account.Enabled;

            foreach (var checkBox in ServicesPanel.Children.OfType<CheckBox>())
            {
                var key = checkBox.Tag as string ?? string.Empty;
                checkBox.IsChecked = account.Services.Contains(key, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var site in account.Sites)
            {
                _accountSites.Add(site);
            }

            foreach (var window in account.Windows)
            {
                _accountWindows.Add(window);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void AddAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        var account = new GoogleAccountRule
        {
            Email = "name@gmail.com",
            Enabled = false,
            Windows = { NewWindow() }
        };

        _accounts.Add(account);
        AccountsListBox.SelectedItem = account;
        SetStatus("נוסף חשבון. הזן כתובת מייל, בחר שירותים וקבע לוח זמנים.", false);
    }

    private void DeleteAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is { } account)
        {
            _accounts.Remove(account);
        }
    }

    private void AccountField_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedAccount is not { } account)
        {
            return;
        }

        account.Email = AccountEmailBox.Text.Trim();
        account.Enabled = AccountEnabledBox.IsChecked == true;
        AccountsListBox.Items.Refresh();
    }

    private void ServiceCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedAccount is not { } account)
        {
            return;
        }

        account.Services = ServicesPanel.Children.OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string ?? string.Empty)
            .Where(key => key.Length > 0)
            .ToList();
    }

    private void AddAccountSiteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        AddSiteToAccount(account, NewSiteBox.Text);
        NewSiteBox.Text = string.Empty;
    }

    private void AddSiteToAccount(GoogleAccountRule account, string rawOrigin)
    {
        var origin = ConfigurationValidation.NormalizeOrigin(rawOrigin);
        if (origin.Length == 0)
        {
            SetStatus("כתובת אתר לא תקינה. הפורמט הוא https://example.com", true);
            return;
        }

        if (account.Sites.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus("האתר כבר קיים בחשבון הזה.", true);
            return;
        }

        account.Sites.Add(origin);
        if (ReferenceEquals(account, SelectedAccount))
        {
            _accountSites.Add(origin);
        }

        SetStatus($"{origin} נוסף לחשבון {account.Email}.", false);
    }

    private void RemoveAccountSiteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account || AccountSitesListBox.SelectedItem is not string site)
        {
            return;
        }

        account.Sites.Remove(site);
        _accountSites.Remove(site);
    }

    private void AddAlwaysWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        var window = AlwaysWindow();
        account.Windows.Add(window);
        _accountWindows.Add(window);
        AccountWindowsListBox.SelectedItem = window;
    }

    private void AccountWindowsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        AccountWindowDelayBox.Text = SelectedAccountWindow?.ActivationDelaySeconds.ToString() ?? "0";
    }

    private void AccountWindowDelayBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || SelectedAccountWindow is not { } window)
        {
            return;
        }

        if (!int.TryParse(AccountWindowDelayBox.Text.Trim(), out var delay) || delay is < 0 or > 86_400)
        {
            SetStatus("השהיית החשבון חייבת להיות מספר בין 0 ל־86400 שניות.", true);
            return;
        }

        window.ActivationDelaySeconds = delay;
        AccountWindowsListBox.Items.Refresh();
    }

    private void RemoveAccountWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account || AccountWindowsListBox.SelectedItem is not ScheduleWindow window)
        {
            return;
        }

        account.Windows.Remove(window);
        _accountWindows.Remove(window);
    }

    private void CopyScheduleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        if (SelectedRule is not { } rule)
        {
            SetStatus("קודם בחר כלל בלשונית 'אפליקציות', ואז חזור לכאן.", true);
            return;
        }

        account.Windows = rule.Windows.Select(CloneWindow).ToList();
        _accountWindows.Clear();
        foreach (var window in account.Windows)
        {
            _accountWindows.Add(window);
        }

        AccountWindowsListBox.SelectedItem = _accountWindows.FirstOrDefault();
        SetStatus($"לוח הזמנים של '{rule.Name}' הועתק לחשבון {account.Email}.", false);
    }

    // ==================== discovered sites ====================

    private void PromoteDiscoveredButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DiscoveredListBox.SelectedItem is not DiscoveredSite site)
        {
            return;
        }

        var account = _accounts.FirstOrDefault(item =>
            string.Equals(item.Email, site.Email, StringComparison.OrdinalIgnoreCase)) ?? SelectedAccount;

        if (account is null)
        {
            SetStatus("לא נמצא חשבון מתאים. צור חשבון בלשונית 'חשבונות Google' ובחר אותו.", true);
            return;
        }

        AddSiteToAccount(account, site.Origin);
        MainTabs.SelectedIndex = 1;
        AccountsListBox.SelectedItem = account;
    }

    private void DismissDiscoveredButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DiscoveredListBox.SelectedItem is not DiscoveredSite site)
        {
            return;
        }

        site.Dismissed = true;
        _discovered.Remove(site);
    }

    // ==================== websites ====================

    private void AddDomainButton_OnClick(object sender, RoutedEventArgs e)
    {
        var domain = PolicyEngine.NormalizeDomain(NewDomainBox.Text);
        if (!ConfigurationValidation.IsValidDomain(domain))
        {
            SetStatus("דומיין לא תקין. לדוגמה: example.com", true);
            return;
        }

        if (_domains.Any(rule => string.Equals(rule.Domain, domain, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("הדומיין כבר קיים ברשימה.", true);
            return;
        }

        _domains.Add(new WebsiteRule { Name = domain, Domain = domain, Enabled = true, Windows = { AlwaysWindow() } });
        NewDomainBox.Text = string.Empty;
    }

    private void DomainsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || DomainsListBox.SelectedItem is not WebsiteRule rule)
        {
            WebsiteDelayBox.Text = "0";
            return;
        }

        WebsiteDelayBox.Text = rule.Windows.FirstOrDefault()?.ActivationDelaySeconds.ToString() ?? "0";
    }

    private void WebsiteDelayBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || DomainsListBox.SelectedItem is not WebsiteRule rule)
        {
            return;
        }

        if (!int.TryParse(WebsiteDelayBox.Text.Trim(), out var delay) || delay is < 0 or > 86_400)
        {
            SetStatus("השהיית האתר חייבת להיות מספר בין 0 ל־86400 שניות.", true);
            return;
        }

        foreach (var window in rule.Windows)
        {
            window.ActivationDelaySeconds = delay;
        }

        DomainsListBox.Items.Refresh();
    }

    private void RemoveDomainButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DomainsListBox.SelectedItem is WebsiteRule rule)
        {
            _domains.Remove(rule);
            WebsiteDelayBox.Text = "0";
        }
    }

    private void AddDomainAlwaysWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DomainsListBox.SelectedItem is not WebsiteRule rule)
        {
            return;
        }

        var window = AlwaysWindow();
        if (int.TryParse(WebsiteDelayBox.Text.Trim(), out var delay) && delay is >= 0 and <= 86_400)
        {
            window.ActivationDelaySeconds = delay;
        }

        rule.Windows.Add(window);
        SetStatus($"נוסף חלון 'כל הזמן' ל־{rule.Domain}.", false);
    }

    // ==================== status ====================

    private async void RefreshStatusButton_OnClick(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private async void ClearSafeModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            if (MessageBox.Show(
                    "לפני ביטול מצב בטוח: ודא שהכללים שגרמו לבעיה תוקנו או הושבתו.\n\nלהמשיך?",
                    "ביטול מצב בטוח",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var response = await _pipeClient.ClearSafeModeAsync(_applicationPassword);
            if (!response.Ok)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            await RefreshStatusAsync();
            SetStatus("מצב בטוח בוטל. האכיפה תחזור לפעול לאחר תקופת החסד.", false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetStatus(exception.Message, true);
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (!_authenticated)
        {
            return;
        }

        try
        {
            var response = await _pipeClient.GetStatusAsync(_applicationPassword);
            if (!response.Ok || response.Status is null)
            {
                ServiceStatusText.Text = "לא ניתן לקרוא את מצב השירות.";
                return;
            }

            var status = response.Status;
            ServiceStatusText.Text =
                $"אכיפה: {(status.EnforcementActive ? "פעילה" : "מושבתת")}\n" +
                $"מצב בטוח: {(status.SafeMode ? "כן" : "לא")}\n" +
                $"סיבה: {status.Reason}\n" +
                $"חוקי חסימת אינטרנט פעילים: {status.ActiveNetworkBlocks}\n" +
                $"דפדפנים חסומים להפעלה: {status.BlockedBrowserLaunches}\n" +
                $"דפדפנים לא מאושרים שנמצאו בסריקה: {status.HiddenBrowsersFound}\n" +
                $"השירות פועל מאז: {status.ServiceStartedUtc.ToLocalTime():dd/MM/yyyy HH:mm}\n" +
                $"גרסה: {status.Version}";

            PendingChangeText.Text = status.PendingChangeSummary.Length > 0
                ? status.PendingChangeSummary
                : "אין שינוי ממתין.";

            ServiceStatusText.Foreground = status.SafeMode ? Brushes.DarkRed : Brushes.Black;
            HeaderStatusText.Text = status.SafeMode ? "⚠ השירות במצב בטוח — האכיפה מושבתת" : string.Empty;
            HeaderStatusText.Foreground = Brushes.DarkRed;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ServiceStatusText.Text = exception.Message;
        }
    }


    // ==================== browser lockdown ====================

    private void DetectBrowsersButton_OnClick(object sender, RoutedEventArgs e)
    {
        var added = 0;
        foreach (var path in BrowserIdentification.DefaultApprovedPaths())
        {
            if (!_approvedBrowsers.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _approvedBrowsers.Add(path);
                added++;
            }
        }

        SetStatus(
            added == 0
                ? "לא נמצאו דפדפנים חדשים לאישור. ייתכן שהם כבר ברשימה."
                : $"נוספו {added} דפדפנים מאושרים. שים לב שזו הקלה, ולכן היא עשויה להמתין את זמן הצינון.",
            false);
    }

    private void AddApprovedBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "בחר קובץ הפעלה של דפדפן מאושר",
            Filter = "קובצי הפעלה (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = Path.GetFullPath(dialog.FileName);
        if (_approvedBrowsers.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SetStatus("הדפדפן כבר ברשימת המאושרים.", true);
            return;
        }

        _approvedBrowsers.Add(path);
        SetStatus("הדפדפן נוסף. זו הקלה, ולכן היא עשויה להמתין את זמן הצינון.", false);
    }

    private void RemoveApprovedBrowserButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ApprovedBrowsersListBox.SelectedItem is string path)
        {
            _approvedBrowsers.Remove(path);
        }
    }

    // ==================== change control ====================

    private async void CancelPendingButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureAuthenticated();
            var response = await _pipeClient.CancelPendingChangeAsync(_applicationPassword);
            if (!response.Ok || response.Configuration is null)
            {
                throw new UnauthorizedAccessException(response.Error);
            }

            _configuration = response.Configuration;
            LoadConfigurationIntoUi();
            SetStatus("השינוי הממתין בוטל. ההגדרה המחמירה נשארת בתוקף.", false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetStatus(exception.Message, true);
        }
    }

    // ==================== helpers ====================

    private static ScheduleWindow NewWindow() => new()
    {
        Enabled = true,
        Days = new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday },
        Start = "23:00",
        End = "07:00"
    };

    private static ScheduleWindow AlwaysWindow() => new()
    {
        Enabled = true,
        AllDay = true,
        Days = Enum.GetValues<DayOfWeek>().ToList()
    };

    private static ScheduleWindow CloneWindow(ScheduleWindow source) => new()
    {
        Enabled = source.Enabled,
        Days = source.Days.ToList(),
        Start = source.Start,
        End = source.End,
        AllDay = source.AllDay,
        ActivationDelaySeconds = source.ActivationDelaySeconds
    };

    private static int ParseInt(string value, int fallback, string fieldName)
    {
        if (int.TryParse(value.Trim(), out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        throw new InvalidOperationException($"הערך בשדה '{fieldName}' חייב להיות מספר שלם אי־שלילי.");
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated || string.IsNullOrEmpty(_applicationPassword))
        {
            throw new UnauthorizedAccessException("יש לאמת קודם את סיסמת האפליקציה.");
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.DarkRed : Brushes.DarkGreen;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException
            or UnauthorizedAccessException
            or ArgumentException
            or IOException
            or TimeoutException;
}
