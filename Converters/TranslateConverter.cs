using System.Globalization;
using System.Windows.Data;
using MarketPos.Services;

namespace MarketPos.Converters;

/// <summary>
/// Shows a word the app stored in English — "Cash", "Rent" — in the shop's language.
///
/// Bound text is left alone by the localizer on purpose, because most of it is the shop's own
/// (a product called "Pay"). These few columns hold the app's own vocabulary instead, so they
/// are translated at the binding. A word the table does not know comes back unchanged.
/// </summary>
public sealed class TranslateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? string.Empty : Loc.T(value.ToString() ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
