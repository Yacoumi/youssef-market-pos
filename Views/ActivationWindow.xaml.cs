using System.Windows;
using System.Windows.Input;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// Asks for this computer's activation key before the software opens. See
/// <see cref="License"/>.
/// </summary>
public partial class ActivationWindow : DialogWindow
{
    public ActivationWindow()
    {
        InitializeComponent();
        Localizer.Apply(this);
        MachineCodeBox.Text = License.MachineCode;
        Loaded += (_, _) => KeyBox.Focus();
    }

    /// <summary>True when this computer is activated, asking for the key if it is not yet.</summary>
    public static bool EnsureActivated()
    {
        if (License.IsActivated) return true;
        return new ActivationWindow().ShowDialog() == true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(License.MachineCode); }
        catch { /* the code is on screen to read out either way */ }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (License.TryActivate(KeyBox.Text))
        {
            DialogResult = true;
            Close();
            return;
        }

        ErrorText.Text = Loc.T("That key is not for this computer. Check it and try again.");
        KeyBox.Focus();
        KeyBox.SelectAll();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Activate_Click(sender, e);
        else if (e.Key == Key.Escape) Close_Click(sender, e);
    }
}
