using System.Net;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README 7.15: teklif kayıp nedeni, müşteri kaynağına göre dönüşüm ve talep → sipariş hunisi.
[Collection("postgres")]
public sealed class ConversionReportTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(null, "Belirtilmedi")]
    [InlineData("  instagram ", "Instagram")]
    [InlineData("Komşu tavsiyesi", "Komşu tavsiyesi")]
    public void Source_IsNormalized(string? source, string expected) => Assert.Equal(expected, CustomerSources.Normalize(source));

    [Fact]
    public async Task LostReason_Required_AndReportGroups()
    {
        await using var app = postgres.CreateApp("conversion_report");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var insta = await QuoteDocumentTests.NewCustomerAsync(app, "Instagram Müşterisi");
        var google = await QuoteDocumentTests.NewCustomerAsync(app, "Google Müşterisi");
        await app.WithDbAsync(async db =>
        {
            await db.Customers.Where(c => c.Id == insta).ExecuteUpdateAsync(x => x.SetProperty(c => c.Source, "instagram"));
            await db.Customers.Where(c => c.Id == google).ExecuteUpdateAsync(x => x.SetProperty(c => c.Source, "Google"));
            db.ContactMessages.AddRange(Enumerable.Range(1, 4).Select(i => new ContactMessage { Name = "Talep " + i, ContactInfo = "05320000000", Message = "Teklif", PrivacyAccepted = true }));
            return await db.SaveChangesAsync();
        });

        async Task<int> QuoteAsync(int customer, string title, decimal amount)
        {
            await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, QuoteDocumentTests.BlocksJson(("", new[] { ("Kalem", amount) })));
            var id = await app.WithDbAsync(db => db.Quotes.Where(q => q.Title == title).Select(q => q.Id).SingleAsync());
            await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{id}", token, ("status", ((int)QuoteStatus.Sent).ToString()));
            return id;
        }
        var won = await QuoteAsync(insta, "Kazanılan", 10000);
        var lost = await QuoteAsync(insta, "Kaybedilen", 8000);
        var lost2 = await QuoteAsync(google, "Kaybedilen 2", 5000);
        await QuoteAsync(google, "Bekleyen", 3000);
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{won}", token, ("status", ((int)QuoteStatus.Accepted).ToString()));

        // Reddedilen teklifte neden zorunlu
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{lost}", token, ("status", ((int)QuoteStatus.Rejected).ToString()));
        Assert.Equal(QuoteStatus.Sent, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == lost).Select(q => q.Status).SingleAsync()));
        Assert.Contains("kayıp nedenini seçin", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{lost}")));
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{lost}", token, ("status", ((int)QuoteStatus.Rejected).ToString()), ("lostReason", "Fiyat yüksek"), ("lostCompetitor", "Rakip Alüminyum"), ("lostNote", "%10 ucuz"));
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{lost2}", token, ("status", ((int)QuoteStatus.Rejected).ToString()), ("lostReason", "Fiyat yüksek"), ("lostCompetitor", "rakip alüminyum"));
        var saved = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(q => q.Id == lost));
        Assert.Equal((QuoteStatus.Rejected, "Fiyat yüksek", "Rakip Alüminyum", "%10 ucuz"), (saved.Status, saved.LostReason!, saved.LostCompetitor!, saved.LostNote!));
        Assert.Contains("Kayıp nedeni:</strong> Fiyat yüksek · rakip: Rakip Alüminyum", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{lost}")));

        var report = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/raporlar?aralik=30"));
        Assert.Contains("Satış dönüşümü", report);
        Assert.Contains("<td>Instagram</td><td>2</td><td>1</td><td>%50</td>", report.Replace("\n", "").Replace("  ", ""));
        Assert.Contains("<td>Google</td><td>2</td><td>0</td>", report.Replace("\n", "").Replace("  ", ""));
        Assert.Contains("2 teklif · 13.000,00 TL", report); // iki fiyat kaybı
        Assert.Contains("Rakip Alüminyum", report); // büyük/küçük harf farkı tek satır
        var csv = await client.GetStringAsync("/yonetim/raporlar/csv?bolum=donusum&aralik=30");
        Assert.Contains("Kayıp nedenleri", csv);
        Assert.Contains("Fiyat yüksek", csv);

        // Müşteri kartında kaynak önerileri
        Assert.Contains("datalist id=\"customer-sources\"", await client.GetStringAsync($"/yonetim/crm/musteriler/duzenle/{insta}"));
    }

    [Fact]
    public async Task ExistingDatabase_GetsLostReasonColumns()
    {
        await using (var first = postgres.CreateApp("conversion_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Quotes\" DROP COLUMN \"LostReason\", DROP COLUMN \"LostCompetitor\", DROP COLUMN \"LostNote\""));
        }
        await using var second = postgres.CreateApp("conversion_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/raporlar")).StatusCode);
    }
}
