using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MarketPos.Controls;

namespace MarketPos.Views;

/// <summary>
/// Puts dialogs inside the page they were opened from. See <see cref="DialogWindow"/>.
///
/// <para>
/// Each host window gets one overlay laid over its contents, inside its scaling, so a dialog is
/// drawn at the same size as the page behind it and never runs off a small screen. Dialogs
/// stack: a confirmation asked from inside a form sits on top of that form.
/// </para>
/// </summary>
public static class InPage
{
    private sealed class Host
    {
        public required Window Window { get; init; }
        public required Grid Overlay { get; init; }
        public List<Layer> Open { get; } = new();
    }

    private static readonly ConditionalWeakTable<Window, Host> Hosts = new();

    /// <summary>True while any dialog is open inside this window.</summary>
    public static bool IsOpen(Window? window) =>
        window is not null && Hosts.TryGetValue(window, out var host) && host.Open.Count > 0;

    /// <summary>
    /// The window a dialog should open inside: the page its owner is on, or the page of the
    /// dialog it was opened from. Null when there is none, and the dialog opens as a window.
    /// </summary>
    internal static Window? HostFor(Window? owner)
    {
        if (owner is DialogWindow { HostWindow: { } inside }) return inside;
        if (owner is null || owner is DialogWindow || owner is KeyboardWindow) return null;
        if (!Owned.CanOwn(owner) || !owner.IsVisible) return null;
        return owner.Content is UIElement ? owner : null;
    }

    internal static Layer Open(Window window, DialogWindow dialog)
    {
        var host = HostOf(window);
        var layer = new Layer(host.Window, host.Overlay, dialog, host.Open);
        host.Open.Add(layer);
        host.Overlay.Visibility = Visibility.Visible;
        return layer;
    }

    private static Host HostOf(Window window)
    {
        if (Hosts.TryGetValue(window, out var existing)) return existing;

        var overlay = new Grid { Visibility = Visibility.Collapsed };

        // Inside the window's scaling where it has some, so a form is drawn at the same size
        // as the page behind it.
        var frame = new Grid();
        if (window.Content is ScaleHost scaled)
        {
            var page = scaled.Child;
            scaled.Child = null;
            frame.Children.Add(page);
            frame.Children.Add(overlay);
            scaled.Child = frame;
        }
        else
        {
            var page = (UIElement)window.Content;
            window.Content = null;
            frame.Children.Add(page);
            frame.Children.Add(overlay);
            window.Content = frame;
        }

        var host = new Host { Window = window, Overlay = overlay };
        Hosts.Add(window, host);

        // A window closed with a form still open must not leave the code that opened it
        // waiting for ever.
        window.Closed += (_, _) =>
        {
            foreach (var layer in host.Open.ToList()) layer.Close();
        };

        return host;
    }

    /// <summary>One open dialog, drawn over the page.</summary>
    internal sealed class Layer
    {
        private readonly Grid _overlay;
        private readonly Grid _root;
        private readonly DialogWindow _dialog;
        private readonly List<Layer> _stack;
        private readonly IInputElement? _focusBefore;
        private readonly DispatcherFrame _frame = new();
        private bool _closed;

        public Window Host { get; }

        public bool IsLoaded => _root.IsLoaded;

        public Layer(Window host, Grid overlay, DialogWindow dialog, List<Layer> stack)
        {
            Host = host;
            _overlay = overlay;
            _dialog = dialog;
            _stack = stack;
            _focusBefore = Keyboard.FocusedElement;

            var content = (FrameworkElement)dialog.Content;
            dialog.Content = null;

            // The size the dialog asked for as a window becomes the size of its card here.
            if (!double.IsNaN(dialog.Width)) content.Width = dialog.Width;
            if (!double.IsNaN(dialog.Height)) content.Height = dialog.Height;
            if (!double.IsInfinity(dialog.MaxHeight)) content.MaxHeight = dialog.MaxHeight;
            if (dialog.MinWidth > 0) content.MinWidth = dialog.MinWidth;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Margin = new Thickness(24);

            // The page stays where it is, quieted behind the form.
            var backdrop = new Border
            {
                Background = host.TryFindResource("Brush.Page") is SolidColorBrush page
                    ? new SolidColorBrush(page.Color) { Opacity = 0.94 }
                    : new SolidColorBrush(Color.FromArgb(0xF0, 0xF3, 0xF4, 0xF6)),
            };

            // A form taller than the page scrolls; one that fits sits in the middle of it.
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
            var centre = new Grid();
            centre.SetBinding(FrameworkElement.MinHeightProperty,
                new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = scroller });
            centre.Children.Add(content);
            scroller.Content = centre;

            _root = new Grid();
            _root.Children.Add(backdrop);
            _root.Children.Add(scroller);

            // The window's caption strip lies over the top of every page; without this it
            // would swallow presses on the upper part of a tall form.
            WindowChrome.SetIsHitTestVisibleInChrome(_root, true);

            _root.PreviewKeyDown += (_, e) =>
            {
                // Only the dialog on top hears the keyboard.
                if (ReferenceEquals(_stack.LastOrDefault(), this)) _dialog.Forward(e);
            };

            var announced = false;
            _root.Loaded += (_, _) =>
            {
                if (announced) return;
                announced = true;
                _dialog.AnnounceLoaded();

                // Something inside the form takes focus, so typing lands in it rather than in
                // the page behind.
                if (!IsWithin(Keyboard.FocusedElement as DependencyObject, _root))
                    _root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            };

            _overlay.Children.Add(_root);
        }

        /// <summary>Waits, the way ShowDialog waits, until the dialog is closed.</summary>
        public void Wait()
        {
            if (!_closed) Dispatcher.PushFrame(_frame);
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;

            _overlay.Children.Remove(_root);
            _stack.Remove(this);
            if (_stack.Count == 0) _overlay.Visibility = Visibility.Collapsed;

            // Back to whatever had the caret before the form opened.
            if (_focusBefore is UIElement { IsVisible: true } previous) previous.Focus();

            _frame.Continue = false;
        }

        private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
        {
            while (node is not null)
            {
                if (ReferenceEquals(node, ancestor)) return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
    }
}
