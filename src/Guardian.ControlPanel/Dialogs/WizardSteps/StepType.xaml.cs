using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepType : UserControl
    {
        public event EventHandler? ValidityChanged;

        public bool IsValid => SelectedType switch
        {
            RuleType.App => !string.IsNullOrWhiteSpace(SelectedValue),
            RuleType.Site => !string.IsNullOrWhiteSpace(TxtSiteUrl.Text.Trim()),
            // An account rule blocks either Google services or third party sites that
            // are reached through Sign in with Google. At least one of them is required.
            RuleType.Account => !string.IsNullOrWhiteSpace(TxtEmail.Text.Trim())
                && (GetSelectedServices().Count > 0 || GetSelectedSites().Count > 0),
            _ => false
        };

        public RuleType SelectedType { get; private set; } = RuleType.None;
        public string? SelectedValue { get; private set; }

        private Border? _lastCard;

        public StepType()
        {
            InitializeComponent();
        }

        public enum RuleType { None, App, Site, Account }

        private void SelectCard(Border card, RuleType type)
        {
            // Reset previous selection
            if (_lastCard != null)
            {
                _lastCard.BorderBrush = (Brush)Application.Current.FindResource("BrushLine");
                _lastCard.BorderThickness = new Thickness(1);
            }

            // Highlight new selection
            card.BorderBrush = (Brush)Application.Current.FindResource("BrushTeal");
            card.BorderThickness = new Thickness(2);
            _lastCard = card;
            SelectedType = type;

            // Show/hide conditional fields
            AppField.Visibility = type == RuleType.App ? Visibility.Visible : Visibility.Collapsed;
            SiteField.Visibility = type == RuleType.Site ? Visibility.Visible : Visibility.Collapsed;
            AccountField.Visibility = type == RuleType.Account ? Visibility.Visible : Visibility.Collapsed;

            // Hide error
            ErrorBorder.Visibility = Visibility.Collapsed;

            ValidityChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CardApp_Click(object sender, MouseButtonEventArgs e)
            => SelectCard(CardApp, RuleType.App);

        private void CardSite_Click(object sender, MouseButtonEventArgs e)
            => SelectCard(CardSite, RuleType.Site);

        private void CardAccount_Click(object sender, MouseButtonEventArgs e)
            => SelectCard(CardAccount, RuleType.Account);

        private void BtnBrowseApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "בחר קובץ aplikace"
            };
            if (dialog.ShowDialog() == true)
            {
                SelectedValue = dialog.FileName;
                BtnBrowseApp.Content = System.IO.Path.GetFileName(dialog.FileName);
            }
        }

        private void BtnDiscoveredSites_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Open discovered sites dialog if needed
        }

        /// <summary>
        /// Show inline error when user clicks Next without a complete selection.
        /// </summary>
        public void ShowError()
        {
            ErrorText.Text = SelectedType switch
            {
                RuleType.App => "יש לבחור קובץ אפליקציה (.exe)",
                RuleType.Site => "יש להזין כתובת אתר",
                RuleType.Account => string.IsNullOrWhiteSpace(TxtEmail.Text.Trim())
                    ? "יש להזין כתובת אימייל"
                    : "יש לסמן לפחות שירות אחד, או להוסיף לפחות אתר אחד לחסימה",
                _ => "בחר סוג כלל"
            };
            ErrorBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Get the identified value based on selected type.
        /// </summary>
        public string GetIdentifiedValue()
        {
            return SelectedType switch
            {
                RuleType.App => SelectedValue ?? "",
                RuleType.Site => TxtSiteUrl.Text.Trim(),
                RuleType.Account => TxtEmail.Text.Trim(),
                _ => ""
            };
        }

        /// <summary>
        /// The Google service keys the parent picked for this account rule.
        /// </summary>
        public System.Collections.Generic.List<string> GetSelectedServices()
        {
            var services = new System.Collections.Generic.List<string>();
            if (ServicesPanel is null) return services;
            foreach (var child in ServicesPanel.Children)
            {
                if (child is CheckBox box && box.IsChecked == true && box.Tag is string key
                    && !string.IsNullOrWhiteSpace(key))
                {
                    services.Add(key);
                }
            }
            return services;
        }

        /// <summary>
        /// Prefill the step for editing an existing account rule: email, services,
        /// third-party sites and the account card selection.
        /// </summary>
        public void LoadForEdit(GoogleAccountRule rule)
        {
            TxtEmail.Text = rule.Email ?? string.Empty;
            foreach (var child in ServicesPanel.Children)
            {
                if (child is CheckBox box && box.Tag is string key)
                {
                    box.IsChecked = rule.Services.Contains(key, StringComparer.OrdinalIgnoreCase);
                }
            }

            _accountSites.Clear();
            _accountSites.AddRange(rule.Sites ?? new List<string>());
            RefreshAccountSitesList();
            SelectCard(CardAccount, RuleType.Account);
        }

        // ==================== Account third-party sites ====================

        private readonly System.Collections.Generic.List<string> _accountSites = new();

        /// <summary>
        /// Third party site origins this account rule blocks. Values are stored as
        /// full https origins so the browser extension can match and turn each one
        /// into a blocking rule directly.
        /// </summary>
        public System.Collections.Generic.List<string> GetSelectedSites()
        {
            return _accountSites.ToList();
        }

        private void BtnAddAccountSite_Click(object sender, RoutedEventArgs e)
            => AddAccountSiteFromInput();

        private void TxtAccountSite_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddAccountSiteFromInput();
                e.Handled = true;
            }
        }

        private void BtnRemoveAccountSite_Click(object sender, RoutedEventArgs e)
        {
            var selected = AccountSitesList.SelectedItems.Cast<string>().ToList();
            foreach (var site in selected)
            {
                _accountSites.RemoveAll(existing =>
                    string.Equals(existing, site, StringComparison.OrdinalIgnoreCase));
            }
            RefreshAccountSitesList();
            ValidityChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddAccountSiteFromInput()
        {
            var normalized = NormalizeSiteOrigin(TxtAccountSite.Text);
            if (normalized.Length == 0)
            {
                ErrorText.Text = "יש להזין כתובת אתר תקינה, למשל example.com";
                ErrorBorder.Visibility = Visibility.Visible;
                return;
            }

            if (!_accountSites.Any(existing =>
                    string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                _accountSites.Add(normalized);
            }

            TxtAccountSite.Clear();
            RefreshAccountSitesList();
            ValidityChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string NormalizeSiteOrigin(string input)
        {
            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            if (!trimmed.Contains("://", StringComparison.Ordinal))
            {
                trimmed = "https://" + trimmed;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                || uri.HostNameType != UriHostNameType.Dns && uri.HostNameType != UriHostNameType.IPv4
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                return string.Empty;
            }

            return $"https://{uri.Host.ToLowerInvariant()}";
        }

        private void RefreshAccountSitesList()
        {
            AccountSitesList.ItemsSource = null;
            AccountSitesList.ItemsSource = _accountSites.ToList();
        }
    }
}
