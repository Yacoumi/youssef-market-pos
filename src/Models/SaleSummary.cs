using System.Globalization;

namespace MarketPos.Models;

/// <summary>One row on the Tickets page — enough to identify a sale without loading its lines.</summary>
public sealed class SaleSummary
{
    public required int InvoiceNumber { get; init; }
    public required DateTime SoldAt { get; init; }
    public required decimal Total { get; init; }
    public required decimal DiscountAmount { get; init; }
    public required PaymentMethod PaymentMethod { get; init; }
    public required int LineCount { get; init; }

    public string Number => "#" + InvoiceNumber;
    public string When => SoldAt.ToString("dd/MM/yyyy  HH:mm", CultureInfo.InvariantCulture);
    public string TotalLabel => Total.ToString("N2", CultureInfo.InvariantCulture) + " DH";
    public string MethodLabel => Services.Loc.T(PaymentMethod switch
    {
        PaymentMethod.Card => "Card",
        PaymentMethod.Other => "Other",
        _ => "Cash",
    });
    public string ItemsLabel => Services.Loc.T(LineCount == 1 ? "{0} item" : "{0} items", LineCount);
    public bool HasDiscount => DiscountAmount > 0;
    public string DiscountLabel => Services.Loc.T("Discount -{0}", DiscountAmount.ToString("N2", CultureInfo.InvariantCulture) + " DH");
}
