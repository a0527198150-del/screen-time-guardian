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
        var width = ActualWidth > 0 ? ActualWidth : 560;
        var height = ActualHeight > 0 ? ActualHeight : 360;
        const double left = 52;
        const double top = 32;
        var cellWidth = Math.Max(1, (width - left) / 7);
        var cellHeight = Math.Max(1, (height - top) / 24);
        var free = new SolidColorBrush(Color.FromRgb(238, 243, 246));
        var blocked = new SolidColorBrush(Color.FromRgb(182, 227, 242));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(210, 220, 228)), 1);
        var ink = new SolidColorBrush(Color.FromRgb(21, 33, 61));

        for (var day = 0; day < Days.Length; day++)
        {
            var label = new FormattedText(HebrewDays.Name(Days[day]), CultureInfo.CurrentCulture,
                FlowDirection.RightToLeft, new Typeface("Segoe UI"), 12, ink, 1.0);
            drawingContext.DrawText(label, new Point(left + day * cellWidth + 4, 7));
            for (var hour = 0; hour < 24; hour++)
            {
                var rectangle = new Rect(left + day * cellWidth, top + hour * cellHeight,
                    Math.Max(1, cellWidth - 2), Math.Max(1, cellHeight - 2));
                drawingContext.DrawRectangle(_blocked.Contains((Days[day], hour)) ? blocked : free,
                    border, rectangle);
            }
        }

        for (var hour = 0; hour < 24; hour++)
        {
            var label = new FormattedText($"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, ink, 1.0);
            drawingContext.DrawText(label, new Point(0, top + hour * cellHeight + 3));
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
        const double left = 52;
        const double top = 32;
        var cellWidth = (ActualWidth - left) / 7;
        var cellHeight = (ActualHeight - top) / 24;
        if (cellWidth <= 0 || cellHeight <= 0 || point.X < left || point.Y < top) return null;
        var day = (int)((point.X - left) / cellWidth);
        var hour = (int)((point.Y - top) / cellHeight);
        return day is >= 0 and < 7 && hour is >= 0 and < 24 ? (Days[day], hour) : null;
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
