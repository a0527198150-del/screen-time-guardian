using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenTimeGuardian.ControlPanel.Dialogs;

/// <summary>
/// Analog clock time picker. Two-step selection: pick an hour, then a minute.
/// 24-hour layout: outer ring shows hours 0–11, inner ring hours 12–23.
/// The minute ring is shown after the hour is chosen.
///
/// Selection is computed geometrically from the click position (angle and
/// distance from the center), so it is immune to FlowDirection/RTL quirks.
/// </summary>
public partial class TimePickerDialog : Window
{
    private const double CenterX = 126;
    private const double CenterY = 126;
    private const double OuterRadius = 98;   // hours 0–11 and minute ticks
    private const double InnerRadius = 62;   // hours 12–23
    private const double MarkerSize = 34;    // hit area of one marker

    private readonly SolidColorBrush _teal = new(Color.FromRgb(14, 124, 134));
    private readonly SolidColorBrush _tealSoft = new(Color.FromRgb(224, 242, 241));
    private readonly SolidColorBrush _ink = new(Color.FromRgb(20, 27, 46));

    private int _hour;
    private int _minute;
    private bool _minuteMode;

    /// <summary>The time chosen by the user (valid when DialogResult is true).</summary>
    public TimeSpan SelectedTime => new(_hour % 24, _minute, 0);

    public TimePickerDialog(int initialHour = 22, int initialMinute = 0)
    {
        InitializeComponent();
        _hour = Math.Clamp(initialHour, 0, 23);
        _minute = Math.Clamp(initialMinute, 0, 59);
        UpdateReadout();
        BuildHourFace();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Readout_Click(object sender, RoutedEventArgs e)
    {
        _minuteMode = !_minuteMode;
        RebuildFace();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
    }

    // ================================================================ face

    private void RebuildFace()
    {
        if (_minuteMode) BuildMinuteFace(); else BuildHourFace();
    }

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
    }

    private Border CreateMarker(string label, double radius, double angleDeg,
        bool selected, bool isHour)
    {
        var (x, y) = Polar(radius, angleDeg);

        var text = new TextBlock
        {
            Text = label,
            FontFamily = (FontFamily)FindResource(isHour ? "FontUi" : "FontData"),
            FontSize = isHour ? 14 : 12,
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
        Canvas.SetLeft(hand, CenterX);
        Canvas.SetTop(hand, CenterY - 1.5);
        ClockCanvas.Children.Add(hand);
    }

    private void DrawCenterDot()
    {
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = _teal
        };
        Canvas.SetLeft(dot, CenterX - 4);
        Canvas.SetTop(dot, CenterY - 4);
        ClockCanvas.Children.Add(dot);
    }

    private static (double X, double Y) Polar(double radius, double angleDeg)
    {
        var radians = angleDeg * Math.PI / 180.0;
        return (CenterX + radius * Math.Cos(radians), CenterY + radius * Math.Sin(radians));
    }

    // ================================================================ input

    private void ClockCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(ClockCanvas);
        var dx = point.X - CenterX;
        var dy = point.Y - CenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        // Outside the outer ring (or in the dead center) — ignore.
        if (distance < 12 || distance > OuterRadius + MarkerSize / 2) return;

        // Angle measured clockwise from 12 o'clock.
        var angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0 + 360.0) % 360.0;

        if (_minuteMode)
        {
            _minute = (int)Math.Round(angle / 6.0) % 60;
            BuildMinuteFace();   // re-render with the new selection highlight
            UpdateReadout();
            return;
        }

        // Hour mode: inner ring = 12–23, outer ring = 0–11.
        var step = (int)Math.Round(angle / 30.0) % 12;
        _hour = distance <= InnerRadius + MarkerSize / 2 ? 12 + step : step;
        _hour %= 24;
        _minuteMode = true;
        BuildMinuteFace();
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        ReadoutText.Text = $"{_hour:00}:{_minute:00}";
    }
}
