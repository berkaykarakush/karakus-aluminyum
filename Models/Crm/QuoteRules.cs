namespace KarakusAluminyum.Web.Models.Crm;

public static class QuoteRules
{
    public static (decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal) Calculate(
        IEnumerable<QuoteItem> items, decimal discountRate, decimal taxRate, VatMode vatMode = VatMode.Added)
    {
        if (discountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(discountRate));
        if (taxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(taxRate));
        var lines = items.ToList();
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0)) throw new ArgumentException("Teklif kalemlerinde miktar pozitif, birim fiyat sıfır veya daha büyük olmalıdır.", nameof(items));
        foreach (var line in lines)
            line.LineTotal = decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var subtotal = lines.Sum(x => x.LineTotal);
        var discount = decimal.Round(subtotal * discountRate / 100m, 2, MidpointRounding.AwayFromZero);
        var net = subtotal - discount;
        switch (vatMode)
        {
            case VatMode.Included:
                return (subtotal, discount, decimal.Round(net - net / (1 + taxRate / 100m), 2, MidpointRounding.AwayFromZero), net);
            case VatMode.None:
                return (subtotal, discount, 0m, net);
            default:
                var tax = decimal.Round(net * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
                return (subtotal, discount, tax, net + tax);
        }
    }

    public static bool CanTransition(QuoteStatus current, QuoteStatus next) => (current, next) switch
    {
        (QuoteStatus.Draft, QuoteStatus.Sent) => true,
        (QuoteStatus.Sent, QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired) => true,
        (QuoteStatus.Accepted, QuoteStatus.Converted) => true,
        _ => false
    };
}
