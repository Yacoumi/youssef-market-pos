using System.IO;
using System.Windows.Media.Imaging;

namespace MarketPos.Services;

/// <summary>
/// Writes a product photo into the shop's folder.
///
/// The other half of <see cref="ProductImages"/>, kept apart for the same reason its category
/// twin is: this is the only part that needs a graphics stack, and the shared project has no
/// screen attached to it.
///
/// Stored as the barcode with a .png on it, which is the name the catalogue already looks for.
/// So a photo picked in the back office and a photo dropped into the folder by hand are the
/// same thing, and neither needs a row in the database to point at it.
/// </summary>
public static class ProductImageWriter
{
    /// <summary>Longest edge kept. Tiles draw at 190px; twice that covers a high-DPI screen.</summary>
    public const int MaxEdge = 380;

    /// <summary>
    /// Sends a chosen picture to the shop, replacing whatever it held for this product. On the
    /// shop's own machine it is written beside marketpos.db; on a till it travels to the server
    /// and is written there, so every screen that asks the shop shows it.
    /// </summary>
    public static void Save(int productId, string barcode, string sourcePath)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        source.UriSource = new Uri(sourcePath);

        // Decoded straight to tile size rather than loading a 4MB phone photo and shrinking it
        // on every draw of the grid.
        source.DecodePixelWidth = MaxEdge;
        source.EndInit();
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var png = new MemoryStream();
        encoder.Save(png);

        Link.Shop.Stock.SavePhoto(productId, barcode, png.ToArray());

        // A till remembers pictures it has already asked for; this one has just changed.
        ShopImages.Forget();
    }
}
