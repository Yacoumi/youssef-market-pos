using MarketPos.Views;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit a product, in the same few questions as the Add product page: barcode, name,
/// category, how it is sold, what it cost, what it sells for, how many, and when it goes off.
///
/// <para>
/// On an edit the quantity is how many more arrived, not a number to type over. The shelf
/// count only moves through a stock movement with a reason, so a delivery added here is
/// recorded as a delivery, and a correction still belongs on Inventory → Adjust.
/// </para>
///
/// <para>
/// What the short form does not ask — VAT bracket, minimum stock, shelf, supplier, SKU,
/// whether it shows at the till — is kept as it was on an edit, and takes the shop's default
/// on a new product.
/// </para>
/// </summary>
public partial class ProductWindow : MarketPos.Views.DialogWindow
{
    private readonly StockItem? _existing;

    /// <summary>A photo chosen in this dialog but not yet saved. Null means "leave it alone".</summary>
    private string? _pickedFrom;

    /// <summary>The two ways the shop sells: by the piece, or by the kilo.</summary>
    private static readonly (string Label, Unit Unit)[] Units =
    [
        ("Per unit", Unit.Each),
        ("Kilogram (kg)", Unit.Kg),
    ];

    public ProductWindow(StockItem? existing)
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);
        _existing = existing;

        UnitBox.ItemsSource = Units.Select(u => Loc.T(u.Label)).ToList();

        // Asked of the shop on a till. This machine has no categories of its own, and a
        // dropdown filled from an empty local table would file every product under nothing.
        CategoryBox.ItemsSource = Link.Shop.Categories.List().Select(c => c.Name).ToList();

        if (existing is null) FillForNew(); else FillFrom(existing);

        ShowPicture();
        UpdateLabels();
    }

    public static bool AddNew(Window owner) =>
        new ProductWindow(null).By(owner).ShowDialog() == true;

    /// <summary>
    /// The same form, opened already knowing the barcode — the cashier has just scanned
    /// something the shop does not sell yet. The caret starts on the name.
    /// </summary>
    public static bool AddScanned(Window owner, string barcode)
    {
        var form = new ProductWindow(null).By(owner);
        form.BarcodeBox.Text = barcode;
        form.FocusOn(form.NameBox);
        return form.ShowDialog() == true;
    }

    public static bool Edit(Window owner, StockItem item) =>
        new ProductWindow(item).By(owner).ShowDialog() == true;

    // ------------------------------- Filling in -------------------------------

    private void FillForNew()
    {
        HeadingText.Text = Loc.T("New product");
        SubText.Text = Loc.T("Scan the barcode, or leave it empty for goods with nothing printed on them.");
        Title = "Add product";

        UnitBox.SelectedIndex = 0;

        // At least one is in somebody's hand. Starting at 0 made the sale that follows a
        // scan refuse the product the moment it was created.
        QuantityBox.Text = "1";

        CategoryBox.Text = (CategoryBox.ItemsSource as List<string>)?.FirstOrDefault() ?? string.Empty;
        SaveButton.Content = Loc.T("Save product");
        FocusOn(BarcodeBox);
    }

    private void FillFrom(StockItem item)
    {
        HeadingText.Text = item.Name;
        SubText.Text = Loc.T("Change the details, or enter how many arrived to add them to stock.");
        Title = item.Name;

        BarcodeBox.Text = item.Barcode;
        ShowBarcodeRow(item.Barcode.Length > 0);

        NameBox.Text = item.Name;
        CategoryBox.Text = item.Category;
        UnitBox.SelectedIndex = item.Unit == Unit.Kg ? 1 : 0;
        CostBox.Text = item.Cost > 0m ? item.Cost.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        PriceBox.Text = item.Price.ToString("0.00", CultureInfo.InvariantCulture);
        ExpiryBox.SelectedDate = item.ExpiresOn;

        QuantityBox.Text = "0";
        InStockText.Visibility = Visibility.Visible;
        InStockText.Text = Loc.T("In stock now: {0}. The quantity above is added to it.",
                                 Loc.Ltr(StockLabel(item.Stock, item.Unit)));

        SaveButton.Content = Loc.T("Save changes");
        FocusOn(NameBox);
    }

    private static string StockLabel(decimal stock, Unit unit) =>
        unit == Unit.Kg ? $"{stock:0.###} {Loc.T("kg")}" : $"{stock:0.###}";

    private Unit SelectedUnit => Units[Math.Clamp(UnitBox.SelectedIndex, 0, Units.Length - 1)].Unit;

    private void Unit_Changed(object sender, RoutedEventArgs e) => UpdateLabels();

    private void UpdateLabels()
    {
        if (CostLabel is null || UnitBox.ItemsSource is null) return;

        var weighed = SelectedUnit == Unit.Kg;
        CostLabel.Text = Loc.T(weighed ? "BOUGHT FOR / KG" : "BOUGHT FOR");
        PriceLabel.Text = Loc.T(weighed ? "SELLING FOR / KG" : "SELLING FOR");
        QuantityLabel.Text = Loc.T(_existing is null
            ? (weighed ? "WEIGHT (KG)" : "QUANTITY")
            : (weighed ? "ADD WEIGHT (KG)" : "ADD QUANTITY"));

        UpdateTotals();
    }

    private void Amount_Changed(object sender, RoutedEventArgs e) => UpdateTotals();

    /// <summary>What this delivery cost, and what the shop makes on each sale.</summary>
    private void UpdateTotals()
    {
        if (TotalCostText is null) return;

        var hasCost = TryAmount(CostBox.Text, out var cost);
        var hasPrice = TryAmount(PriceBox.Text, out var price);
        var hasQuantity = TryAmount(QuantityBox.Text, out var quantity);

        TotalCostText.Text = hasCost && hasQuantity && cost > 0m && quantity > 0m
            ? Loc.Ltr($"{Math.Round(cost * quantity, 2):N2} DH")
            : "—";

        if (!hasPrice || price <= 0m)
        {
            MarginText.Text = "—";
            MarginText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Muted");
            return;
        }

        var margin = price - (hasCost ? cost : 0m);
        MarginText.Text = Loc.Ltr($"{margin:N2} DH  ·  {margin / price * 100m:0.#}%");

        // Selling below cost loses money on every single sale, quietly.
        MarginText.Foreground = (System.Windows.Media.Brush)FindResource(
            margin < 0m ? "Brush.Danger" : "Brush.Accent");
    }

    // ------------------------------- Barcode -------------------------------

    private void NoBarcode_Click(object sender, RoutedEventArgs e)
    {
        BarcodeBox.Clear();
        ShowBarcodeRow(false);
        FocusOn(NameBox);
    }

    private void HasBarcode_Click(object sender, RoutedEventArgs e)
    {
        ShowBarcodeRow(true);
        FocusOn(BarcodeBox);
    }

    private void ShowBarcodeRow(bool showing)
    {
        BarcodeRow.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        NoBarcodeNote.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Says at once when the code belongs to a product the shop already has.</summary>
    private void Barcode_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;

        ShowPicture();

        var taken = TakenBy(BarcodeBox.Text.Trim());
        ErrorText.Text = taken is null
            ? string.Empty
            : Loc.T("This product is already in inventory: {0}", taken.Name);
    }

    /// <summary>The other product already carrying this code, if there is one.</summary>
    private Product? TakenBy(string barcode)
    {
        var found = Catalog.FindByBarcode(barcode);
        return found is not null && found.Id != (_existing?.Id ?? 0) ? found : null;
    }

    // ------------------------------- Saving -------------------------------

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var name = NameBox.Text.Trim();
        var category = CategoryBox.Text.Trim();
        var barcode = BarcodeRow.Visibility == Visibility.Visible ? BarcodeBox.Text.Trim() : string.Empty;

        if (name.Length == 0) { Fail("Give the product a name.", NameBox); return; }
        if (category.Length == 0) { Fail("Choose or type a category.", CategoryBox); return; }

        if (!TryAmount(PriceBox.Text, out var price) || price <= 0m)
        {
            Fail("Enter what it sells for.", PriceBox);
            return;
        }

        if (!TryAmount(CostBox.Text, out var cost) || cost < 0m)
        {
            Fail("The purchase price must be a number, like 6.20.", CostBox);
            return;
        }

        if (!TryAmount(QuantityBox.Text, out var quantity) || quantity < 0m)
        {
            Fail("Enter how many arrived.", QuantityBox);
            return;
        }

        if (_existing is null && quantity <= 0m)
        {
            Fail("Enter how many arrived.", QuantityBox);
            return;
        }

        if (barcode.Length > 0 && TakenBy(barcode) is { } taken)
        {
            Fail(Loc.T("This product is already in inventory: {0}", taken.Name), BarcodeBox);
            return;
        }

        // The till's catalogue only holds what is on sale. On the shop's own machine a removed
        // product still owns its barcode, and only the database can say so.
        if (barcode.Length > 0 && !Catalog.BelongsToAServer
            && Link.Shop.Stock.BarcodeTaken(barcode, _existing?.Id ?? 0))
        {
            Fail("That barcode already belongs to another product.", BarcodeBox);
            return;
        }

        var unit = SelectedUnit;
        var item = new StockItem
        {
            Id = _existing?.Id ?? 0,
            Name = name,
            Category = category,
            Barcode = barcode,
            Cost = cost,
            Price = price,
            Unit = unit,
            ExpiresOn = ExpiryBox.SelectedDate,

            // Not on this form: kept as they were, or the shop's defaults for a new product.
            Sku = _existing?.Sku ?? string.Empty,
            TaxRate = _existing?.TaxRate ?? VatForCategory(category),
            MinStock = _existing?.MinStock ?? AppSettings.Current.DefaultLowStock,
            Shelf = _existing?.Shelf ?? string.Empty,
            SupplierId = _existing?.SupplierId,
            // Saving this form is how a product is put on sale — including one that arrived
            // from a supplier and has been waiting in stock off the till.
            ShowInPos = true,

            // Only a path somebody set deliberately is stored. The catalogue finds the usual
            // file on its own — and a till's picture link is never a path to store.
            ImagePath = ShopImages.IsToken(_existing?.ImagePath)
                        || ProductImages.IsTheUsualPlace(_existing?.ImagePath,
                               _existing is null ? barcode : ProductImages.NameFor(_existing.Id, _existing.Barcode))
                ? null
                : _existing?.ImagePath,
        };

        SaveButton.IsEnabled = false;

        try
        {
            var savedId = _existing?.Id ?? 0;

            if (_existing is null)
            {
                savedId = await Create(item, quantity);
                if (savedId <= 0) return;
            }
            else
            {
                // A new barcode is a new file name: the photo is carried over to it.
                if (_pickedFrom is null && (_existing.Barcode ?? "").Trim() != barcode)
                    _carriedPhoto = CurrentPhotoBytes(_existing);

                Link.Shop.Stock.Update(item);

                if (quantity > 0m)
                    Link.Shop.Stock.ReceiveAtTill(item.Id, quantity, cost: null, price: null, expiresOn: null);
            }

            FilePicture(savedId, barcode);

            // A till keeps the shop's catalogue in memory; it has to be asked again before the
            // change is on this screen.
            if (Catalog.BelongsToAServer) await ShopLink.PullCatalogue();
            Catalog.Reload();

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
        finally
        {
            if (IsLoaded) SaveButton.IsEnabled = true;
        }
    }

    /// <summary>Puts a new product into the shop, wherever the shop is. Its id, or 0 when refused.</summary>
    private async Task<int> Create(StockItem item, decimal openingStock)
    {
        if (!Catalog.BelongsToAServer)
            return Link.Shop.Stock.Create(item, openingStock);

        // A till holds a copy of the shop, not the shop: a new product goes to the server,
        // which is the only place the back office and the other tills will look for it.
        var made = await ShopLink.AddProduct(new Link.NewProduct(
            item.Barcode, item.Name, item.Category, item.Price, item.Cost, item.TaxRate,
            item.Unit.ToString(), openingStock,
            Session.Current?.Name ?? Session.OwnerLabel));

        if (made is null)
        {
            ErrorText.Text = Loc.T("The shop's server did not take it: {0}", ShopLink.LastProblem);
            return 0;
        }

        if (made.AlreadyHad)
        {
            Fail(Loc.T("This product is already in inventory: {0}", made.Name), BarcodeBox);
            return 0;
        }

        return made.Id;
    }

    /// <summary>
    /// Borrows the VAT bracket from whatever else is in that category, which is right far more
    /// often than a fixed default. A brand new category falls back to the standard rate.
    /// </summary>
    private static decimal VatForCategory(string category)
    {
        var siblings = Catalog.Products
            .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return siblings.Count == 0
            ? 0.20m
            : siblings.GroupBy(p => p.TaxRate).OrderByDescending(g => g.Count()).First().Key;
    }

    // ------------------------------- Photo -------------------------------

    private void Picture_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose a photo for this product"),
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(DialogOwner) != true) return;

        _pickedFrom = picker.FileName;
        ShowPicture();
    }

    /// <summary>
    /// Draws whichever photo is current: the one just chosen, or the one already on file.
    /// Loaded with OnLoad so the file is not left open.
    /// </summary>
    private void ShowPicture()
    {
        if (PictureBox is null) return;

        // The photo just picked, or the one the shop already holds: a file here on the shop's
        // own machine, and on a till the picture asked of the server.
        var source = _pickedFrom
            ?? (_existing is null ? null
                : Catalog.BelongsToAServer ? ShopImages.ProductToken(_existing.Id)
                : ProductImages.Find(ProductImages.NameFor(_existing.Id, _existing.Barcode)));

        PictureBox.Source = new MarketPos.Converters.ImagePathConverter()
            .Convert(source, typeof(object), null, CultureInfo.InvariantCulture) as System.Windows.Media.ImageSource;

        PicturePrompt.Visibility = PictureBox.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private byte[]? _carriedPhoto;

    /// <summary>The photo the shop holds for this product now, as bytes, or null.</summary>
    private static byte[]? CurrentPhotoBytes(StockItem product)
    {
        try
        {
            if (Catalog.BelongsToAServer) return ShopImages.Bytes(ShopImages.ProductToken(product.Id));
            var file = ProductImages.Find(ProductImages.NameFor(product.Id, product.Barcode));
            return file is null ? null : System.IO.File.ReadAllBytes(file);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the photo to the shop under the product it belongs to. With or without a barcode:
    /// the goods with nothing printed on them are the ones the cashier finds by their picture.
    /// </summary>
    private void FilePicture(int productId, string barcode)
    {
        if (_carriedPhoto is { } carried && productId > 0)
        {
            _carriedPhoto = null;
            Link.Shop.Stock.SavePhoto(productId, barcode, carried);
            ShopImages.Forget();
        }

        if (_pickedFrom is null || productId <= 0) return;

        ProductImageWriter.Save(productId, barcode, _pickedFrom);
    }

    // ------------------------------- Helpers -------------------------------

    /// <summary>Accepts both "8.50" and "8,50", and an empty box as zero.</summary>
    private static bool TryAmount(string? text, out decimal value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) { value = 0m; return true; }

        return decimal.TryParse(trimmed.Replace(',', '.'), NumberStyles.Number,
                                CultureInfo.InvariantCulture, out value);
    }

    private void Fail(string message, System.Windows.Controls.Control focus)
    {
        ErrorText.Text = Loc.T(message);
        FocusOn(focus);
    }

    /// <summary>Focus at Background priority: a field just shown is not yet focusable.</summary>
    private void FocusOn(System.Windows.Controls.Control control) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            control.Focus();
            Keyboard.Focus(control);
            if (control is System.Windows.Controls.TextBox box) box.SelectAll();
        }));

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
