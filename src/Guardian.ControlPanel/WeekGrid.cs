using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

/// <summary>
/// Keyboard and pointer editor for a seven-day, twenty-four-hour schedule.
///
/// RTL note: The parent Window sets FlowDirection="RightToLeft", which causes
/// WPF to mirror all rendering automatically. The x-coordinates used here are
/// in LOGICAL space — x=0 is the left side of the control's logical coordinate
/// system, which WPF renders on the RIGHT side of the screen in RTL mode.
/// We draw days left-to-right (Sunday on the left logically), and WPF mirrors
/// them so Sunday appears on the right visually — which is the correct Hebrew
/// order. No manual x-reversal is needed.
/// </summary>
public sealed class WeekGrid : FrameworkElement
{
    private static readonly DayOfWeek[] Days =
    {
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    };

    private readonly HashSet<(DayOfWeek Day, int Hour)> _blocked = new();
    private bool _dragging;
    private bool _erase;

    // Keyboard cursor position
    private int _cursorDayIndex = 0; // 0=Sunday .. 6=Saturday
    private int _cursorHour = 0;     // 0..23
    private bool _hasCursor = false;

    // Visual constants — tuned for legibility at 700×420 and above.
    private const double HourLabelWidth = 41;
    private const double HeaderHeight = 28;
    private const double MinCellPx = 32;

    public static readonly DependencyProperty WindowsProperty = DependencyProperty.Register(
        nameof(Windows), typeof(IEnumerable<ScheduleWindow>), typeof(WeekGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            static (owner, _) => ((WeekGrid)owner).Rebuild()));

    public IEnumerable<ScheduleWindow>? Windows
    {
        get => (IEnumerable<ScheduleWindow>?)GetValue(WindowsProperty);
        set => SetValue(WindowsProperty, value);
    }

    public event EventHandler? ScheduleChanged;

