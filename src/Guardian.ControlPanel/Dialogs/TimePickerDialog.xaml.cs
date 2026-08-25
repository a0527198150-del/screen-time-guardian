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
    private Border? _selectedMinuteMarker;

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

    private void Overlay_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

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
        ClockCanvas.Children.Clear();
        _selectedMinuteMarker = null;
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
            var marker = CreateMarker(hour.ToString(), OuterRadius, hour * 30 - 90,
                hour == _hour, isHour: true, value: hour);
            ClockCanvas.Children.Add(marker);
        }

        // Hours 12–23 on the inner ring (12 at the top).
        for (var hour = 12; hour < 24; hour++)
        {
            var marker = CreateMarker(hour.ToString(), InnerRadius, (hour - 12) * 30 - 90,
                hour == _hour, isHour: true, value: hour);
            ClockCanvas.Children.Add(marker);
        }

        _minuteMode = false;
        ModeText.Text = "בחר שעה";
    }

    private void BuildMinuteFace()
    {
        ClockCanvas.Children.Clear();
        _selectedMinuteMarker = null;

        DrawCenterDot();
        DrawHand(_minute * 6 - 90, OuterRadius);

        for (var minute = 0; minute < 60; minute += 5)
        {
            var marker = CreateMarker(minute.ToString("00"), OuterRadius, minute * 6 - 90,
                minute == _minute, isHour: false, value: minute);
            ClockCanvas.Children.Add(marker);
        }

        _minuteMode = true;
        ModeText.Text = "בחר דקה";
    }

    private Border CreateMarker(string label, double radius, double angleDeg,
        bool selected, bool isHour, int value)
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
            Cursor = Cursors.Hand,
            Child = text,
            Tag = value
        };

        Canvas.SetLeft(border, x - MarkerSize / 2);
        Canvas.SetTop(border, y - MarkerSize / 2);

        if (selected && !isHour)
        {
            _selectedMinuteMarker = border;
        }

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
        Border? best = null;
        var bestDistance = double.MaxValue;

        foreach (var child in ClockCanvas.Children)
        {
            if (child is not Border marker) continue;

            var left = Canvas.GetLeft(marker) + MarkerSize / 2;
            var top = Canvas.GetTop(marker) + MarkerSize / 2;
            var distance = Math.Sqrt((point.X - left) * (point.X - left)
                + (point.Y - top) * (point.Y - top));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = marker;
            }
        }

        if (best is null || best.Tag is not int value || bestDistance > 26) return;

        if (_minuteMode)
        {
            _minute = value;
            if (_selectedMinuteMarker is not null)
            {
                _selectedMinuteMarker.Background = Brushes.Transparent;
                if (_selectedMinuteMarker.Child is TextBlock tb)
                {
                    tb.Foreground = _ink;
                    tb.FontWeight = FontWeights.Normal;
                }
            }
            _selectedMinuteMarker = best;
            best.Background = _tealSoft;
            if (best.Child is TextBlock selectedText)
            {
                selectedText.Foreground = _teal;
                selectedText.FontWeight = FontWeights.Bold;
            }
            UpdateReadout();
            return;
        }

        // Hour mode: select the hour and advance to the minute face.
        _hour = value;
        _minuteMode = true;
        RebuildFace();
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        ReadoutText.Text = $"{_hour:00}:{_minute:00}";
    }
}
