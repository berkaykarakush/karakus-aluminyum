using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Services.Documents;

// Kurumsal teklif tablosu (teklif-formati.pdf): Ürün Adı | Ürün Açıklaması | Miktar | Birim | Tutar.
// Adı olan bölüm bir ürün grubudur: satırlarında tutar yazılmaz, altına "<ad> Toplamı" satırı gelir; tek satırlık grupta
// tutar satırın kendisindedir. Adsız kalemler tek tek fiyatlanır; çizimden gelen "Poz 1 - ..." açıklamasında
// poz adı Ürün Adı sütununa geçer, böylece tablo alttaki çizimlerle eşleşir. Ardından iskonto, KDV ve genel toplam gelir.
public sealed record QuoteTableRow(string? Product, string Description, decimal? Quantity, string? Unit, decimal? Amount, bool IsTotal = false);

public sealed record QuoteTableTotal(string Label, decimal Amount, bool IsGrand = false);

public sealed record QuoteTable(IReadOnlyList<QuoteTableRow> Rows, IReadOnlyList<QuoteTableTotal> Totals, decimal GrandTotal)
{
    private const int MaxProductLength = 40;

    public static QuoteTable From(PriceSheet sheet)
    {
        var rows = new List<QuoteTableRow>();
        foreach (var section in sheet.Sections)
        {
            if (section.Name is null)
            {
                foreach (var line in section.Lines)
                {
                    var (product, description) = Split(line.Description);
                    rows.Add(new QuoteTableRow(product, description, line.Quantity, line.Unit, line.Total));
                }
                continue;
            }
            if (section.Lines.Count == 1)
            {
                var line = section.Lines[0];
                rows.Add(new QuoteTableRow(section.Name, line.Description, line.Quantity, line.Unit, line.Total));
                continue;
            }
            rows.AddRange(section.Lines.Select(line => new QuoteTableRow(section.Name, line.Description, line.Quantity, line.Unit, null)));
            // Birimler aynıysa miktarlar toplanır (ör. 75 Adet); karışık birimde toplam miktar yazılmaz.
            var units = section.Lines.Select(l => l.Unit).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var single = units.Count == 1;
            rows.Add(new QuoteTableRow(null, section.Name + " Toplamı", single ? section.Lines.Sum(l => l.Quantity) : null, single ? units[0] : null, section.Total, IsTotal: true));
        }

        var totals = new List<QuoteTableTotal>();
        var rate = (decimal v) => "%" + v.ToString("0.##", TurkishNumbers.Culture);
        if (sheet.DiscountAmount > 0 || sheet.VatMode == VatMode.Added) totals.Add(new QuoteTableTotal("Ara Toplam", sheet.Subtotal));
        if (sheet.DiscountAmount > 0) totals.Add(new QuoteTableTotal($"İskonto ({rate(sheet.DiscountRate)})", -sheet.DiscountAmount));
        if (sheet.VatMode == VatMode.Added) totals.Add(new QuoteTableTotal($"KDV ({rate(sheet.TaxRate)})", sheet.TaxAmount));
        if (sheet.VatMode == VatMode.Included) totals.Add(new QuoteTableTotal($"Fiyata dahil KDV ({rate(sheet.TaxRate)})", sheet.TaxAmount));
        totals.Add(new QuoteTableTotal("İşin Genel Toplamı" + (sheet.VatMode == VatMode.None ? "" : " (KDV dahil)"), sheet.GrandTotal, IsGrand: true));
        return new QuoteTable(rows, totals, sheet.GrandTotal);
    }

    // "Poz 1 - Pencere, Açılır, 1200 × 1400 mm" → ("Poz 1", "Pencere, Açılır, 1200 × 1400 mm").
    public static (string? Product, string Description) Split(string text)
    {
        var at = text.IndexOf(" - ", StringComparison.Ordinal);
        if (at <= 0 || at > MaxProductLength || at + 3 >= text.Length) return (null, text);
        return (text[..at].Trim(), text[(at + 3)..].Trim());
    }
}
