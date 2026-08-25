// Compile-only stand-ins for WPF, WinForms and the common dialogs.
// Signatures mirror the real API so the code-behind can be type-checked on Linux.
namespace System.Windows
{
    using System.Windows.Media;

    public class DependencyObject { }
    public class UIElement : DependencyObject { public bool IsEnabled { get; set; } public double ActualWidth { get; } public double ActualHeight { get; } }
    public class FrameworkElement : UIElement
    {
        public object? Tag { get; set; }
        public event RoutedEventHandler? Loaded;
        protected virtual Size MeasureOverride(Size availableSize) => new Size(availableSize.Width, availableSize.Height);
    }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
    public class RoutedEventArgs : EventArgs { public RoutedEventArgs() { } }

    public class DependencyPropertyChangedEventArgs { public object? OldValue; public object? NewValue; }

    public static class Keyboard
    {
        public static ModifierKeys Modifiers => ModifierKeys.None;
    }

    [Flags]
    public enum ModifierKeys { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

    public class KeyEventArgs : RoutedEventArgs
    {
        public Key Key { get; set; }
        public bool Handled { get; set; }
    }

    public enum Key { None = 0, Escape = 13, Enter = 18, Delete = 127, S = 83, N = 78, F = 70 }

    public enum MessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
    public enum MessageBoxImage { None, Question, Warning, Information, Error }
    public enum MessageBoxResult { None, OK, Cancel, Yes, No }

    public static class MessageBox
    {
        public static MessageBoxResult Show(string messageBoxText, string caption,
            MessageBoxButton button, MessageBoxImage icon) => MessageBoxResult.Yes;
    }

    public static class SystemParameters
    {
        public static Rect WorkArea => default;
    }

    public struct Rect { public double Left; public double Top; public double Bottom; public double Right; }

    public struct Size
    {
        public Size(double width, double height) { Width = width; Height = height; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public struct Point
    {
        public Point(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class Window : FrameworkElement
    {
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public Visibility Visibility { get; set; }
        public Window? Owner { get; set; }
        public bool? DialogResult { get; set; }
        public bool ShowActivated { get; set; }
        public event System.ComponentModel.CancelEventHandler? Closing;
        public void Show() { }
        public bool? ShowDialog() => false;
        public void Close() { }
        public void InitializeComponent() { }
    }

    public enum Visibility { Visible, Hidden, Collapsed }

    public class StartupEventArgs : EventArgs { }

    public class Application
    {
        public void InitializeComponent() { }
    }
}

namespace System.Windows.Media
{
    public class Brush { }
    public class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) { }
        public Color Color { get; set; }
    }
    public struct Color
    {
        public static Color FromRgb(byte r, byte g, byte b) => default;
        public byte R; public byte G; public byte B; public byte A;
    }
    public class Thickness
    {
        public double Left; public double Top; public double Right; public double Bottom;
        public Thickness(double uniformLength) { Left = Top = Right = Bottom = uniformLength; }
        public Thickness(double left, double top, double right, double bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
    }
    public class FontFamily { public FontFamily(string name) { } }
    public class Typeface { public Typeface(FontFamily family, object style, object weight, object stretch) { } }
    public enum FontStyles { Normal }
    public enum FontWeights { Normal, SemiBold, Bold }
    public enum FontStretches { Normal }
    public class RenderTransform { }
    public class TranslateTransform : RenderTransform { public TranslateTransform() { } public TranslateTransform(double x, double y) { } public double X { get; set; } public double Y { get; set; } }
    public static class Brushes
    {
        public static Brush DarkRed => new();
        public static Brush DarkGreen => new();
        public static Brush Black => new();
        public static Brush White => new();
        public static Brush Transparent => new();
    }
}

namespace System.Windows.Controls
{
    using System.Collections;
    using System.Windows.Media;

    public class Control : FrameworkElement
    {
        public Brush? Background { get; set; }
        public Brush? Foreground { get; set; }
        public Brush? BorderBrush { get; set; }
        public Thickness? BorderThickness { get; set; }
        public Thickness? Padding { get; set; }
        public double FontSize { get; set; }
        public CornerRadius CornerRadius { get; set; }
    }

    public class UserControl : Control
    {
        public void InitializeComponent() { }
    }

    public class Canvas : Panel
    {
        public static void SetLeft(UIElement element, double value) { }
        public static void SetTop(UIElement element, double value) { }
        public static double GetLeft(UIElement element) => 0;
        public static double GetTop(UIElement element) => 0;
    }

    public struct CornerRadius
    {
        public CornerRadius(double uniformLength) { }
    }

    public class TextBlock : FrameworkElement { public string Text { get; set; } = string.Empty; public Brush? Foreground { get; set; } }
    public class TextBox : Control { public string Text { get; set; } = string.Empty; }
    public class PasswordBox : Control { public string Password { get; set; } = string.Empty; }
    public class Button : Control { public object? Content { get; set; } }
    public class CheckBox : Control { public bool? IsChecked { get; set; } public object? Content { get; set; } }

    public class ItemCollection : IEnumerable
    {
        public void Refresh() { }
        public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
    }

    public class UIElementCollection : IEnumerable<UIElement>
    {
        public IEnumerator<UIElement> GetEnumerator() => Enumerable.Empty<UIElement>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class Panel : FrameworkElement { public UIElementCollection Children { get; } = new(); }
    public class StackPanel : Panel { }
    public class WrapPanel : Panel { }

    public enum SelectionMode { Single, Multiple, Extended }

    public class Selector : Control
    {
        public IEnumerable? ItemsSource { get; set; }
        public object? SelectedItem { get; set; }
        public int SelectedIndex { get; set; }
        public ItemCollection Items { get; } = new();
    }

    public class ListBox : Selector
    {
        public SelectionMode SelectionMode { get; set; }
        public IList SelectedItems { get; } = new System.Collections.ArrayList();
    }

    public class ComboBox : Selector { }
    public class TabControl : Selector { }

    public class SelectionChangedEventArgs : System.Windows.RoutedEventArgs { }
}

namespace System.Windows.Input
{
    public class MouseEventArgs : System.Windows.RoutedEventArgs { }
    public class MouseButtonEventArgs : MouseEventArgs { }
}

namespace System.Windows.Threading
{
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public void Start() { }
        public void Stop() { }
    }
}

namespace Microsoft.Win32
{
    public class OpenFileDialog
    {
        public string Title { get; set; } = string.Empty;
        public string Filter { get; set; } = string.Empty;
        public bool CheckFileExists { get; set; }
        public string FileName { get; set; } = string.Empty;
        public bool? ShowDialog() => false;
    }
}

namespace System.Windows.Forms
{
    public enum MessageBoxButtons { OK }
    public enum MessageBoxIcon { Information }
    public enum MessageBoxDefaultButton { Button1 }
    [Flags] public enum MessageBoxOptions { None = 0, RtlReading = 1, RightAlign = 2 }

    public static class MessageBox
    {
        public static int Show(string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) => 1;
    }

    public static class ApplicationConfiguration { public static void Initialize() { } }

    public class Screen
    {
        public ScreenBounds Bounds { get; set; } = new();
        public static Screen[] AllScreens => Array.Empty<Screen>();
    }

    public class ScreenBounds
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
    }
}
