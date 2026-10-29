namespace KarakusAluminyum.Web.Models.Crm;

public static class QuoteRules
{
    public static (decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal) Calculate(
        IEnumerable<QuoteItem> items, decimal discountRate, decimal taxRate)
    {
        if (discountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(discountRate));
        if (taxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(taxRate));
        var lines = items.ToList();
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0)) throw new ArgumentException("Teklif kalemlerinde miktar pozitif, birim fiyat sıfır veya daha büyük olmalıdır.", nameof(items));
        foreach (var line in lines)
            line.LineTotal = decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var subtotal = lines.Sum(x => x.LineTotal);
        var discount = decimal.Round(subtotal * discountRate / 100m, 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round((subtotal - discount) * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
        return (subtotal, discount, tax, subtotal - discount + tax);
    }

    public static bool CanTransition(QuoteStatus current, QuoteStatus next) => (current, next) switch
    {
        (QuoteStatus.Draft, QuoteStatus.Sent) => true,
        (QuoteStatus.Sent, QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired) => true,
        (QuoteStatus.Accepted, QuoteStatus.Converted) => true,
        _ => false
    };
}
