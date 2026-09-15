using System.Drawing;
using System.Windows.Forms;
using MarketPos.Services;

/// <summary>
/// The server's activation prompt. The server has no window of its own, so on a computer that
/// is not activated yet it shows this once, takes the key, and only then starts serving.
/// </summary>
internal static class ServerActivation
{
    /// <summary>True when this computer is activated, asking for the key if it is not yet.</summary>
    public static bool EnsureActivated()
    {
        if (License.IsActivated) return true;

        var activated = false;
        var ui = new Thread(() =>
        {
            Application.EnableVisualStyles();
            using var form = Build(() => activated = true);
            Application.Run(form);
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        return activated;
    }

    private static Form Build(Action succeeded)
    {
        var arabic = Loc.Current == MarketPos.Models.Language.Arabic;
        var font = new Font("Segoe UI", 10.5f);

        var form = new Form
        {
            Text = "POS-Server — " + Loc.T("Activate this computer"),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(470, 290),
            Font = font,
            RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No,
            RightToLeftLayout = arabic,
            TopMost = true,
            BackColor = Color.White,
        };

        var intro = new Label
        {
            Text = Loc.T("This copy is registered to one shop. Send the machine code to Homayk Studio to receive the activation key for this computer."),
            Location = new Point(20, 16), Size = new Size(430, 58),
        };
        var codeLabel = new Label { Text = Loc.T("MACHINE CODE"), Location = new Point(20, 80), AutoSize = true };
        var code = new TextBox
        {
            Text = License.MachineCode, ReadOnly = true, Location = new Point(20, 104), Size = new Size(320, 30),
            RightToLeft = RightToLeft.No, Font = new Font("Consolas", 12f),
        };
        var copy = new Button { Text = Loc.T("Copy"), Location = new Point(350, 102), Size = new Size(100, 32) };
        copy.Click += (_, _) => { try { Clipboard.SetText(License.MachineCode); } catch { } };

        var keyLabel = new Label { Text = Loc.T("ACTIVATION KEY"), Location = new Point(20, 146), AutoSize = true };
        var key = new TextBox
        {
            Location = new Point(20, 170), Size = new Size(430, 30), RightToLeft = RightToLeft.No,
            CharacterCasing = CharacterCasing.Upper, Font = new Font("Consolas", 12f),
        };
        var error = new Label { ForeColor = Color.Firebrick, Location = new Point(20, 206), Size = new Size(430, 24) };

        var activate = new Button { Text = Loc.T("Activate"), Location = new Point(300, 238), Size = new Size(150, 36) };
        var close = new Button { Text = Loc.T("Close"), Location = new Point(20, 238), Size = new Size(110, 36) };

        activate.Click += (_, _) =>
        {
            if (License.TryActivate(key.Text))
            {
                succeeded();
                form.Close();
                return;
            }
            error.Text = Loc.T("That key is not for this computer. Check it and try again.");
            key.Focus();
            key.SelectAll();
        };
        close.Click += (_, _) => form.Close();

        form.AcceptButton = activate;
        form.CancelButton = close;
        form.Controls.AddRange(new Control[] { intro, codeLabel, code, copy, keyLabel, key, error, activate, close });
        form.Shown += (_, _) => key.Focus();
        return form;
    }
}
