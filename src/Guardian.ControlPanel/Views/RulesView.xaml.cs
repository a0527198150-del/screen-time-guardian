using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class RulesView : UserControl
{
    private readonly ObservableCollection<ApplicationRule> _appRules = new();
    private readonly ObservableCollection<WebsiteRule> _siteRules = new();
    private readonly ObservableCollection<GoogleAccountRule> _accountRules = new();

    private string _currentFilter = "all";

    public event EventHandler? NewRuleRequested;
    public event EventHandler<ApplicationRule>? EditAppRuleRequested;
    public event EventHandler<WebsiteRule>? EditSiteRuleRequested;
    public event EventHandler<GoogleAccountRule>? EditAccountRuleRequested;
    public event EventHandler<ScheduledRule>? DeleteRuleRequested;
    public event EventHandler<ScheduledRule>? ToggleRuleRequested;

    public RulesView()
    {
        InitializeComponent();
    }

    public void Show(ConfigurationDocument config)
    {
        _appRules.Clear();
        _siteRules.Clear();
        _accountRules.Clear();

        foreach (var rule in config.Applications) _appRules.Add(rule);
        foreach (var rule in config.Websites) _siteRules.Add(rule);
        foreach (var rule in config.GoogleAccounts) _accountRules.Add(rule);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        RulesList.Items.Clear();

        IEnumerable<ScheduledRule> items = _currentFilter switch
        {
            "apps" => _appRules.Cast<ScheduledRule>(),
            "sites" => _siteRules.Cast<ScheduledRule>(),
            "accounts" => _accountRules.Cast<ScheduledRule>(),
            _ => _appRules.Cast<ScheduledRule>()
                .Concat(_siteRules.Cast<ScheduledRule>())
                .Concat(_accountRules.Cast<ScheduledRule>())
        };

        foreach (var rule in items)
        {
            var card = new RuleCard();
            card.Bind(rule);
            card.EditRequested += (_, _) => OnEditRequested(rule);
            card.DeleteRequested += (_, _) => OnDeleteRequested(rule);
            card.ToggleChanged += (_, _) => ToggleRuleRequested?.Invoke(this, rule);
            RulesList.Items.Add(card);
        }

        EmptyState.Visibility = RulesList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEditRequested(ScheduledRule rule)
    {
        switch (rule)
        {
            case ApplicationRule app:
                EditAppRuleRequested?.Invoke(this, app);
                break;
            case WebsiteRule site:
                EditSiteRuleRequested?.Invoke(this, site);
                break;
            case GoogleAccountRule account:
                EditAccountRuleRequested?.Invoke(this, account);
                break;
        }
    }

    private void OnDeleteRequested(ScheduledRule rule)
    {
        var name = rule switch
        {
            WebsiteRule w => w.Domain,
            GoogleAccountRule a => a.Email,
            ApplicationRule app => app.Name,
            _ => "כלל"
        };

        if (MessageBox.Show($"למחוק את '{name}'?", "אישור מחיקה",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            DeleteRuleRequested?.Invoke(this, rule);
            ApplyFilter();
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _currentFilter = FilterAll.IsChecked == true ? "all"
            : FilterApps.IsChecked == true ? "apps"
            : FilterSites.IsChecked == true ? "sites"
            : FilterAccounts.IsChecked == true ? "accounts"
            : "all";
        ApplyFilter();
    }

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        NewRuleRequested?.Invoke(this, EventArgs.Empty);
    }
}
