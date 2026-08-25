using System;
using System.Windows;
using System.Windows.Controls;

namespace Guardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepSchedule : UserControl
    {
        public event EventHandler? ValidityChanged;

        public bool IsValid => true;

        private bool _suppressEvents;

        public StepSchedule()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateSummary();
        }

        /// <summary>
        /// Apply a preset schedule to the WeekGrid.
        /// Called by StepConfirm to apply shortcuts.
        /// </summary>
        public void ApplyPreset(string preset)
        {
            _suppressEvents = true;

            switch (preset)
            {
                case "shabbat":
                    // Friday evening to Saturday evening
                    ScheduleGrid.ClearAll();
                    ScheduleGrid.SetRange(5, 18, 6, 20, true); // Friday 18:00 – Saturday 20:00
                    break;
                case "work":
                    // Sunday–Thursday 9:00–17:00
                    ScheduleGrid.ClearAll();
                    ScheduleGrid.SetRange(0, 9, 4, 17, true);
                    break;
                case "night":
                    // Every day 22:00–07:00
                    ScheduleGrid.ClearAll();
                    ScheduleGrid.SetRange(0, 22, 6, 7, true);
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

        private void TimeExpander_Expanded(object sender, RoutedEventArgs e) { }

        private void TimeExpander_Collapsed(object sender, RoutedEventArgs e) { }

        /// <summary>
        /// Get the schedule summary for Step 3 confirmation.
        /// </summary>
        public string GetSummary()
        {
            var stats = ScheduleGrid.GetStats();
            if (stats.TotalHours == 0) return "לא נבחרו שעות";

            var dayNames = new[] { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };
            int firstDay = -1, lastDay = -1, firstHour = -1, lastHour = -1;

            for (int d = 0; d < 7; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    if (ScheduleGrid.IsSelected(d, h))
                    {
                        if (firstDay == -1) firstDay = d;
                        lastDay = d;
                        if (firstHour == -1 || h < firstHour) firstHour = h;
                        if (h > lastHour) lastHour = h;
                    }
                }
            }

            string days = firstDay == lastDay
                ? dayNames[firstDay]
                : $"{dayNames[firstDay]}–{dayNames[lastDay]}";

            string start = $"{firstHour:D2}:00";
            string end = $"{lastHour:D2}:00";

            return $"{days} · {start}–{end} · {stats.TotalHours} שעות";
        }

        private void UpdateSummary()
        {
            var stats = ScheduleGrid.GetStats();
            if (stats.TotalHours == 0)
            {
                SummaryText.Text = "בחר שעות ברשת למעלה או באמצעות קיצור דרך";
                return;
            }

            var dayNames = new[] { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };
            int firstDay = -1, lastDay = -1, firstHour = -1, lastHour = -1;

            for (int d = 0; d < 7; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    if (ScheduleGrid.IsSelected(d, h))
                    {
                        if (firstDay == -1) firstDay = d;
                        lastDay = d;
                        if (firstHour == -1 || h < firstHour) firstHour = h;
                        if (h > lastHour) lastHour = h;
                    }
                }
            }

            string days = firstDay == lastDay
                ? dayNames[firstDay]
                : $"{dayNames[firstDay]}–{dayNames[lastDay]}";

            SummaryText.Text = $"נבחרו {stats.TotalHours} שעות · {days} · {firstHour:D2}:00–{lastHour:D2}:00";
        }
    }
}
