// Compile-only stand-ins for WPF, WinForms and the common dialogs.
// Signatures mirror the real API so the code-behind can be type-checked on Linux.
namespace System.Windows
{
    using System.Windows.Media;

    public class DependencyObject { }
    public class UIElement : DependencyObject { public bool IsEnabled { get; set; } public double ActualHeight { get; } }
    public class FrameworkElement : UIElement { public object? Tag { get; set; } public event RoutedEventHandler? Loaded; }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
    public class RoutedEventArgs : EventArgs { public RoutedEventArgs() { } }

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

    public class Window : FrameworkElement
    {
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public bool ShowActivated { get; set; }
        public void Show() { }
        public void Close() { }
        public void InitializeComponent() { }
    }

    public class StartupEventArgs : EventArgs { }

    public class Application
    {
        public void InitializeComponent() { }
    }
}

namespace System.Windows.Media
{
    public class Brush { }
    public static class Brushes
    {
        public static Brush DarkRed => new();
        public static Brush DarkGreen => new();
        public static Brush Black => new();
    }
}

namespace System.Windows.Controls
{
    using System.Collections;
    using System.Windows.Media;

    public class Control : FrameworkElement { }

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
}
