using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Guardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepType : UserControl
    {
        public event EventHandler? ValidityChanged;

        public bool IsValid => SelectedType != RuleType.None;

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
        /// Show inline error when user clicks Next without selecting a type.
        /// </summary>
        public void ShowError()
        {
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
    }
}
