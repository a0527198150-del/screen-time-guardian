using System;
using System.Windows;
using System.Windows.Controls;

namespace ScreenTimeGuardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepSchedule : UserControl
    {
        public event EventHandler? ValidityChanged;

        public bool IsValid => true;

        private bool _suppressEvents;

        public string StartTime => $"{TxtStartHour.Text.PadLeft(2, '0')}:{TxtStartMin.Text.PadLeft(2, '0')}";
        public string EndTime => $"{TxtEndHour.Text.PadLeft(2, '0')}:{TxtEndMin.Text.PadLeft(2, '0')}";

        public StepSchedule()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateSummary();
        }

        /// <summary>
        /// Apply a preset schedule to the WeekGrid.
        /// </summary>
        public void ApplyPreset(string preset)
        {
            _suppressEvents = true;

            switch (preset)
            {
                case "shabbat":
                    // Friday 18:00 – Saturday 20:00
                    ScheduleGrid.ClearAll();
                    ScheduleGrid.SetDayHours(5, 18, 24);
                    ScheduleGrid.SetDayHours(6, 0, 20);
                    break;
                case "work":
                    // Sunday–Thursday 09:00–17:00
                    ScheduleGrid.ClearAll();
                    for (var d = 0; d <= 4; d++) ScheduleGrid.SetDayHours(d, 9, 17);
                    break;
                case "night":
                    // Every day 22:00–07:00
                    ScheduleGrid.ClearAll();
                    for (var d = 0; d <= 6; d++) ScheduleGrid.SetDayHours(d, 22, 7);
                    break;
                case "all":
                    ScheduleGrid.SelectAll();
                    break;
            }

            _suppressEvents = false;
            UpdateSummary();
        }

        private void BtnShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string preset)
            {
                ApplyPreset(preset);
            }
        }

        private void TimeToggle_Click(object sender, RoutedEventArgs e)
        {
            var show = TimePanel.Visibility != Visibility.Visible;
            TimePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TimeToggle.Content = show ? "שעות מדויקות ▴" : "שעות מדויקות ▾";
        }

        /// <summary>
        /// Get the schedule summary for Step 3 confirmation.
        /// </summary>
        public string GetSummary()
        {
            if (ScheduleGrid.BlockedHourCount == 0) return "לא נבחרו שעות";
            return BuildSummaryText();
        }

        private void UpdateSummary()
        {
            if (ScheduleGrid.BlockedHourCount == 0)
            {
                SummaryText.Text = "בחר שעות ברשת למעלה או באמצעות קיצור דרך";
                return;
            }
            SummaryText.Text = BuildSummaryText();
        }

        private string BuildSummaryText()
        {
            var dayNames = new[] { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };
            var firstDay = -1;
            var lastDay = -1;
            var firstHour = -1;
            var lastHour = -1;

            for (var d = 0; d < 7; d++)
            {
                for (var h = 0; h < 24; h++)
                {
                    if (!ScheduleGrid.IsSelected(d, h)) continue;
                    if (firstDay == -1) firstDay = d;
                    lastDay = d;
                    if (firstHour == -1 || h < firstHour) firstHour = h;
                    if (h > lastHour) lastHour = h;
                }
            }

            if (firstDay == -1) return "לא נבחרו שעות";

            var days = firstDay == lastDay
                ? dayNames[firstDay]
                : $"{dayNames[firstDay]}–{dayNames[lastDay]}";

            return $"נבחרו {ScheduleGrid.BlockedHourCount} שעות · {days} · {firstHour:D2}:00–{lastHour:D2}:00";
        }
    }
}
