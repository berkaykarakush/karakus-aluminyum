using System.Net;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Belgelere otomatik yazılan kişiler: keşifte ölçüyü alan, teklif ve sözleşmede imza yetkilisi.
[Collection("postgres")]
public sealed class DocumentPeopleTests(PostgresFixture postgres)
{
    [Fact]
    public void Join_WritesEachNameOnce()
    {
        Assert.Equal("Ahmet Yılmaz, Mehmet Kaya", DocumentPeople.Join(new[] { "Ahmet Yılmaz, Mehmet Kaya", "ahmet yılmaz", " " }));
        Assert.Null(DocumentPeople.Join(Array.Empty<string>()));
    }

    [Fact]
    public async Task UserName_FillsSurveyorAndSignatory_DocumentsShowSurveyors()
    {
        await using var app = postgres.CreateApp("document_people");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var adminId = await app.WithDbAsync(db => db.Users.Where(x => x.Email == AppFactory.AdminEmail).Select(x => x.Id).SingleAsync());

        // Adı yazılmamış kullanıcıda şablondaki yetkili kalır.
        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sozlesmeler/ekle"));
        Assert.Contains("name=\"Signatory\"", before);

        var saved = await client.PostFormAsync($"/yonetim/kullanicilar/bilgi/{adminId}", token, ("fullName", " Ayşe Karakuş "), ("jobTitle", "Satış sorumlusu"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("value=\"Ayşe Karakuş\"", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/kullanicilar")));

        // Keşif formu ölçüyü alanı giriş yapan kullanıcıyla açar.
        var surveyForm = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/kesif/ekle"));
        Assert.Contains("value=\"Ayşe Karakuş\"", surveyForm);
        Assert.Contains("<option value=\"Ayşe Karakuş\">", surveyForm);

        // Keşiften aktarılmış çizim listesi ve ondan hazırlanan teklif
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var listId = await app.WithDbAsync(async db =>
        {
            var list = new DrawingList { Name = "Ölçü listesi", CustomerId = customer, Items = { new WindowDesign { Name = "Salon", CustomerId = customer, WidthMm = 1500, HeightMm = 1400, Quantity = 1 } } };
            db.DrawingLists.Add(list);
            db.SiteSurveys.Add(new SiteSurvey { Number = "KSF-T1", CustomerId = customer, Surveyor = "Mehmet Kaya, Ayşe Karakuş", Status = SurveyStatus.Done, DrawingList = list });
            await db.SaveChangesAsync();
            return list.Id;
        });
        var measurements = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/olcu-listesi/{listId}"));
        Assert.Contains("Ölçüyü alan: Mehmet Kaya, Ayşe Karakuş", measurements);

        var editor = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/ekle?liste={listId}"));
        Assert.Matches("<input[^>]*name=\"Signatory\"[^>]*value=\"Ayşe Karakuş\"", editor);
        var title = "Ölçülü teklif " + Guid.NewGuid().ToString("N")[..8];
        await client.PostFormAsync("/yonetim/crm/teklifler/ekle", token, ("CustomerId", customer.ToString()), ("Title", title), ("DocumentDate", "2026-11-28"), ("VatMode", "Included"), ("TaxRate", "20"),
            ("DiscountRate", "0"), ("DrawingListId", listId.ToString()), ("Signatory", "Ayşe Karakuş"), ("SignatoryTitle", "Satış sorumlusu"), ("DocumentJson", QuoteDocumentTests.BlocksJson(("", new[] { ("PVC pencere", 10000m) }))));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(x => x.Title == title));
        Assert.Equal(("Ayşe Karakuş", "Satış sorumlusu"), (quote.Signatory!, quote.SignatoryTitle!));
        var print = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{quote.Id}"));
        Assert.Contains("Ölçüyü alan: Mehmet Kaya, Ayşe Karakuş", print);
        Assert.Contains("Ayşe Karakuş (Satış sorumlusu)", print);

        // Kabul edilen tekliften sözleşme: imza yetkilisi sözleşmeyi açan kullanıcı, ölçüyü alanlar sözleşmede de görünür.
        await app.WithDbAsync(db => db.Quotes.Where(x => x.Id == quote.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, QuoteStatus.Accepted)));
        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quote.Id}", token);
        var contract = await app.WithDbAsync(db => db.Contracts.AsNoTracking().SingleAsync(x => x.QuoteId == quote.Id));
        Assert.Equal(("Ayşe Karakuş", "Satış sorumlusu"), (contract.Signatory!, contract.SignatoryTitle!));
        var contractPrint = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{contract.Id}"));
        Assert.Contains("Ayşe Karakuş (Satış sorumlusu)", contractPrint);
        Assert.Contains("Ölçüyü alan: Mehmet Kaya, Ayşe Karakuş", contractPrint);
    }
}
