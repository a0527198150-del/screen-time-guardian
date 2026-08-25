using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class SettingsView : UserControl
{
    private ConfigurationDocument? _configuration;
    private List<string>? _approvedPaths;
    private string _applicationPassword = "";
    private bool _suppressEvents;

    public event EventHandler? PasswordChangeRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? RefreshPendingRequested;
    public event EventHandler? CancelPendingRequested;

    public Snackbar? Snackbar { get; set; }

    /// <summary>Well-known browser executables in standard install locations.</summary>
    private static readonly string[] BrowserCandidates =
    {
        @"Google\Chrome\Application\chrome.exe",
        @"Microsoft\Edge\Application\msedge.exe",
        @"Mozilla Firefox\firefox.exe",
        @"BraveSoftware\Brave-Browser\Application\brave.exe",
        @"Opera\launcher.exe",
        @"Vivaldi\Application\vivaldi.exe",
        @"Yandex\YandexBrowser\Application\browser.exe",
        @"Tor Browser\Browser\firefox.exe"
    };

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

        // Approved browsers — bind directly to the real list so edits persist
        AllowApprovedBrowsersBox.IsChecked = config.BrowserLockdown.AllowApprovedBrowsersWithoutExtension;
        _approvedPaths = config.BrowserLockdown.ApprovedBrowserPaths;
        RefreshApprovedList();

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

    // ==================== Approved browsers ====================

    private void RefreshApprovedList()
    {
        if (_approvedPaths is null) return;
        ApprovedBrowsersList.ItemsSource = null;
        ApprovedBrowsersList.ItemsSource = _approvedPaths.ToList();
    }

    private void AddApprovedPath(string path)
    {
        if (_approvedPaths is null) return;
        var trimmed = path.Trim();
        if (trimmed.Length == 0 || _approvedPaths.Any(existing =>
                string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _approvedPaths.Add(trimmed);
    }

    private void IdentifyBrowsers_Click(object sender, RoutedEventArgs e)
    {
        var found = new List<string>();

        foreach (var path in BrowserIdentification.DefaultApprovedPaths())
        {
            found.Add(path);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[] { programFiles, programFilesX86, localAppData }
            .Where(root => !string.IsNullOrWhiteSpace(root));

        foreach (var root in roots)
        {
            foreach (var relative in BrowserCandidates)
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    found.Add(candidate);
                }
            }
        }

        // All candidates come from curated, existence-checked locations — no
        // metadata sanity check needed (Identify does not cover Chrome/Edge).
        var added = 0;
        foreach (var path in found.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var before = _approvedPaths?.Count ?? 0;
            AddApprovedPath(path);
            if ((_approvedPaths?.Count ?? 0) > before) added++;
        }

        RefreshApprovedList();
        Snackbar?.Show(added > 0
            ? $"נמצאו {added} דפדפנים והוספו לרשימה."
            : "לא נמצאו דפדפנים חדשים.");
    }

    private void AddBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "תוכניות (*.exe)|*.exe|כל הקבצים (*.*)|*.*",
            Title = "בחר קובץ דפדפן"
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName)) return;

        if (!File.Exists(dialog.FileName))
        {
            Snackbar?.Show("הקובץ לא נמצא.");
            return;
        }

        AddApprovedPath(dialog.FileName);
        RefreshApprovedList();
        Snackbar?.Show("הדפדפן נוסף לרשימה.");
    }

    private void RemoveBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_approvedPaths is null || ApprovedBrowsersList.SelectedItems.Count == 0) return;

        var toRemove = ApprovedBrowsersList.SelectedItems.Cast<string>().ToList();
        foreach (var path in toRemove)
        {
            _approvedPaths.RemoveAll(existing =>
                string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        }
        RefreshApprovedList();
        Snackbar?.Show(toRemove.Count == 1
            ? "הדפדפן הוסר מהרשימה."
            : $"{toRemove.Count} דפדפנים הוסרו מהרשימה.");
    }

    // ==================== Pending change ====================

    private void RefreshPending_Click(object sender, RoutedEventArgs e)
        => RefreshPendingRequested?.Invoke(this, EventArgs.Empty);

    private void CancelPending_Click(object sender, RoutedEventArgs e)
        => CancelPendingRequested?.Invoke(this, EventArgs.Empty);

    // ==================== Save ====================

    private void Save_Click(object sender, RoutedEventArgs e)
        => SaveRequested?.Invoke(this, EventArgs.Empty);

    // ==================== Password ====================

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
