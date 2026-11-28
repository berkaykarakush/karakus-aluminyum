using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;

namespace KarakusAluminyum.Web.Services.Documents;

// Proforma fiyat tablosu (ERP'lerdeki satış teklifi / proforma düzeni): bölümlere ayrılmış kalemler,
// satır iskontosu, genel iskonto, KDV matrahı, KDV ve genel toplam. Teklif ve sözleşme aynı tabloyu basar.
// Tutarlar QuoteRules ile hesaplanır; maliyet ve marj bu tabloya hiç girmez.
public sealed record PriceLine(int No, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountRate, decimal Total);

public sealed record PriceSection(string? Name, IReadOnlyList<PriceLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.Total);
}

public sealed record PriceSheet(
    IReadOnlyList<PriceSection> Sections, VatMode VatMode, decimal TaxRate, decimal DiscountRate,
    decimal Gross, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal)
{
    public bool HasLines => Sections.Any(s => s.Lines.Count > 0);
    public bool HasLineDiscount => Sections.SelectMany(s => s.Lines).Any(l => l.DiscountRate > 0);
    public decimal LineDiscount => Gross - Subtotal;
    public decimal Net => Subtotal - DiscountAmount; // genel iskonto sonrası
    // Genel toplamın yuvarlanmasından doğan fark (yuvarlama yoksa sıfır).
    public decimal Rounding => QuoteRules.RoundingAmount(VatMode, Subtotal, DiscountAmount, TaxAmount, GrandTotal);
    // KDV matrahı: KDV hariç net tutar. KDV dahil fiyatta yuvarlanmış toplamdan ayrılır.
    public decimal TaxBase => VatMode == VatMode.Included ? GrandTotal - TaxAmount : Net;

    public static PriceSheet FromQuote(Quote quote)
    {
        var items = quote.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var sections = Group(items.Select(i => (i.BlockName, i.Description, i.Quantity, i.Unit, i.UnitPrice, i.DiscountRate)));
        var gross = items.Sum(i => QuoteRules.GrossTotal(i.Quantity, i.UnitPrice));
        return new PriceSheet(sections, quote.VatMode, quote.TaxRate, quote.DiscountRate, gross, quote.Subtotal, quote.DiscountAmount, quote.TaxAmount, quote.GrandTotal);
    }

    // Sözleşmede kalemler belge içeriğindeki bloklarda tutulur; toplamlar aynı kuralla hesaplanır.
    public static PriceSheet FromContent(DocumentContent content, VatMode vatMode, decimal taxRate, decimal discountRate)
    {
        var rows = content.Blocks.SelectMany(b => b.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description))
            .Select(i => (Name: string.IsNullOrWhiteSpace(b.Name) ? null : b.Name.Trim(), Description: i.Description!.Trim(), i.Quantity, Unit: string.IsNullOrWhiteSpace(i.Unit) ? "Adet" : i.Unit.Trim(), i.UnitPrice, i.DiscountRate))).ToList();
        var items = rows.Select(r => new QuoteItem { Quantity = r.Quantity, UnitPrice = r.UnitPrice, DiscountRate = r.DiscountRate }).ToList();
        var totals = items.Count == 0 ? (0m, 0m, 0m, 0m) : QuoteRules.Calculate(items, discountRate, taxRate, vatMode, content.TotalRounding);
        var gross = rows.Sum(r => QuoteRules.GrossTotal(r.Quantity, r.UnitPrice));
        return new PriceSheet(Group(rows), vatMode, taxRate, discountRate, gross, totals.Item1, totals.Item2, totals.Item3, totals.Item4);
    }

    private static List<PriceSection> Group(IEnumerable<(string? Section, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountRate)> rows)
    {
        var sections = new List<(string? Name, List<PriceLine> Lines)>();
        var no = 0;
        foreach (var r in rows)
        {
            var name = string.IsNullOrWhiteSpace(r.Section) ? null : r.Section.Trim();
            if (sections.Count == 0 || sections[^1].Name != name) sections.Add((name, new List<PriceLine>()));
            sections[^1].Lines.Add(new PriceLine(++no, r.Description, r.Quantity, r.Unit, r.UnitPrice, r.DiscountRate, QuoteRules.LineTotal(r.Quantity, r.UnitPrice, r.DiscountRate)));
        }
        return sections.Select(s => new PriceSection(s.Name, s.Lines)).ToList();
    }
}
