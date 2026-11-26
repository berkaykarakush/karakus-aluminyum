using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services.Documents;
using Xunit;

namespace KarakusAluminyum.Tests;

// Kurumsal teklif tablosu (teklif-formati.pdf): ürün grupları, grup toplamları, genel toplam ve alttaki çizimler.
public sealed class QuoteTableTests
{
    private static Quote Quote(VatMode vat, decimal discount, params QuoteItem[] items)
    {
        var quote = new Quote { VatMode = vat, TaxRate = 20, DiscountRate = discount };
        var order = 0;
        foreach (var item in items) { item.SortOrder = order++; item.LineTotal = QuoteRules.LineTotal(item.Quantity, item.UnitPrice, item.DiscountRate); quote.Items.Add(item); }
        (quote.Subtotal, quote.DiscountAmount, quote.TaxAmount, quote.GrandTotal) = QuoteRules.Calculate(quote.Items, discount, 20, vat);
        return quote;
    }

    [Fact]
    public void NamedGroup_HidesLineAmounts_AndAddsGroupTotal_SingleLineGroupKeepsAmount()
    {
        var quote = Quote(VatMode.None, 0,
            new QuoteItem { BlockName = "PVC Doğrama", Description = "4100x2200 4'lü Sürme", Quantity = 6, UnitPrice = 100 },
            new QuoteItem { BlockName = "PVC Doğrama", Description = "900x2200 Açılır Kapı", Quantity = 3, UnitPrice = 50 },
            new QuoteItem { BlockName = "Isıcam", Description = "3'lü Isıcam", Quantity = 186, Unit = "M2", UnitPrice = 10 });
        var table = QuoteTable.From(PriceSheet.FromQuote(quote));

        Assert.Equal(new[] { null, null, (decimal?)750, 1860 }, table.Rows.Select(r => r.Amount));
        var total = table.Rows[2];
        Assert.Equal(("PVC Doğrama Toplamı", (decimal?)9, "Adet", true), (total.Description, total.Quantity, total.Unit, total.IsTotal));
        Assert.Equal(("Isıcam", "3'lü Isıcam", "M2"), (table.Rows[3].Product, table.Rows[3].Description, table.Rows[3].Unit));
        var grand = Assert.Single(table.Totals);
        Assert.Equal(("İşin Genel Toplamı", 2610m, true), (grand.Label, grand.Amount, grand.IsGrand));
    }

    [Fact]
    public void MixedUnits_LeaveGroupQuantityEmpty()
    {
        var quote = Quote(VatMode.None, 0,
            new QuoteItem { BlockName = "Cam", Description = "Isıcam", Quantity = 10, Unit = "M2", UnitPrice = 10 },
            new QuoteItem { BlockName = "Cam", Description = "Çıta", Quantity = 4, Unit = "Adet", UnitPrice = 5 });
        var total = QuoteTable.From(PriceSheet.FromQuote(quote)).Rows[^1];
        Assert.Equal(((decimal?)null, (string?)null, (decimal?)120), (total.Quantity, total.Unit, total.Amount));
    }

    [Fact]
    public void UnnamedLines_ArePricedOneByOne_PozNameGoesToProductColumn_AndVatDiscountRowsFollow()
    {
        var quote = Quote(VatMode.Added, 10,
            new QuoteItem { Description = "Poz 1 - Pencere, Açılır, 1200 × 1400 mm", Quantity = 2, UnitPrice = 1000 },
            new QuoteItem { Description = "Montaj", Quantity = 1, UnitPrice = 500 });
        var table = QuoteTable.From(PriceSheet.FromQuote(quote));

        Assert.Equal(("Poz 1", "Pencere, Açılır, 1200 × 1400 mm", (decimal?)2000), (table.Rows[0].Product, table.Rows[0].Description, table.Rows[0].Amount));
        Assert.Equal(((string?)null, "Montaj", (decimal?)500), (table.Rows[1].Product, table.Rows[1].Description, table.Rows[1].Amount));
        Assert.Equal(new[] { "Ara Toplam", "İskonto (%10)", "KDV (%20)", "İşin Genel Toplamı (KDV dahil)" }, table.Totals.Select(t => t.Label));
        Assert.Equal(new[] { 2500m, -250m, 450m, 2700m }, table.Totals.Select(t => t.Amount));
    }

    [Theory]
    [InlineData("Montaj ve nakliye", null, "Montaj ve nakliye")]
    [InlineData("Poz 12 - Sürme", "Poz 12", "Sürme")]
    [InlineData("Poz 1 - ", null, "Poz 1 - ")]
    [InlineData(" - Sürme", null, " - Sürme")]
    public void Split_TakesShortPrefixAsProduct(string text, string? product, string description)
        => Assert.Equal((product, description), QuoteTable.Split(text));

    // Teklifin altındaki çizimler: seçilen çizimler önce, çizimden gelen kalemlerin pozları ardından, tekrarsız.
    [Fact]
    public void QuoteDesignIds_UnionsSelectedDrawingsAndItemDesigns()
    {
        var quote = new Quote { Items = { new QuoteItem { WindowDesignId = 7, SortOrder = 1 }, new QuoteItem { WindowDesignId = 3, SortOrder = 0 }, new QuoteItem { SortOrder = 2 } } };
        Assert.Equal(new[] { 5, 3, 7 }, DocumentViews.QuoteDesignIds(quote, new DocumentContent { DesignIds = { 5, 3 } }));
    }
}
