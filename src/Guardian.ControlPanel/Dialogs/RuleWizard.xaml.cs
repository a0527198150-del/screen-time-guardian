using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

public partial class RuleWizard : Window
{
    private int _step = 1;

    // Step 1 selections
    private string _selectedType = ""; // "app", "site", "account"

    // Step 2 data
    private readonly List<DayOfWeek> _selectedDays = new();
    private string _startTime = "23:00";
    private string _endTime = "07:00";
    private bool _allDay = false;

    // Step 1 UI elements (created in code to avoid massive XAML)
    private StackPanel? _step1Content;
    private StackPanel? _step1Detail;
    private TextBlock? _step1Error;
    private Border? _selectedAppCard;
    private Border? _selectedSiteCard;
    private Border? _selectedAccountCard;
    private TextBox? _siteDomainBox;
    private TextBox? _accountEmailBox;

    // Step 2 UI elements
    private StackPanel? _step2Content;
    private WeekGrid? _wizardWeekGrid;

    // Step 3 UI elements
    private StackPanel? _step3Content;

    // Result
    public ScheduledRule? Result { get; private set; }
    private bool _hasChanges;

    public RuleWizard()
    {
        InitializeComponent();
        ShowStep1();
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e) => PromptClose();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 2) ShowStep1();
        else if (_step == 3) ShowStep2();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => PromptClose();

    private void PromptClose()
    {
        if (!_hasChanges) { Close(); return; }

        var dialog = new ConfirmDialog
        {
            DialogTitleText = "לצאת בלי לשמור?",
            Message = "יש לך שינויים שלא נשמרו. לצאת?",
            ConfirmText = "צא",
            CancelText = " המשך לערוך"
        };
        if (dialog.ShowDialog() == true)
            Close();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            if (string.IsNullOrEmpty(_selectedType))
            {
                if (_step1Error is null)
                {
                    _step1Error = new TextBlock
                    {
                        Text = "בחר סוג כלל",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(169, 50, 38)),
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    _step1Content?.Children.Add(_step1Error);
                }
                _step1Error.Visibility = Visibility.Visible;
                return;
            }
            if (_step1Error is not null) _step1Error.Visibility = Visibility.Collapsed;
            _hasChanges = true;
            ShowStep2();
        }
        else if (_step == 2)
        {
            ShowStep3();
        }
        else if (_step == 3)
        {
            CreateRule();
            DialogResult = true;
            Close();
        }
    }

    private void UpdateProgress()
    {
        Step1Dot.Fill = new SolidColorBrush(_step >= 1 ? Color.FromRgb(14, 124, 134) : Color.FromRgb(226, 231, 238));
        Step2Dot.Fill = new SolidColorBrush(_step >= 2 ? Color.FromRgb(14, 124, 134) : Color.FromRgb(226, 231, 238));
        Step3Dot.Fill = new SolidColorBrush(_step >= 3 ? Color.FromRgb(14, 124, 134) : Color.FromRgb(226, 231, 238));

        BackButton.Visibility = _step > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (_step == 3)
        {
            NextButton.Content = "שמור";
        }
        else
        {
            NextButton.Content = "הבא";
        }
    }

    // ===== STEP 1: What to block =====

    private void ShowStep1()
    {
        _step = 1;
        UpdateProgress();
        StepTitle.Text = "מה לחסום?";

        _step1Content = new StackPanel();
        _step1Content.Margin = new Thickness(0, 8, 0, 0);

        var cardsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // App card
        _selectedAppCard = CreateTypeCard("📱", "אפליקציה", "com.youtube譬如");
        _selectedAppCard.MouseLeftButtonDown += (_, _) => SelectType("app");
        cardsPanel.Children.Add(_selectedAppCard);

        // Site card
        _selectedSiteCard = CreateTypeCard("🌐", "אתר", "com.youtube譬如");
        _selectedSiteCard.MouseLeftButtonDown += (_, _) => SelectType("site");
        cardsPanel.Children.Add(_selectedSiteCard);

        // Account card
        _selectedAccountCard = CreateTypeCard("👤", "חשבון", "Google");
        _selectedAccountCard.MouseLeftButtonDown += (_, _) => SelectType("account");
        cardsPanel.Children.Add(_selectedAccountCard);

        _step1Content.Children.Add(cardsPanel);

        // Detail panel — appears below cards when a type is selected
        _step1Detail = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        _step1Content.Children.Add(_step1Detail);

        StepContent.Content = _step1Content;
    }

    private Border CreateTypeCard(string icon, string label, string hint)
    {
        var border = new Border
        {
            Width = 160, Height = 120, Margin = new Thickness(0, 0, 12, 0),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 231, 238)),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(new TextBlock
        {
            Text = icon, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 17, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = hint, FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(90, 103, 128)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
        });

        border.Child = stack;
        return border;
    }

    private void SelectType(string type)
    {
        _selectedType = type;

        ResetCardBorder(_selectedAppCard);
        ResetCardBorder(_selectedSiteCard);
        ResetCardBorder(_selectedAccountCard);

        var selected = type switch
        {
            "app" => _selectedAppCard,
            "site" => _selectedSiteCard,
            "account" => _selectedAccountCard,
            _ => null
        };

        if (selected is not null)
        {
            selected.BorderBrush = new SolidColorBrush(Color.FromRgb(14, 124, 134));
            selected.Background = new SolidColorBrush(Color.FromRgb(224, 242, 241));
        }

        // Populate detail panel based on selected type
        _step1Detail?.Children.Clear();
        if (type == "site")
        {
            _siteDomainBox = new TextBox
            {
                Height = 40, FontSize = 15,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var hint = new TextBlock
            {
                Text = "לדוגמה: com.youtube",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 103, 128)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var discoveredBtn = new Button
            {
                Content = "הוסף מרשימת אתרים שהתגלו",
                Height = 36, Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            // Set style via resource lookup
            if (Application.Current.TryFindResource("ButtonSecondary") is Style btnStyle)
                discoveredBtn.Style = btnStyle;
            _step1Detail?.Children.Add(_siteDomainBox);
            _step1Detail?.Children.Add(hint);
            _step1Detail?.Children.Add(discoveredBtn);
        }
        else if (type == "account")
        {
            _accountEmailBox = new TextBox
            {
                Height = 40, FontSize = 15,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _step1Detail?.Children.Add(_accountEmailBox);
        }
        else if (type == "app")
        {
            var browseBtn = new Button
            {
                Content = "בחר קובץ exe…",
                Height = 40, Padding = new Thickness(20, 0, 20, 0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            if (Application.Current.TryFindResource("ButtonSecondary") is Style btnStyle)
                browseBtn.Style = btnStyle;
            _step1Detail?.Children.Add(browseBtn);
        }
    }

    private static void ResetCardBorder(Border? card)
    {
        if (card is null) return;
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 231, 238));
        card.Background = new SolidColorBrush(Colors.White);
    }

    // ===== STEP 2: When to block =====

    private void ShowStep2()
    {
        _step = 2;
        UpdateProgress();
        StepTitle.Text = "מתי לחסום?";

        _step2Content = new StackPanel();

        // Quick shortcuts
        var shortcutsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var shortcuts = new[] { ("שבת", new[] { DayOfWeek.Saturday }),
            ("שעות עבודה", new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                DayOfWeek.Wednesday, DayOfWeek.Thursday }),
            ("שעות לילה", new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                DayOfWeek.Wednesday, DayOfWeek.Thursday }),
            ("כל הזמן", Enum.GetValues<DayOfWeek>()) };

        foreach (var (label, days) in shortcuts)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource("ButtonSecondary"),
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(0, 0, 8, 8)
            };
            btn.Click += (_, _) => { _selectedDays.Clear(); _selectedDays.AddRange(days); };
            shortcutsPanel.Children.Add(btn);
        }
        _step2Content.Children.Add(shortcutsPanel);

        // WeekGrid
        _wizardWeekGrid = new WeekGrid { Height = 300, MinHeight = 200, Margin = new Thickness(0, 0, 0, 16) };
        _step2Content.Children.Add(_wizardWeekGrid);

        // Precise time fields (collapsed by default)
        var precisePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var startLabel = new TextBlock { Text = "משעה:", Style = (Style)FindResource("TypeLabel"), VerticalAlignment = VerticalAlignment.Center };
        var startBox = new TextBox { Width = 80, FlowDirection = FlowDirection.LeftToRight, Text = _startTime, Margin = new Thickness(0, 0, 12, 0) };
        var endLabel = new TextBlock { Text = "עד שעה:", Style = (Style)FindResource("TypeLabel"), VerticalAlignment = VerticalAlignment.Center };
        var endBox = new TextBox { Width = 80, FlowDirection = FlowDirection.LeftToRight, Text = _endTime };
        startBox.LostFocus += (_, _) => _startTime = startBox.Text;
        endBox.LostFocus += (_, _) => _endTime = endBox.Text;

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        timeRow.Children.Add(startLabel);
        timeRow.Children.Add(startBox);
        timeRow.Children.Add(endLabel);
        timeRow.Children.Add(endBox);
        precisePanel.Children.Add(timeRow);
        _step2Content.Children.Add(precisePanel);

        StepContent.Content = _step2Content;
    }

    // ===== STEP 3: Confirmation =====

    private void ShowStep3()
    {
        _step = 3;
        UpdateProgress();
        StepTitle.Text = "אישור";

        // Build WeekGrid windows for the summary
        var windows = _wizardWeekGrid?.ToScheduleWindows() ?? new List<ScheduleWindow>();

        _step3Content = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

        var daysStr = _selectedDays.Count > 0
            ? string.Join("–", _selectedDays.Select(HebrewDays.Name))
            : "כל הימים";
        var timeStr = _allDay ? "כל היום" : $"{_startTime} ל־{_endTime}";
        var typeName = _selectedType switch
        {
            "app" => "אפליקציה",
            "site" => "אתר",
            "account" => "חשבון Google",
            _ => "כלל"
        };

        var summary = new TextBlock
        {
            Text = $"כלל מסוג \"{typeName}\" ייחסם בימים {daysStr} בין {timeStr}",
            Style = (Style)FindResource("TypeBody"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24)
        };
        _step3Content.Children.Add(summary);

        StepContent.Content = _step3Content;
    }

    // ===== Create the rule =====

    private void CreateRule()
    {
        var windows = _wizardWeekGrid?.ToScheduleWindows() ?? new List<ScheduleWindow>();
        if (windows.Count == 0)
        {
            windows.Add(new ScheduleWindow
            {
                Enabled = true,
                AllDay = false,
                Days = Enum.GetValues<DayOfWeek>().ToList(),
                Start = _startTime,
                End = _endTime
            });
        }

        Result = _selectedType switch
        {
            "app" => new ApplicationRule
            {
                Name = "כלל חדש",
                Enabled = true,
                Windows = windows
            },
            "site" => new WebsiteRule
            {
                Name = "אתר חדש",
                Domain = "",
                Enabled = true,
                Windows = windows
            },
            "account" => new GoogleAccountRule
            {
                Email = "",
                Enabled = true,
                Windows = windows
            },
            _ => null
        };
    }
}
