using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenTimeGuardian.Contracts;
using ScreenTimeGuardian.ControlPanel.Dialogs.WizardSteps;

namespace ScreenTimeGuardian.ControlPanel;

public partial class RuleWizard : Window
{
    private int _step = 1;
    private readonly StepType _stepType = new();
    private readonly StepSchedule _stepSchedule = new();
    private readonly StepConfirm _stepConfirm = new();
    private bool _hasChanges;
    public ScheduledRule? Result { get; private set; }

    public RuleWizard()
    {
        InitializeComponent();
        _stepType.ValidityChanged += (_, _) => { };
        _stepSchedule.ValidityChanged += (_, _) => { };
        _stepConfirm.SaveRequested += (_, _) => { CreateRule(); DialogResult = true; Close(); };
        ShowStep(1);
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e) => PromptClose();
    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);
    private void Cancel_Click(object sender, RoutedEventArgs e) => PromptClose();

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1 && !_stepType.IsValid) { _stepType.ShowError(); return; }
        if (_step == 1) _hasChanges = true;
        if (_step == 3) { CreateRule(); DialogResult = true; Close(); return; }
        ShowStep(_step + 1);
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 1, 3);
        Step1Dot.Fill = (System.Windows.Media.Brush)FindResource(_step >= 1 ? "BrushTeal" : "BrushLine");
        Step2Dot.Fill = (System.Windows.Media.Brush)FindResource(_step >= 2 ? "BrushTeal" : "BrushLine");
        Step3Dot.Fill = (System.Windows.Media.Brush)FindResource(_step >= 3 ? "BrushTeal" : "BrushLine");
        BackButton.Visibility = _step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _step == 3 ? "שמור" : "הבא";

        switch (_step)
        {
            case 1: StepTitle.Text = "מה לחסום?"; StepContent.Content = _stepType; break;
            case 2: StepTitle.Text = "מתי לחסום?"; StepContent.Content = _stepSchedule; break;
            case 3:
                StepTitle.Text = "אישור";
                _stepConfirm.SetConfirmation(BuildConfirmation());
                StepContent.Content = _stepConfirm;
                break;
        }
    }

    private string BuildConfirmation()
    {
        var typeName = _stepType.SelectedType switch
        {
            StepType.RuleType.App => "אפליקציה", StepType.RuleType.Site => "אתר",
            StepType.RuleType.Account => "חשבון Google", _ => "כלל"
        };
        return $"כלל מסוג \"{typeName}\" ייחסם ב{_stepSchedule.GetSummary()}";
    }

    private void PromptClose()
    {
        if (!_hasChanges) { Close(); return; }
        var dialog = new ConfirmDialog
        {
            DialogTitleText = "לצאת בלי לשמור?",
            Message = "יש לך שינויים שלא נשמרו. לצאת?",
            ConfirmText = "צא", CancelText = " המשך לערוך"
        };
        if (dialog.ShowDialog() == true) Close();
    }

    private void CreateRule()
    {
        var windows = _stepSchedule.ScheduleGrid.ToScheduleWindows();
        if (windows.Count == 0)
            windows.Add(new ScheduleWindow { Enabled = true, AllDay = false, Start = "23:00", End = "07:00" });

        // The rule must carry the target the user picked in step 1, or it is an
        // empty shell: it appears in the list but never matches anything and
        // therefore never blocks. Every rule type carries its own payload.
        var value = _stepType.GetIdentifiedValue();

        Result = _stepType.SelectedType switch
        {
            StepType.RuleType.App => new ApplicationRule
            {
                Name = "כלל חדש",
                Enabled = true,
                Windows = windows,
                Targets = string.IsNullOrWhiteSpace(value)
                    ? new List<AppTarget>()
                    : new List<AppTarget>
                    {
                        new()
                        {
                            DisplayName = System.IO.Path.GetFileNameWithoutExtension(value),
                            ExecutablePath = value
                        }
                    }
            },
            StepType.RuleType.Site => new WebsiteRule
            {
                Name = "אתר חדש",
                Enabled = true,
                Windows = windows,
                Domain = value
            },
            StepType.RuleType.Account => new GoogleAccountRule
            {
                Name = "חשבון חדש",
                Enabled = true,
                Windows = windows,
                Email = value,
                Services = new List<string>
                {
                    "gmail", "drive", "docs", "calendar", "meet", "photos",
                    "search", "youtube", "gemini", "maps", "translate",
                    "keep", "news", "finance", "groups"
                }
            },
            _ => null
        };
    }
}