    public WeekGrid()
    {
        Focusable = true;
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => InvalidateVisual();
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    public void Refresh() => Rebuild();

    public List<ScheduleWindow> ToScheduleWindows()
    {
        var result = new List<ScheduleWindow>();
        foreach (var day in Days)
        {
            var hour = 0;
            while (hour < 24)
            {
                if (!_blocked.Contains((day, hour)))
                {
                    hour++;
                    continue;
                }

                var start = hour;
                while (hour < 24 && _blocked.Contains((day, hour))) hour++;
                var end = hour;
                result.Add(new ScheduleWindow
                {
                    Enabled = true,
                    Days = new List<DayOfWeek> { day },
                    AllDay = start == 0 && end == 24,
                    Start = $"{start:00}:00",
                    End = end == 24 ? "00:00" : $"{end:00}:00"
                });
            }
        }
        return result;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth > 0 ? ActualWidth : 740;
        var height = ActualHeight > 0 ? ActualHeight : 420;

        var cellWidth = Math.Max(MinCellPx, (width - HourLabelWidth - 7) / 7);
        var cellHeight = Math.Max(MinCellPx / 2.0, (height - HeaderHeight) / 24);
        var gridLeft = HourLabelWidth;
        var gridTop = HeaderHeight;

        // Brushes
        var freeBrush = new SolidColorBrush(Color.FromRgb(226, 231, 238));   // #E2E7EE (ClrLine)
        var blockedBrush = new SolidColorBrush(Color.FromRgb(14, 124, 134)); // Teal #0E7C86 (ClrTeal)
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 231, 238)), 1.0);
        var inkBrush = new SolidColorBrush(Color.FromRgb(20, 27, 46));       // Ink #141B2E
        var mutedBrush = new SolidColorBrush(Color.FromRgb(90, 103, 128));   // MutedInk #5A6780
        var cursorBrush = new SolidColorBrush(Color.FromRgb(14, 124, 134));  // Teal for cursor border
        var uiFont = (System.Windows.Media.FontFamily)Application.Current.FindResource("FontUi");
        var headerTypeface = new Typeface(uiFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var hourTypeface = new Typeface(uiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Day headers — logical x goes left to right (Sunday=0 to Saturday=6).
        // WPF mirrors the entire control in RTL mode, so Sunday ends up on the
        // right side of the screen automatically.
        for (var dayIndex = 0; dayIndex < Days.Length; dayIndex++)
        {
            var day = Days[dayIndex];
            var x = gridLeft + dayIndex * cellWidth;
            var label = new FormattedText(HebrewDays.Name(day), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, headerTypeface, 13, inkBrush, 1.0);
            label.TextAlignment = TextAlignment.Center;
            label.MaxTextWidth = cellWidth;
            drawingContext.DrawText(label, new Point(x, 6));
        }

        // Hour labels — always LTR regardless of parent FlowDirection.
        for (var hour = 0; hour < 24; hour++)
        {
            var y = gridTop + hour * cellHeight;
            var label = new FormattedText($"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, hourTypeface, 11, mutedBrush, 1.0);
            drawingContext.DrawText(label, new Point(2, y + cellHeight * 0.14));
        }

        // Cells
        var cursorPen = new Pen(cursorBrush, 2.0);
        for (var dayIndex = 0; dayIndex < Days.Length; dayIndex++)
        {
            var day = Days[dayIndex];
            var x = gridLeft + dayIndex * cellWidth;
            for (var hour = 0; hour < 24; hour++)
            {
                var y = gridTop + hour * cellHeight;
                var gap = Math.Max(1.0, cellHeight * 0.08);
                var rect = new Rect(x + gap, y + gap,
                    Math.Max(1, cellWidth - gap * 2), Math.Max(1, cellHeight - gap * 2));
                var isBlocked = _blocked.Contains((day, hour));

                drawingContext.DrawRoundedRectangle(isBlocked ? blockedBrush : freeBrush,
                    borderPen, rect, 2.0, 2.0);

                if (isBlocked && cellHeight >= 26)
                {
                    // Subtle inner highlight on blocked cells
                    var lightBlocked = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                    var inner = new Rect(rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 4), Math.Max(1, rect.Height - 4));
                    drawingContext.DrawRoundedRectangle(lightBlocked, null, inner, 1.5, 1.5);
                }

                // Keyboard cursor — 2px Teal border on the focused cell
                if (_hasCursor && dayIndex == _cursorDayIndex && hour == _cursorHour)
                {
                    var cursorRect = new Rect(x + gap - 1, y + gap - 1,
                        Math.Max(1, cellWidth - gap * 2 + 2), Math.Max(1, cellHeight - gap * 2 + 2));
                    drawingContext.DrawRectangle(null, cursorPen, cursorRect);
                }
            }
        }

        // Hour guides — subtle horizontal lines every 6 hours
        var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 0.5);
        for (var hour = 0; hour <= 24; hour += 6)
        {
            var y = gridTop + hour * cellHeight;
            drawingContext.DrawLine(guidePen,
                new Point(gridLeft, y), new Point(gridLeft + 7 * cellWidth, y));
        }
    }

    private void Rebuild()
    {
        _blocked.Clear();
        var now = DateTimeOffset.Now;
        var sunday = now.Date.AddDays(-(int)now.DayOfWeek);
        foreach (var day in Days)
        {
            var date = sunday.AddDays((int)day);
            for (var hour = 0; hour < 24; hour++)
            {
                var instant = new DateTimeOffset(date.AddHours(hour), now.Offset);
                if (Windows?.Any(window => window.Contains(instant)) == true)
                    _blocked.Add((day, hour));
            }
        }
        InvalidateVisual();
    }

    private (DayOfWeek Day, int Hour)? CellAt(Point point)
    {
        var width = ActualWidth > 0 ? ActualWidth : 740;
        var height = ActualHeight > 0 ? ActualHeight : 420;
        var cellWidth = Math.Max(MinCellPx, (width - HourLabelWidth - 7) / 7);
        var cellHeight = Math.Max(MinCellPx / 2.0, (height - HeaderHeight) / 24);
        var gridLeft = HourLabelWidth;
        var gridTop = HeaderHeight;

        if (cellWidth <= 0 || cellHeight <= 0 || point.X < gridLeft || point.Y < gridTop) return null;

        // In RTL mode, WPF mirrors coordinates. point.X=0 is the RIGHT edge
        // of the visible control, and it increases to the LEFT. The gridLeft
        // offset is also mirrored, so we subtract from the mirrored width.
        var mirroredX = width - point.X;
        var dayIndex = (int)((mirroredX - gridLeft) / cellWidth);
        if (dayIndex is < 0 or >= 7) return null;
        var day = Days[dayIndex];
        var hour = (int)((point.Y - gridTop) / cellHeight);
        return hour is >= 0 and < 24 ? (day, hour) : null;
    }

    private void Paint(Point point)
    {
        var cell = CellAt(point);
        if (cell is null) return;
        if (_erase) _blocked.Remove(cell.Value); else _blocked.Add(cell.Value);
        InvalidateVisual();
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _dragging = true;
        _erase = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        CaptureMouse();
        var point = e.GetPosition(this);
        Paint(point);

        // Move keyboard cursor to the clicked cell
        var cell = CellAt(point);
        if (cell is { } c)
        {
            _cursorDayIndex = Array.IndexOf(Days, c.Day);
            _cursorHour = c.Hour;
            _hasCursor = true;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed) Paint(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Number keys 1-7 toggle entire days (legacy behavior)
        var dayKey = (int)e.Key >= (int)Key.D1 && (int)e.Key <= (int)Key.D7
            ? (int)e.Key - (int)Key.D1
            : (int)e.Key >= (int)Key.NumPad1 && (int)e.Key <= (int)Key.NumPad7
                ? (int)e.Key - (int)Key.NumPad1
                : -1;

        if (dayKey >= 0)
        {
            var day = Days[dayKey];
            var erase = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            for (var hour = 0; hour < 24; hour++)
            {
                if (erase) _blocked.Remove((day, hour)); else _blocked.Add((day, hour));
            }
            _hasCursor = true;
            _cursorDayIndex = dayKey;
            _cursorHour = 0;
            InvalidateVisual();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Arrow keys — move cursor
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (!_hasCursor)
            {
                _hasCursor = true;
                _cursorDayIndex = 0;
                _cursorHour = 0;
            }

            switch (e.Key)
            {
                case Key.Left:
                    _cursorDayIndex = (_cursorDayIndex - 1 + 7) % 7;
                    break;
                case Key.Right:
                    _cursorDayIndex = (_cursorDayIndex + 1) % 7;
                    break;
                case Key.Up:
                    _cursorHour = (_cursorHour - 1 + 24) % 24;
                    break;
                case Key.Down:
                    _cursorHour = (_cursorHour + 1) % 24;
                    break;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Space — toggle the focused cell
        if (e.Key == Key.Space && _hasCursor)
        {
            var cell = (Days[_cursorDayIndex], _cursorHour);
            if (_blocked.Contains(cell))
                _blocked.Remove(cell);
            else
                _blocked.Add(cell);

            InvalidateVisual();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Enter — toggle entire day at cursor (like clicking the header)
        if (e.Key == Key.Enter && _hasCursor)
        {
            var day = Days[_cursorDayIndex];
            var anyBlocked = false;
            for (var h = 0; h < 24; h++)
            {
                if (_blocked.Contains((day, h))) { anyBlocked = true; break; }
            }
            for (var h = 0; h < 24; h++)
            {
                if (anyBlocked) _blocked.Remove((day, h)); else _blocked.Add((day, h));
            }
            InvalidateVisual();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
    }
}
