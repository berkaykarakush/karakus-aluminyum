using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class PriceListParsingTests
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("€ 45", 45)]
    public void TryPrice_ReadsTurkishAndInternationalNumbers(string text, double expected)
    {
        Assert.True(PriceLists.TryPrice(text, out var price));
        Assert.Equal((decimal)expected, price);
    }

    [Fact]
    public void ParsePriceList_SkipsHeader_AndReportsBadRows()
    {
        var (rows, errors) = PriceLists.ParsePriceList("Kod;Fiyat;Para\nK-70;12,50;€\nC-1\t45\nX;abc\nY;5;GBP\n\n");
        Assert.Equal(new[] { ("K-70", 12.50m, "EUR"), ("C-1", 45m, (string?)null) }, rows.Select(r => (r.Code, r.Price, r.Currency)));
        Assert.Equal(2, errors.Count); // "abc" fiyatı ve GBP
    }

    [Fact]
    public void ParseTcmb_ReadsForexSellingPerUnit()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Tarih_Date Tarih="21.11.2026" Date="11/21/2026" Bulten_No="2026/221">
              <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD"><Unit>1</Unit><Isim>ABD DOLARI</Isim><ForexBuying>40.1000</ForexBuying><ForexSelling>40.2000</ForexSelling></Currency>
              <Currency CrossOrder="9" Kod="EUR" CurrencyCode="EUR"><Unit>1</Unit><Isim>EURO</Isim><ForexBuying>46.5000</ForexBuying><ForexSelling>46.6000</ForexSelling></Currency>
              <Currency CrossOrder="12" Kod="JPY" CurrencyCode="JPY"><Unit>100</Unit><ForexSelling>26.0000</ForexSelling></Currency>
            </Tarih_Date>
            """;
        var (date, rates) = PriceLists.ParseTcmb(xml);
        Assert.Equal(new DateOnly(2026, 11, 21), date);
        Assert.Equal(new[] { ("USD", 40.2m), ("EUR", 46.6m) }, rates);
    }
}

[Collection("postgres")]
public sealed class PriceListPageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PriceList_Rates_CostAndPurchaseDraft()
    {
        await using var app = postgres.CreateApp("price_lists");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (systemId, frame, gasket, supplier) = await app.WithDbAsync(async db =>
        {
            var frame = new Product { Code = "PL-K70", Name = "Kasa profili", Unit = "Boy" };
            var gasket = new Product { Code = "PL-C1", Name = "Conta", Unit = "Metre" };
            var supplier = new KarakusAluminyum.Web.Models.Crm.Customer { Name = "Profil tedarikçisi", PartyType = PartyType.Supplier };
            db.AddRange(frame, gasket, supplier);
            await db.SaveChangesAsync();
            var system = new ProfileSystem { Name = "Fiyat serisi", MaterialsJson = MaterialTakeoff.SerializeRules(new[] {
                new MaterialRule { ProductId = frame.Id, Basis = MaterialBases.ProfileBars, Key = "Kasa" }, new MaterialRule { ProductId = gasket.Id, Basis = MaterialBases.Perimeter } }) };
            db.ProfileSystems.Add(system);
            await db.SaveChangesAsync();
            return (system.Id, frame.Id, gasket.Id, supplier.Id);
        });

        await client.PostFormAsync("/yonetim/fiyat-listeleri/kur", token, ("currency", "EUR"), ("rate", "40"), ("date", "2026-11-20"));
        await client.PostFormAsync("/yonetim/fiyat-listeleri/kur", token, ("currency", "EUR"), ("rate", "50"), ("date", "2026-11-22")); // en yeni kur kullanılır
        await client.PostFormAsync("/yonetim/fiyat-listeleri/yukle", token, ("text", "pl-k70;100,00\nPL-C1;20;TRY\nYOK-1;5"), ("currency", "EUR"));
        var products = await app.WithDbAsync(db => db.Products.AsNoTracking().Where(p => p.Id == frame || p.Id == gasket).ToDictionaryAsync(p => p.Id));
        Assert.Equal((100m, "EUR"), (products[frame].PurchasePrice, products[frame].PriceCurrency));
        Assert.Equal((20m, "TRY"), (products[gasket].PurchasePrice, products[gasket].PriceCurrency));
        var index = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/fiyat-listeleri"));
        Assert.Contains("EUR 50,0000 ₺", index);
        Assert.Contains("5.000,00 ₺", index); // 100 EUR × 50

        await DrawingLists.PostAsync(client, token, "Fiyat listesi işi", DrawingLists.Rows(DrawingLists.Row("Salon", 1000, 1000, quantity: 1, system: systemId)));
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Fiyat işi"), ("DrawingListId", (await DrawingLists.ListIdAsync(app, "Fiyat listesi işi")).ToString()), ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Fiyat işi").Select(o => o.Id).SingleAsync());
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/metraj/{orderId}"));
        Assert.Contains("5.080,00 ₺", page); // 1 boy × 100 EUR × 50 + 4 m conta × 20 TL

        await client.PostFormAsync($"/yonetim/siparisler/alis-taslagi/{orderId}", token); // tedarikçi zorunlu
        Assert.False(await app.WithDbAsync(db => db.Invoices.AnyAsync(i => i.CustomerId == supplier)));
        await client.PostFormAsync($"/yonetim/siparisler/alis-taslagi/{orderId}", token, ("supplierId", supplier.ToString()));
        var invoice = await app.WithDbAsync(db => db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.CustomerId == supplier));
        Assert.Equal((InvoiceKind.Purchase, InvoiceStatus.Draft, "TRY"), (invoice.Kind, invoice.Status, invoice.Currency)); // karışık para birimi → TL
        Assert.Equal(5000m, invoice.Lines.Single(l => l.ProductId == frame).UnitPrice);
        Assert.Equal(4m, invoice.Lines.Single(l => l.ProductId == gasket).Quantity);
        Assert.Equal(5080m, invoice.Net);
    }

    [Fact]
    public async Task Planning_SavesUnitCapacity()
    {
        await using var app = postgres.CreateApp("planning_units");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/planlama/kapasite", token, ("capacity", "40"), ("unitCapacity", "25"));
        var plan = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/siparisler/planlama"));
        Assert.Contains("name=\"unitCapacity\" type=\"number\" min=\"0\" step=\"1\" value=\"25\"", plan);
        Assert.Contains("value=\"40\"", plan);
    }
}
