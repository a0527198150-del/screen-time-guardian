using System;
using System.Windows;
using System.Windows.Controls;
using ScreenTimeGuardian.ControlPanel.Dialogs;

namespace ScreenTimeGuardian.ControlPanel.Dialogs.WizardSteps
{
    public partial class StepSchedule : UserControl
    {
        public event EventHandler? ValidityChanged;

        public bool IsValid => true;

        private bool _suppressEvents;

        private int _startHour = 22;
        private int _startMinute;
        private int _endHour = 7;
        private int _endMinute;

        public string StartTime => $"{_startHour:00}:{_startMinute:00}";
        public string EndTime => $"{_endHour:00}:{_endMinute:00}";

        public StepSchedule()
        {
            InitializeComponent();
            ScheduleGrid.ScheduleChanged += (_, _) =>
            {
                UpdateSummary();
                NotifyValidityChanged();
            };
            Loaded += (_, _) =>
            {
                UpdateTimeButtons();
                UpdateSummary();
            };
        }

        private void UpdateTimeButtons()
        {
            BtnStartTime.Content = StartTime;
            BtnEndTime.Content = EndTime;
        }

        private void PickStartTime_Click(object sender, RoutedEventArgs e)
            => PickTime(ref _startHour, ref _startMinute);

        private void PickEndTime_Click(object sender, RoutedEventArgs e)
            => PickTime(ref _endHour, ref _endMinute);

        private void PickTime(ref int hour, ref int minute)
        {
            var dialog = new TimePickerDialog(hour, minute) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                hour = dialog.SelectedTime.Hours;
                minute = dialog.SelectedTime.Minutes;
                UpdateTimeButtons();
            }
        }

        private void ApplyTime_Click(object sender, RoutedEventArgs e)
        {
            _suppressEvents = true;
            ScheduleGrid.ClearAll();
            for (var day = 0; day < 7; day++)
            {
                ScheduleGrid.SetDayHours(day, _startHour, _endHour);
            }
            _suppressEvents = false;
            NotifyValidityChanged();
            UpdateSummary();
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
            NotifyValidityChanged();
            UpdateSummary();
        }

        private void NotifyValidityChanged()
        {
            if (!_suppressEvents)
                ValidityChanged?.Invoke(this, EventArgs.Empty);
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
