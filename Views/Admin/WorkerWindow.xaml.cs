using MarketPos.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit a member of staff.
///
/// The role box spells out what it grants, because "Manager" is not self-explanatory and
/// getting it wrong is how a cashier ends up able to see the shop's profit.
/// </summary>
public partial class WorkerWindow : MarketPos.Views.DialogWindow
{
    private readonly Worker? _existing;

    /// <summary>
    /// Read through the translator where they are used, not here: a static field is built once
    /// when the type is first touched, which may be before the shop's language is even known.
    /// </summary>
    private static readonly (string Label, WorkerRole Role, string What)[] Roles =
    [
        ("Cashier", WorkerRole.Cashier,
         "Uses the till. In the back office they see Add product, Categories and Inventory."),
        ("Stock worker", WorkerRole.StockWorker,
         "In the back office they see Add product, Categories and Inventory. No money screens."),
        ("Manager", WorkerRole.Manager,
         "Runs the shop floor: products, stock, suppliers, purchases, staff and reports. "
         + "Cannot see profit, salaries, supplier debt or settings."),
        ("Owner", WorkerRole.Owner,
         "Everything, including profit, salaries, supplier debt and business settings."),
    ];

    public WorkerWindow(Worker? existing)
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);
        _existing = existing;

        RoleBox.ItemsSource = Roles.Select(r => Loc.T(r.Label)).ToList();
        PeriodBox.ItemsSource = new[] { Loc.T("Monthly"), Loc.T("Weekly"), Loc.T("Daily") };

        // Salary is not shown at all to someone who may not see salaries; leaving an empty
        // box there would invite them to type one in and have it silently rejected.
        SalarySection.Visibility = Session.Can(Permission.SeeSalaries)
            ? Visibility.Visible : Visibility.Collapsed;

        if (existing is null)
        {
            HeadingText.Text = Loc.T("Add worker");
            SubText.Text = Loc.T("Give them a name and a password: they use them to sign in to the back office.");
            PasswordNote.Text = Loc.T("They sign in with their name and this password.");
            RoleBox.SelectedIndex = 0;
            PeriodBox.SelectedIndex = 0;
            StartedBox.SelectedDate = DateTime.Today;
        }
        else
        {
            HeadingText.Text = existing.Name;
            SubText.Text = existing.IsActive
                ? Loc.T("{0} since {1}.", existing.RoleLabel, existing.StartedOn.ToString("d MMMM yyyy"))
                : Loc.T("This worker is inactive.");
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            EmailBox.Text = existing.Email;
            NoteBox.Text = existing.Note;
            StartedBox.SelectedDate = existing.StartedOn;
            SalaryBox.Text = existing.Salary.ToString("0.00", CultureInfo.InvariantCulture);
            RoleBox.SelectedIndex = Math.Max(0, Array.FindIndex(Roles, r => r.Role == existing.Role));
            PeriodBox.SelectedIndex = existing.SalaryPeriod switch
            {
                SalaryPeriod.Weekly => 1,
                SalaryPeriod.Daily => 2,
                _ => 0,
            };

            PasswordNote.Text = Loc.T(existing.HasPin
                ? "Leave empty to keep the current password."
                : "They sign in with their name and this password.");

            ActiveButton.Visibility = Visibility.Visible;
            ActiveButton.Content = Loc.T(existing.IsActive ? "Deactivate" : "Reactivate");
            if (existing.IsActive)
                ActiveButton.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        }

        UpdateRoleNote();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public static bool AddNew(Window owner) =>
        new WorkerWindow(null).By(owner).ShowDialog() == true;

    public static bool Edit(Window owner, Worker worker) =>
        new WorkerWindow(worker).By(owner).ShowDialog() == true;

    private void Role_Changed(object sender, RoutedEventArgs e) => UpdateRoleNote();

    private void UpdateRoleNote()
    {
        if (RoleNoteText is null) return;
        RoleNoteText.Text = Loc.T(Roles[Math.Max(0, RoleBox.SelectedIndex)].What);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = Loc.T("Give the worker a name.");
            NameBox.Focus();
            return;
        }

        // The account. Required for a new worker, who could never sign in without one, and
        // optional on an edit, where an empty box keeps the password they have.
        var password = PasswordBox.Password;
        var needsPassword = _existing is null || !_existing.HasPin;
        if (password.Length > 0 || ConfirmPasswordBox.Password.Length > 0 || needsPassword)
        {
            if (password.Length == 0)
            {
                ErrorText.Text = Loc.T("Give the worker a password so they can sign in.");
                PasswordBox.Focus();
                return;
            }
            if (password.Length < 4)
            {
                ErrorText.Text = Loc.T("Use at least 4 characters.");
                PasswordBox.Focus();
                return;
            }
            if (password != ConfirmPasswordBox.Password)
            {
                ErrorText.Text = Loc.T("The two passwords do not match.");
                ConfirmPasswordBox.Focus();
                return;
            }
        }

        // The sign-in picks a worker by name, so two workers may not share one.
        try
        {
            if (Link.Shop.Workers.List(includeInactive: true).Any(w =>
                    w.Id != (_existing?.Id ?? 0) &&
                    string.Equals(w.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)))
            {
                ErrorText.Text = Loc.T("Another worker already has this name. Give each worker their own.");
                NameBox.Focus();
                return;
            }
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
            return;
        }

        var salary = _existing?.Salary ?? 0m;
        if (SalarySection.Visibility == Visibility.Visible)
        {
            if (SalaryBox.Text.Trim().Length > 0 &&
                !decimal.TryParse(SalaryBox.Text.Trim().Replace(',', '.'),
                                  NumberStyles.Number, CultureInfo.InvariantCulture, out salary))
            {
                ErrorText.Text = Loc.T("The salary must be a number, like 3000.");
                SalaryBox.Focus();
                return;
            }
            if (salary < 0m) { ErrorText.Text = Loc.T("The salary cannot be negative."); return; }
        }

        var worker = new Worker
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Phone = PhoneBox.Text.Trim(),
            Email = EmailBox.Text.Trim(),
            Role = Roles[Math.Max(0, RoleBox.SelectedIndex)].Role,
            StartedOn = StartedBox.SelectedDate ?? DateTime.Today,
            Salary = salary,
            SalaryPeriod = PeriodBox.SelectedIndex switch
            {
                1 => SalaryPeriod.Weekly,
                2 => SalaryPeriod.Daily,
                _ => SalaryPeriod.Monthly,
            },
            Note = NoteBox.Text.Trim(),
            IsActive = _existing?.IsActive ?? true,
        };

        try
        {
            var id = _existing?.Id ?? 0;
            if (_existing is null) id = Link.Shop.Workers.Create(worker);
            else Link.Shop.Workers.Update(worker);

            if (password.Length > 0 && id > 0) Link.Shop.Workers.SetPin(id, password);

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;
        var activate = !_existing.IsActive;

        if (!activate && !ConfirmWindow.Ask(this, Loc.T("Deactivate {0}?", _existing.Name),
                "They can no longer sign in at the till. Their past sales, shifts and salary "
                + "payments all stay on record."))
            return;

        Link.Shop.Workers.SetActive(_existing.Id, _existing.Name, activate);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }
}
