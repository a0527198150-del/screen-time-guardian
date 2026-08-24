using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenTimeGuardian.Contracts;

namespace ScreenTimeGuardian.ControlPanel;

/// <summary>Keyboard and pointer editor for a seven-day, twenty-four-hour schedule.</summary>
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
        var freeBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));   // #F1F5F9
        var blockedBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136));  // Teal #0D9488
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1.0);
        var inkBrush = new SolidColorBrush(Color.FromRgb(26, 26, 46));        // Ink #1A1A2E
        var mutedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));   // MutedInk #64748B
        var headerTypeface = new Typeface("Assistant");
        var hourTypeface = new Typeface("Assistant");

        // Day headers — drawn right-to-left so יום ראשון is on the right.
        for (var visualIndex = 0; visualIndex < Days.Length; visualIndex++)
        {
            var day = Days[visualIndex];
            var x = gridLeft + (Days.Length - 1 - visualIndex) * cellWidth;
            var label = new FormattedText(HebrewDays.Name(day), CultureInfo.CurrentCulture,
                FlowDirection.RightToLeft, headerTypeface, 13, inkBrush, 1.0);
            label.TextAlignment = TextAlignment.Center;
            label.MaxTextWidth = cellWidth;
            drawingContext.DrawText(label, new Point(x, 6));
        }

        // Hour labels on the left, always LTR.
        for (var hour = 0; hour < 24; hour++)
        {
            var y = gridTop + hour * cellHeight;
            var label = new FormattedText($"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, hourTypeface, 11, mutedBrush, 1.0);
            drawingContext.DrawText(label, new Point(2, y + cellHeight * 0.14));
        }

        // Cells
        for (var visualIndex = 0; visualIndex < Days.Length; visualIndex++)
        {
            var day = Days[visualIndex];
            var x = gridLeft + (Days.Length - 1 - visualIndex) * cellWidth;
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

        // Visual index 0 is rightmost (Sunday). Reverse to map physical → logical.
        var visualIndex = (int)((point.X - gridLeft) / cellWidth);
        if (visualIndex is < 0 or >= 7) return null;
        var dayIndex = 7 - 1 - visualIndex;  // map visual → Days array
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
        Paint(e.GetPosition(this));
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
        var key = (int)e.Key >= (int)Key.D1 && (int)e.Key <= (int)Key.D7
            ? (int)e.Key - (int)Key.D1
            : (int)e.Key >= (int)Key.NumPad1 && (int)e.Key <= (int)Key.NumPad7
                ? (int)e.Key - (int)Key.NumPad1
                : -1;
        if (key < 0) return;
        var day = Days[key];
        var erase = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        for (var hour = 0; hour < 24; hour++)
        {
            if (erase) _blocked.Remove((day, hour)); else _blocked.Add((day, hour));
        }
        InvalidateVisual();
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}