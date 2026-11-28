namespace KarakusAluminyum.Web.Models.Crm;

// Otomatik hesaplanan tutarın yuvarlanması: adım (1, 0,50, 10, 50, 100, 500, 1.000) ve yön.
// Teklifte marjdan gelen birim fiyata ve genel toplama, sözleşmede genel toplama uygulanır. Belge içeriğinde (DocumentJson) saklanır.
public sealed class RoundingRule
{
    public const string Nearest = "nearest";
    public const string Up = "up";
    public const string Down = "down";
    public static readonly decimal[] Steps = { 0.5m, 1m, 5m, 10m, 50m, 100m, 500m, 1000m };

    public decimal Step { get; set; }
    public string Direction { get; set; } = Nearest;

    public bool IsActive => Steps.Contains(Step);

    // Geçersiz adım yuvarlamayı kapatır; geçersiz yön en yakına döner.
    public static RoundingRule? Normalize(RoundingRule? rule)
        => rule is null || !rule.IsActive ? null : new RoundingRule { Step = rule.Step, Direction = rule.Direction is Up or Down ? rule.Direction : Nearest };

    public decimal Apply(decimal value)
    {
        if (!IsActive) return value;
        var q = value / Step;
        var r = Direction switch
        {
            Up => decimal.Ceiling(q),
            Down => decimal.Floor(q),
            _ => decimal.Round(q, 0, MidpointRounding.AwayFromZero)
        };
        return decimal.Round(r * Step, 2, MidpointRounding.AwayFromZero);
    }
}

public static class QuoteRules
{
    // totalRounding: genel toplam (müşterinin ödeyeceği tutar) seçilen adıma yuvarlanır. KDV dahil fiyatta KDV yuvarlanmış
    // toplamdan yeniden ayrılır; KDV eklenen fiyatta KDV aynı kalır ve fark "Yuvarlama" satırı olarak basılır.
    public static (decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal) Calculate(
        IEnumerable<QuoteItem> items, decimal discountRate, decimal taxRate, VatMode vatMode = VatMode.Added, RoundingRule? totalRounding = null)
    {
        var (subtotal, discount, tax, grand) = CalculateExact(items, discountRate, taxRate, vatMode);
        if (totalRounding is not { IsActive: true }) return (subtotal, discount, tax, grand);
        var rounded = totalRounding.Apply(grand);
        if (vatMode == VatMode.Included) tax = decimal.Round(rounded - rounded / (1 + taxRate / 100m), 2, MidpointRounding.AwayFromZero);
        return (subtotal, discount, tax, rounded);
    }

    // Yuvarlama farkı: genel toplamın iskonto sonrası net (+ eklenen KDV) tutardan farkı. Yuvarlama yoksa sıfırdır.
    public static decimal RoundingAmount(VatMode vatMode, decimal subtotal, decimal discountAmount, decimal taxAmount, decimal grandTotal)
        => grandTotal - (subtotal - discountAmount + (vatMode == VatMode.Added ? taxAmount : 0m));

    private static (decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal) CalculateExact(
        IEnumerable<QuoteItem> items, decimal discountRate, decimal taxRate, VatMode vatMode)
    {
        if (discountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(discountRate));
        if (taxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(taxRate));
        var lines = items.ToList();
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0)) throw new ArgumentException("Teklif kalemlerinde miktar pozitif, birim fiyat sıfır veya daha büyük olmalıdır.", nameof(items));
        if (lines.Any(x => x.DiscountRate is < 0 or > 100)) throw new ArgumentException("Satır iskontosu %0 ile %100 arasında olmalıdır.", nameof(items));
        // Satır tutarı satır iskontosu düşülmüş tutardır; genel iskonto ara toplama uygulanır (ERP'lerdeki sıra).
        foreach (var line in lines)
            line.LineTotal = LineTotal(line.Quantity, line.UnitPrice, line.DiscountRate);
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

    public static decimal LineTotal(decimal quantity, decimal unitPrice, decimal discountRate)
        => decimal.Round(quantity * unitPrice * (1 - discountRate / 100m), 2, MidpointRounding.AwayFromZero);

    // İskontosuz satır tutarı: proformada "brüt toplam" ve "satır iskontoları" için.
    public static decimal GrossTotal(decimal quantity, decimal unitPrice)
        => decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    // Maliyetten marjla satış fiyatı: kuruşa, yuvarlama seçildiyse seçilen adıma yuvarlanır.
    public static decimal PriceFromCost(decimal unitCost, decimal marginPercent, RoundingRule? rounding = null)
    {
        var price = decimal.Round(unitCost * (1 + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);
        return rounding is { IsActive: true } ? rounding.Apply(price) : price;
    }

    // İç kâr özeti: iskonto ve yuvarlama sonrası KDV hariç satıştan maliyet düşülür. Maliyetler KDV hariçtir.
    public static (decimal NetSales, decimal Profit, decimal? MarginPercent) Profit(Quote quote)
    {
        var net = quote.GrandTotal - quote.TaxAmount;
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
