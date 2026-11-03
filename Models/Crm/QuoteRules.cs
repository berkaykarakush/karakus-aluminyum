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

    // Maliyetten marjla satış fiyatı: kuruşa yuvarlanır.
    public static decimal PriceFromCost(decimal unitCost, decimal marginPercent)
        => decimal.Round(unitCost * (1 + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);

    // İç kâr özeti: iskonto sonrası KDV hariç satıştan maliyet düşülür. Maliyetler KDV hariçtir.
    public static (decimal NetSales, decimal Profit, decimal? MarginPercent) Profit(Quote quote)
    {
        var net = quote.Subtotal - quote.DiscountAmount;
        if (quote.VatMode == VatMode.Included) net = decimal.Round(net - quote.TaxAmount, 2, MidpointRounding.AwayFromZero);
        var profit = net - quote.CostTotal;
        return (net, profit, quote.CostTotal > 0 ? decimal.Round(profit / quote.CostTotal * 100m, 1, MidpointRounding.AwayFromZero) : null);
    }

    public static bool CanTransition(QuoteStatus current, QuoteStatus next) => (current, next) switch
    {
        (QuoteStatus.Draft, QuoteStatus.Sent) => true,
        (QuoteStatus.Sent, QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired) => true,
        (QuoteStatus.Accepted, QuoteStatus.Converted) => true,
        _ => false
    };
}
