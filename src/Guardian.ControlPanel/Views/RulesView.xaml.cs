using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class RulesView : UserControl
{
    private readonly ObservableCollection<ApplicationRule> _appRules = new();
    private readonly ObservableCollection<WebsiteRule> _siteRules = new();
    private readonly ObservableCollection<GoogleAccountRule> _accountRules = new();

    private string _currentFilter = "all";
    private string _searchText = "";

    public event EventHandler? NewRuleRequested;
    public event EventHandler<ApplicationRule>? EditAppRuleRequested;
    public event EventHandler<WebsiteRule>? EditSiteRuleRequested;
    public event EventHandler<GoogleAccountRule>? EditAccountRuleRequested;
    public event EventHandler<ScheduledRule>? DeleteRuleRequested;
    public event EventHandler<ScheduledRule>? ToggleRuleRequested;

    public Snackbar? Snackbar { get; set; }

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

        // Show search when >8 rules
        var totalRules = _appRules.Count + _siteRules.Count + _accountRules.Count;
        SearchBox.Visibility = totalRules > 8 ? Visibility.Visible : Visibility.Collapsed;
        ShortcutHint.Visibility = totalRules > 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        // Filter_Changed can fire while XAML is still being parsed, before the
        // control tree (and thus RulesList) has been built. Guard against that so
        // startup does not crash with a NullReferenceException.
        if (RulesList is null || _appRules is null || _siteRules is null || _accountRules is null)
            return;

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

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim().ToLowerInvariant();
            items = items.Where(r => MatchesSearch(r, search));
        }

        // Sort: active first, enabled-not-active second, disabled last (with 55% opacity)
        var now = DateTimeOffset.Now;
        items = items
            .OrderBy(r => r.IsActive(now) ? 0 : r.Enabled ? 1 : 2)
            .ThenBy(r => GetNameForSort(r));

        foreach (var rule in items)
        {
            var card = new RuleCard();
            card.Bind(rule);
            card.EditRequested += (_, _) => OnEditRequested(rule);
            card.DeleteRequested += (_, _) => OnDeleteRequested(rule);
            card.ToggleChanged += (_, _) => ToggleRuleRequested?.Invoke(this, rule);

            // Disabled rules get 55% opacity
            if (!rule.Enabled)
                card.Opacity = 0.55;

            RulesList.Items.Add(card);
        }

        EmptyState.Visibility = RulesList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool MatchesSearch(ScheduledRule rule, string search)
    {
        var name = (rule.Name ?? "").ToLowerInvariant();
        if (name.Contains(search)) return true;

        var identifier = rule switch
        {
            WebsiteRule w => w.Domain,
            GoogleAccountRule a => a.Email,
            ApplicationRule app => string.Join(" ", app.Targets.Select(t => t.DisplayName)),
            _ => ""
        };
        return identifier.ToLowerInvariant().Contains(search);
    }

    private static string GetNameForSort(ScheduledRule rule)
    {
        return rule switch
        {
            ApplicationRule app => app.Name ?? "",
            WebsiteRule site => site.Domain ?? "",
            GoogleAccountRule acct => acct.Email ?? "",
            _ => ""
        };
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

        // Remove immediately
        DeleteRuleRequested?.Invoke(this, rule);
        ApplyFilter();

        // Show undo snackbar
        Snackbar?.Show($"הכלל '{name}' נמחק.", () =>
        {
            // Undo: re-add the rule
            switch (rule)
            {
                case ApplicationRule app: _appRules.Add(app); break;
                case WebsiteRule site: _siteRules.Add(site); break;
                case GoogleAccountRule account: _accountRules.Add(account); break;
            }
            ApplyFilter();
        });
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // The radio buttons may not be instantiated yet when this is raised during
        // XAML parsing. Only read them when the control tree is fully built.
        if (FilterAll is null)
            return;

        _currentFilter = FilterAll.IsChecked == true ? "all"
            : FilterApps.IsChecked == true ? "apps"
            : FilterSites.IsChecked == true ? "sites"
            : FilterAccounts.IsChecked == true ? "accounts"
            : "all";
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        ApplyFilter();
    }

    public void ClearSearch()
    {
        SearchBox.Text = "";
        _searchText = "";
    }

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        NewRuleRequested?.Invoke(this, EventArgs.Empty);
    }
}
