using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenTimeGuardian.ControlPanel;

/// <summary>
/// Inline analog clock for picking an hour and a minute, hosted directly in the
/// wizard (no separate window). Two-step selection: pick an hour — outer ring
/// shows 0–11, inner ring 12–23 — then a minute (every 5 minutes).
///
/// Selection is computed geometrically from the click position (angle and
/// distance from the center), so it works regardless of FlowDirection.
/// </summary>
public partial class TimePickerControl : UserControl
{
    private const double Center = 110;
    private const double OuterRadius = 86;   // hours 0–11 and minute ticks
    private const double InnerRadius = 54;   // hours 12–23
    private const double MarkerSize = 30;    // hit area of one marker

    private readonly SolidColorBrush _teal = new(Color.FromRgb(14, 124, 134));
    private readonly SolidColorBrush _tealSoft = new(Color.FromRgb(224, 242, 241));
    private readonly SolidColorBrush _ink = new(Color.FromRgb(20, 27, 46));

    private int _hour = 22;
    private int _minute;
    private bool _minuteMode;

    /// <summary>The time selected so far (valid once SelectionCompleted fires).</summary>
    public TimeSpan SelectedTime => new(_hour % 24, _minute, 0);

    /// <summary>Raised when the user picks a minute (selection is complete).</summary>
    public event EventHandler? SelectionCompleted;

    public TimePickerControl()
    {
        InitializeComponent();
        BuildHourFace();
    }

    /// <summary>Reset the control to the given time and show the hour face.</summary>
    public void Initialize(TimeSpan time)
    {
        _hour = Math.Clamp(time.Hours, 0, 23);
        _minute = Math.Clamp(time.Minutes, 0, 59);
        _minuteMode = false;
        BuildHourFace();
    }

    // ================================================================ face

    private void BuildHourFace()
    {
        ClockCanvas.Children.Clear();

        DrawCenterDot();
        DrawHand((_hour % 12) * 30 - 90, _hour < 12 ? OuterRadius : InnerRadius);

        // Hours 0–11 on the outer ring (0 at the top).
        for (var hour = 0; hour < 12; hour++)
        {
            ClockCanvas.Children.Add(CreateMarker(hour.ToString(), OuterRadius, hour * 30 - 90,
                hour == _hour, isHour: true));
        }

        // Hours 12–23 on the inner ring (12 at the top).
        for (var hour = 12; hour < 24; hour++)
        {
            ClockCanvas.Children.Add(CreateMarker(hour.ToString(), InnerRadius, (hour - 12) * 30 - 90,
                hour == _hour, isHour: true));
        }

        _minuteMode = false;
        ModeText.Text = "בחר שעה";
        UpdateReadout();
    }

    private void BuildMinuteFace()
    {
        ClockCanvas.Children.Clear();

        DrawCenterDot();
        DrawHand(_minute * 6 - 90, OuterRadius);

        for (var minute = 0; minute < 60; minute += 5)
        {
            ClockCanvas.Children.Add(CreateMarker(minute.ToString("00"), OuterRadius, minute * 6 - 90,
                minute == _minute, isHour: false));
        }

        _minuteMode = true;
        ModeText.Text = "בחר דקה";
        UpdateReadout();
    }

    private Border CreateMarker(string label, double radius, double angleDeg,
        bool selected, bool isHour)
    {
        var (x, y) = Polar(radius, angleDeg);

        var text = new TextBlock
        {
            Text = label,
            FontFamily = (FontFamily)FindResource(isHour ? "FontUi" : "FontData"),
            FontSize = isHour ? 13 : 11,
            FontWeight = selected ? FontWeights.Bold : FontWeights.Normal,
            Foreground = selected ? _teal : _ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Width = MarkerSize,
            Height = MarkerSize,
            CornerRadius = new CornerRadius(MarkerSize / 2),
            Background = selected ? _tealSoft : Brushes.Transparent,
            Child = text
        };

        Canvas.SetLeft(border, x - MarkerSize / 2);
        Canvas.SetTop(border, y - MarkerSize / 2);

        return border;
    }

    private void DrawHand(double angleDeg, double radius)
    {
        var handLength = radius - 14;

        var hand = new Border
        {
            Width = handLength,
            Height = 3,
            Background = _teal,
            CornerRadius = new CornerRadius(1.5),
            RenderTransformOrigin = new Point(0, 0.5)
        };
        hand.RenderTransform = new RotateTransform(angleDeg);
        Canvas.SetLeft(hand, Center);
        Canvas.SetTop(hand, Center - 1.5);
        ClockCanvas.Children.Add(hand);
    }

    private void DrawCenterDot()
    {
        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = _teal
        };
        Canvas.SetLeft(dot, Center - 3.5);
        Canvas.SetTop(dot, Center - 3.5);
        ClockCanvas.Children.Add(dot);
    }

    private static (double X, double Y) Polar(double radius, double angleDeg)
    {
        var radians = angleDeg * Math.PI / 180.0;
        return (Center + radius * Math.Cos(radians), Center + radius * Math.Sin(radians));
    }

    // ================================================================ input

    private void ClockCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(ClockCanvas);
        var dx = point.X - Center;
        var dy = point.Y - Center;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        // Outside the outer ring (or in the dead center) — ignore.
        if (distance < 12 || distance > OuterRadius + MarkerSize / 2) return;

        // Angle measured clockwise from 12 o'clock.
        var angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0 + 360.0) % 360.0;

        if (_minuteMode)
        {
            // Snap to the 5-minute marks that are drawn on the dial.
            _minute = (int)Math.Round(angle / 6.0) % 60;
            _minute = (int)(Math.Round(_minute / 5.0) * 5) % 60;
            BuildMinuteFace();
            SelectionCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Hour mode: inner ring = 12–23, outer ring = 0–11.
        var step = (int)Math.Round(angle / 30.0) % 12;
        _hour = distance <= InnerRadius + MarkerSize / 2 ? 12 + step : step;
        _hour %= 24;
        BuildMinuteFace();
    }

    private void UpdateReadout()
    {
        ReadoutText.Text = $"{_hour:00}:{_minute:00}";
    }
}
