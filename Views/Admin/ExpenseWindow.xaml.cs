using MarketPos.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit an operating expense.
///
/// The category box is editable, so the owner can type a category the list has never seen —
/// a shop's costs do not fit a fixed menu, and being forced into "Other" makes the Money
/// Spent breakdown useless within a month.
/// </summary>
public partial class ExpenseWindow : MarketPos.Views.DialogWindow
{
    private readonly Expense? _existing;
    private string? _receiptPath;

    /// <summary>
    /// How it was paid, as stored. The list shows these in the shop's language, but the book
    /// keeps the English word: the box used to select "Cash" out of a list of Arabic words,
    /// found nothing, and stayed empty.
    /// </summary>
    private static readonly string[] Methods = { "Cash", "Bank transfer", "Cheque", "Card", "Other" };

    /// <summary>The shop's categories as stored, in the same order as the list shows them.</summary>
    private readonly List<string> _categories;

    public ExpenseWindow(Expense? existing, Expense? template = null)
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);
        _existing = existing;

        _categories = Link.Shop.Expenses.Categories().Select(c => c.Name).ToList();
        CategoryBox.ItemsSource = _categories.Select(c => Loc.T(c)).ToList();
        MethodBox.ItemsSource = Methods.Select(m => Loc.T(m)).ToList();
        RepeatBox.ItemsSource = new[]
        {
            Loc.T("Does not repeat"), Loc.T("Weekly"), Loc.T("Monthly"), Loc.T("Yearly"),
        };

        var source = existing ?? template;

        if (existing is not null)
        {
            HeadingText.Text = Loc.T("Edit expense");
            SubText.Text = Loc.T("Changing the amount changes the profit figures for that period.");
        }
        else if (template is not null)
        {
            HeadingText.Text = Loc.T("{0} — this month", template.Name);
            SubText.Text = Loc.T("Copied from last month. Check the amount before saving: bills change.");
            SaveButton.Content = Loc.T("Add this one");
        }
        else
        {
            HeadingText.Text = Loc.T("Add expense");
            SubText.Text = Loc.T("Rent, electricity, water, repairs — anything that is not stock.");
        }

        if (source is not null)
        {
            NameBox.Text = source.Name;
            CategoryBox.Text = Loc.T(source.Category);
            AmountBox.Text = source.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            MethodBox.SelectedIndex = MethodIndex(source.Method);
            NoteBox.Text = source.Note;
            RepeatBox.SelectedIndex = source.Recurring switch
            {
                Recurrence.Weekly => 1,
                Recurrence.Monthly => 2,
                Recurrence.Yearly => 3,
                _ => 0,
            };
            _receiptPath = existing?.ReceiptPath;
        }
        else
        {
            RepeatBox.SelectedIndex = 0;
        }

        if (MethodBox.SelectedIndex < 0) MethodBox.SelectedIndex = 0;
        // A repeated bill belongs to this month, not to the month it was copied from.
        DateBox.SelectedDate = existing?.SpentOn ?? DateTime.Today;
        ShowReceipt();

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public static bool AddNew(Window owner) =>
        new ExpenseWindow(null).By(owner).ShowDialog() == true;

    public static bool Edit(Window owner, Expense expense) =>
        new ExpenseWindow(expense).By(owner).ShowDialog() == true;

    public static bool Repeat(Window owner, Expense template) =>
        new ExpenseWindow(null, template).By(owner).ShowDialog() == true;

    /// <summary>Where a stored method sits in the list — older rows may hold the translated word.</summary>
    private static int MethodIndex(string stored)
    {
        var index = Array.FindIndex(Methods, m =>
            string.Equals(m, stored, StringComparison.OrdinalIgnoreCase) || Loc.T(m) == stored);
        return Math.Max(0, index);
    }

    /// <summary>
    /// The category as the shop stores it. Picking "صيانة" from the list means Maintenance, and
    /// saving the Arabic word would have started a second Maintenance beside the first.
    /// </summary>
    private string StoredCategory(string shown)
    {
        var index = _categories.FindIndex(c => Loc.T(c) == shown || c == shown);
        return index >= 0 ? _categories[index] : shown;
    }

    private void ShowReceipt() =>
        ReceiptText.Text = string.IsNullOrWhiteSpace(_receiptPath)
            ? Loc.T("None attached")
            : System.IO.Path.GetFileName(_receiptPath);

    /// <summary>
    /// Stores the path to a photo of the paper receipt rather than a copy of it. The file
    /// stays where the owner put it; the database is a till database, not a photo album.
    /// </summary>
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a photo of the receipt",
            Filter = "Images and PDF|*.jpg;*.jpeg;*.png;*.pdf|All files|*.*",
        };

        if (dialog.ShowDialog(DialogOwner) == true)
        {
            _receiptPath = dialog.FileName;
            ShowReceipt();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = Loc.T("Say what the money was spent on.");
            NameBox.Focus();
            return;
        }

        if (!decimal.TryParse(AmountBox.Text.Trim().Replace(',', '.'),
                              NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0m)
        {
            ErrorText.Text = Loc.T("Enter an amount greater than zero.");
            AmountBox.Focus();
            return;
        }

        var category = StoredCategory(CategoryBox.Text.Trim());
        if (category.Length == 0) category = "Other";

        try
        {
            var expense = new Expense
            {
                Id = _existing?.Id ?? 0,
                Name = name,
                CategoryId = Link.Shop.Expenses.AddCategory(category),
                Category = category,
                Amount = amount,
                SpentOn = DateBox.SelectedDate ?? DateTime.Today,
                Method = Methods[Math.Max(0, MethodBox.SelectedIndex)],
                Note = NoteBox.Text.Trim(),
                ReceiptPath = _receiptPath,
                Recurring = RepeatBox.SelectedIndex switch
                {
                    1 => Recurrence.Weekly,
                    2 => Recurrence.Monthly,
                    3 => Recurrence.Yearly,
                    _ => Recurrence.None,
                },
            };

            if (_existing is null) Link.Shop.Expenses.Create(expense);
            else Link.Shop.Expenses.Update(expense);

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
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
