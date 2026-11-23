using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README madde 2: çıktılarda tarih yazdırma anına göre basılır, sayfadaki seçiciyle başka gün seçilir; kayıtlı tarihler değişmez.
[Collection("postgres")]
public sealed class PrintDateTests(PostgresFixture postgres)
{
    private static string Today => PrintDates.Today.ToString("dd.MM.yyyy");

    [Theory]
    [InlineData("2026-03-05", 2026, 3, 5)]
    [InlineData("05.03.2026", 0, 0, 0)]
    [InlineData("", 0, 0, 0)]
    [InlineData("2026-02-30", 0, 0, 0)]
    public void From_ParsesIsoDate_OrFallsBackToToday(string value, int y, int m, int d)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tarih=" + value);
        Assert.Equal(y == 0 ? PrintDates.Today : new DateOnly(y, m, d), PrintDates.From(context.Request));
    }

    [Fact]
    public async Task EveryPrintout_DefaultsToToday_AndAcceptsAPickedDate()
    {
        await using var app = postgres.CreateApp("print_dates");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await DrawingLists.PostAsync(client, token, "Tarih listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 1)));
        var listId = await DrawingLists.ListIdAsync(app, "Tarih listesi");
        var ids = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "T-TARIH-1", Title = "Tarih işi", CustomerId = customer, Status = QuoteStatus.Accepted, TaxRate = 20, DocumentDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), PublicToken = new string('a', 22) };
            var invoice = new Invoice { Kind = InvoiceKind.Sale, CustomerId = customer, Date = new DateOnly(2026, 1, 12), DueDate = new DateOnly(2026, 2, 12) };
            var glassType = new GlassType { Name = "Tarih camı" };
            var glass = new GlassList { Name = "Tarih cam listesi", Items = { new GlassListItem { GlassType = glassType, WidthMm = 500, HeightMm = 600, Quantity = 1 } } };
            db.AddRange(quote, invoice, glass);
            await db.SaveChangesAsync();
            return (Quote: quote.Id, Invoice: invoice.Id, Glass: glass.Id);
        });
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Tarih siparişi"), ("DrawingListId", listId.ToString()), ("OrderDate", "2026-01-15"));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Tarih siparişi").Select(o => o.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/siparisler/irsaliye/ekle/{orderId}", token, ("Date", "2026-01-20"), ("Lines[0].Description", "Salon"), ("Lines[0].Quantity", "1"), ("Lines[0].Unit", "Adet"));
        var noteId = await app.WithDbAsync(db => db.DeliveryNotes.Where(n => n.ProductionOrderId == orderId).Select(n => n.Id).SingleAsync());

        var pages = new (string Url, string Picked)[]
        {
            ($"/yonetim/crm/teklifler/yazdir/{ids.Quote}", "05/03/2026"),
            ($"/yonetim/muhasebe/faturalar/yazdir/{ids.Invoice}", "05.03.2026"),
            ($"/yonetim/siparisler/irsaliye/{noteId}", "05.03.2026"),
            ($"/yonetim/siparisler/is-emri/{orderId}", "05.03.2026"),
            ($"/yonetim/siparisler/teslim-tutanagi/{orderId}", "05.03.2026"),
            ($"/yonetim/siparisler/malzeme-listesi/{orderId}", "05.03.2026"),
            ($"/yonetim/camlar/detay/{ids.Glass}", "05.03.2026"),
            ($"/yonetim/muhasebe/cariler/{customer}", "05.03.2026"),
            ("/yonetim/sunum/goruntule?firma=Deneme", "05/03/2026")
        };
        foreach (var (url, picked) in pages)
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
            Assert.True(html.Contains(Today) || html.Contains(Today.Replace('.', '/')), url + " bugünü basmalı");
            Assert.Contains("name=\"tarih\"", html);
            var chosen = WebUtility.HtmlDecode(await client.GetStringAsync(url + (url.Contains('?') ? "&" : "?") + "tarih=2026-03-05"));
            Assert.True(chosen.Contains(picked), url + " seçilen tarihi basmalı");
        }

        // Kayıtlı tarih seçicide önerilir ama kendisi değişmez
        var quoteHtml = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{ids.Quote}"));
        Assert.Contains("Teklif tarihi (10.01.2026)", quoteHtml);
        Assert.Contains("Fatura tarihi (12.01.2026)", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/faturalar/yazdir/{ids.Invoice}")));
        Assert.Contains("Sevk tarihi (20.01.2026)", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/irsaliye/{noteId}")));
        Assert.Equal(new DateOnly(2026, 1, 12), await app.WithDbAsync(db => db.Invoices.Where(i => i.Id == ids.Invoice).Select(i => i.Date).SingleAsync()));
        // Seçici diğer sorgu değerlerini korur (sunum: firma ve yön)
        Assert.Contains("name=\"firma\" value=\"Deneme\"", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sunum/goruntule?firma=Deneme&tarih=2026-03-05")));

        // Müşterinin açtığı QR bağlantısında seçici yok; başlık kayıtlı teklif tarihini gösterir
        var publicHtml = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/belge/teklif/{new string('a', 22)}"));
        Assert.DoesNotContain("print-date", publicHtml);
        Assert.Contains("10/01/2026", publicHtml);
    }

    // README madde 3: çizim çıktıları (poz listesi, imalat föyü, ölçü listesi, optimizasyon) da bugünü basar, tarih değiştirilebilir.
    [Fact]
    public async Task DrawingPrintouts_DefaultToToday_AndAcceptAPickedDate()
    {
        await using var app = postgres.CreateApp("print_dates_drawings");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var systemId = await app.WithDbAsync(async db => { var s = new ProfileSystem { Name = "Tarih serisi" }; db.ProfileSystems.Add(s); await db.SaveChangesAsync(); return s.Id; });
        await DrawingLists.PostAsync(client, token, "Tarihli çizimler", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 1, system: systemId)));
        var listId = await DrawingLists.ListIdAsync(app, "Tarihli çizimler");
        var designId = await DrawingLists.DesignIdAsync(app, "Salon");
        await app.WithDbAsync(db => db.DrawingLists.Where(l => l.Id == listId).ExecuteUpdateAsync(x => x.SetProperty(l => l.UpdatedUtc, new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc))));

        foreach (var url in new[] { $"/yonetim/cizim/detay/{listId}", $"/yonetim/cizim/imalat/{designId}", $"/yonetim/cizim/olcu-listesi/{listId}", $"/yonetim/cizim/optimizasyon?ids={designId}" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
            Assert.True(html.Contains(Today), url + " bugünü basmalı");
            Assert.Contains("name=\"tarih\"", html);
            var chosen = WebUtility.HtmlDecode(await client.GetStringAsync(url + (url.Contains('?') ? "&" : "?") + "tarih=2026-03-05"));
            Assert.True(chosen.Contains("05.03.2026"), url + " seçilen tarihi basmalı");
        }

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{listId}?tarih=2026-03-05"));
        Assert.Contains("05/03/2026", details); // her sayfada tekrar eden antet
        Assert.Contains("Son değişiklik (01.02.2026)", details);
        Assert.Contains($"ids={designId}&tarih=", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/optimizasyon?ids={designId}&tarih=2026-03-05")).Replace("&amp;", "&")); // seçici diğer sorgu değerlerini korur
    }
}
