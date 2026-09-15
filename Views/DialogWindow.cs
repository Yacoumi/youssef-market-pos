using System.Windows;
using System.Windows.Input;

namespace MarketPos.Views;

/// <summary>
/// A dialog that opens inside the window it belongs to, instead of as a window of its own.
///
/// <para>
/// Every form in the app — add product, pay a supplier, confirm a refund — used to be a
/// separate window floating over the till or the back office. On a shop's touchscreen that
/// reads as the app jumping to another program. So a dialog now lays its content over the page
/// it was opened from, and the page underneath waits until it is answered, the same way it
/// waited for the window before.
/// </para>
///
/// <para>
/// The dialogs themselves are unchanged. <see cref="ShowDialog"/>, <see cref="Close"/> and
/// <see cref="DialogResult"/> are replaced here with versions that work in the page, and each
/// dialog's own code calls them exactly as it called the window's. A dialog with nothing to
/// open inside — the sign-in at start-up, before any window is on screen — still opens as a
/// window.
/// </para>
/// </summary>
public class DialogWindow : Window
{
    private InPage.Layer? _layer;
    private bool? _result;
    private Window? _requestedOwner;

    /// <summary>What the caller handed to <c>By</c> — possibly another dialog that is itself in a page.</summary>
    internal Window? RequestedOwner
    {
        get => _requestedOwner;
        set => _requestedOwner = value;
    }

    /// <summary>True while this dialog is open inside a page rather than as a window.</summary>
    internal bool IsInPage => _layer is not null;

    /// <summary>The window this dialog is drawn inside, while it is in a page.</summary>
    internal Window? HostWindow => _layer?.Host;

    /// <summary>
    /// A real, shown window to hand to Windows' own dialogs (the file picker), which refuse an
    /// owner that has never been on screen.
    /// </summary>
    protected Window DialogOwner => _layer?.Host ?? this;

    public new bool? DialogResult
    {
        get => _layer is null ? base.DialogResult : _result;
        set
        {
            if (_layer is null)
            {
                base.DialogResult = value;
                return;
            }

            _result = value;

            // A window closes itself the moment a modal result is set; so does this.
            if (value is not null) Close();
        }
    }

    public new bool IsLoaded => _layer?.IsLoaded ?? base.IsLoaded;

    public new bool? ShowDialog()
    {
        var host = InPage.HostFor(_requestedOwner ?? Owner);
        if (host is null) return base.ShowDialog();

        _result = null;
        _layer = InPage.Open(host, this);
        _layer.Wait();
        _layer = null;
        return _result;
    }

    public new void Close()
    {
        if (_layer is null)
        {
            base.Close();
            return;
        }

        _layer.Close();
    }

    /// <summary>
    /// Hands a key pressed inside the page to the dialog's own handlers, which were attached
    /// to the window in its XAML and would otherwise never hear Escape or Enter.
    /// </summary>
    internal void Forward(KeyEventArgs pressed)
    {
        var copy = new KeyEventArgs(pressed.KeyboardDevice, pressed.InputSource, pressed.Timestamp, pressed.Key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };

        RaiseEvent(copy);
        if (copy.Handled) pressed.Handled = true;
    }

    /// <summary>Runs the dialog's Loaded handlers once its content is on the page.</summary>
    internal void AnnounceLoaded() => RaiseEvent(new RoutedEventArgs(LoadedEvent, this));
}
