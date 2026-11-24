using System.Net;
using KarakusAluminyum.Web.Areas.Admin.Controllers;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README 7.11: keşif randevusu, sahada ölçü ve ölçülerin çizim listesine aktarılması.
[Collection("postgres")]
public sealed class SurveyTests(PostgresFixture postgres)
{
    private static DateOnly Today => KarakusAluminyum.Web.Services.Documents.PrintDates.Today;

    [Theory]
    [InlineData("fixed", ProductCategories.Aluminum)]
    [InlineData("tilt", ProductCategories.Pvc)]
    [InlineData("turn", ProductCategories.Pvc)]
    [InlineData("slide", ProductCategories.Aluminum)]
    [InlineData("door", ProductCategories.Aluminum)]
    [InlineData("fixed", ProductCategories.Railing)]
    [InlineData("fixed", ProductCategories.Shutter)]
    [InlineData("fixed", ProductCategories.GlassBalcony)]
    public void Transfer_BuildsDrawableDesigns(string opening, string category)
    {
        var survey = new SiteSurvey { Number = "KSF-1", CustomerId = 1, Lines = { new SurveyLine { Location = "Salon", Category = category, Opening = opening, WidthMm = 1801, HeightMm = 1400, Quantity = 2 } } };
        var (list, errors) = SurveyController.BuildDrawingList(survey);
        Assert.Empty(errors);
        var design = Assert.Single(list.Items);
        Assert.Equal(("Salon", 1801, 1400, 2), (design.Name, design.WidthMm, design.HeightMm, design.Quantity));
        if (opening == "slide") Assert.Contains("\"sliding\":true", design.LayoutJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transfer_ReportsTooSmallSizes()
    {
        var survey = new SiteSurvey { Number = "KSF-1", Lines = { new SurveyLine { Location = "Banyo", WidthMm = 50, HeightMm = 50, Quantity = 1 } } };
        Assert.Contains(SurveyController.BuildDrawingList(survey).Errors, e => e.StartsWith("Banyo:"));
    }

    [Fact]
    public async Task WebRequest_Survey_Measure_Transfer()
    {
        await using var app = postgres.CreateApp("site_survey");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var messageId = await app.WithDbAsync(async db =>
        {
            var m = new ContactMessage { Name = "Elif Şahin", ContactInfo = "0533 444 55 66", ProjectLocation = "Bornova, İzmir", Service = "PVC Doğrama", Message = "Evin tüm pencereleri değişecek.", PrivacyAccepted = true };
            db.ContactMessages.Add(m); await db.SaveChangesAsync(); return m.Id;
        });

        // Web talebinden: yeni bireysel müşteri önerilir
        var message = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/mesajlar/{messageId}"));
        Assert.Contains($"/yonetim/kesif/ekle?mesaj={messageId}", message);
        var form = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/kesif/ekle?mesaj={messageId}"));
        foreach (var text in new[] { "value=\"Elif Şahin\"", "value=\"0533 444 55 66\"", "value=\"Bornova, İzmir\"", "Evin tüm pencereleri değişecek." }) Assert.Contains(text, form);

        var day = Today.AddDays(2);
        (string, string)[] Fields(params (string, string)[] extra) => new[] { ("ContactMessageId", messageId.ToString()), ("NewCustomerName", "Elif Şahin"), ("NewCustomerPhone", "0533 444 55 66"),
            ("NewCustomerEmail", "elif@example.com"), ("ContactName", "Elif Şahin"), ("ContactPhone", "0533 444 55 66"), ("Address", "Bornova, İzmir"), ("PlannedOn", day.ToString("yyyy-MM-dd")), ("PlannedAt", "10:30"), ("Surveyor", "Ahmet") }.Concat(extra).ToArray();
        var missing = await client.PostFormAsync("/yonetim/kesif/ekle", token, ("NewCustomerName", "Adsız"));
        Assert.Contains("telefon veya e-posta", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
        var created = await client.PostFormAsync("/yonetim/kesif/ekle", token, Fields());
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var survey = await app.WithDbAsync(db => db.SiteSurveys.AsNoTracking().Include(x => x.Customer).SingleAsync(x => x.ContactMessageId == messageId));
        Assert.StartsWith("KSF-", survey.Number);
        Assert.Equal((CustomerKind.Individual, "Web sitesi", "elif@example.com"), (survey.Customer!.Kind, survey.Customer.Source!, survey.Customer.Email!));
        Assert.Equal((day, new TimeOnly(10, 30), SurveyStatus.Planned), (survey.PlannedOn!.Value, survey.PlannedAt!.Value, survey.Status));
        Assert.True(await app.WithDbAsync(db => db.ContactMessages.Where(x => x.Id == messageId).Select(x => x.IsRead).SingleAsync()));
        // Randevu müşteriye bildirilir
        var job = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().SingleAsync(x => x.SiteSurveyId == survey.Id && x.Channel == NotificationChannel.CustomerEmail));
        Assert.Equal((CustomerEvents.SurveyVisit, NotificationStatus.Pending, day), (job.Event!, job.Status, job.EventDate!.Value));

        // Aynı telefondan ikinci talep mevcut müşteriyi bulur
        var second = await app.WithDbAsync(async db =>
        {
            var m = new ContactMessage { Name = "Elif", ContactInfo = "+90 533 444 5566", Message = "Balkon da eklensin.", PrivacyAccepted = true };
            db.ContactMessages.Add(m); await db.SaveChangesAsync(); return m.Id;
        });
        Assert.Contains($"<option selected=\"selected\" value=\"{survey.CustomerId}\">", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/kesif/ekle?mesaj={second}")));

        // Liste ve menü
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/kesif"));
        Assert.Contains(survey.Number, list);
        Assert.Contains("href=\"/yonetim/kesif\"", list);

        // Sahada ölçüler: boş satır atlanır
        var saved = await client.PostFormAsync($"/yonetim/kesif/olculer/{survey.Id}", token,
            ("lines[0].Location", "Salon"), ("lines[0].Category", "pvc"), ("lines[0].Opening", "slide"), ("lines[0].WidthMm", "2400"), ("lines[0].HeightMm", "1500"), ("lines[0].Quantity", "1"), ("lines[0].Note", "Antrasit"),
            ("lines[1].Location", ""), ("lines[1].Category", "pvc"), ("lines[1].Opening", "fixed"), ("lines[1].WidthMm", ""), ("lines[1].HeightMm", ""), ("lines[1].Quantity", "1"),
            ("lines[2].Location", "Balkon"), ("lines[2].Category", "railing"), ("lines[2].Opening", "turn"), ("lines[2].WidthMm", "4200"), ("lines[2].HeightMm", "1100"), ("lines[2].Quantity", "1"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var lines = await app.WithDbAsync(db => db.SurveyLines.AsNoTracking().Where(x => x.SiteSurveyId == survey.Id).OrderBy(x => x.SortOrder).ToListAsync());
        Assert.Equal(2, lines.Count);
        Assert.Equal(("Balkon", "fixed"), (lines[1].Location, lines[1].Opening)); // korkulukta açılım yok
        var print = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/kesif/yazdir/{survey.Id}"));
        foreach (var text in new[] { "KEŞİF / ÖLÇÜ FORMU", survey.Number, "Sürme (2 kanat)", "2400", "Antrasit", "Ahmet" }) Assert.Contains(text, print);
        Assert.Contains("KEŞİF / ÖLÇÜ FORMU", await client.GetStringAsync("/yonetim/kesif/bos-form"));

        // Çizim listesine aktar
        var transfer = await client.PostFormAsync($"/yonetim/kesif/aktar/{survey.Id}", token);
        Assert.Equal(HttpStatusCode.Redirect, transfer.StatusCode);
        var done = await app.WithDbAsync(db => db.SiteSurveys.AsNoTracking().SingleAsync(x => x.Id == survey.Id));
        Assert.Equal(SurveyStatus.Done, done.Status);
        Assert.EndsWith($"/yonetim/cizim/detay/{done.DrawingListId}", transfer.Headers.Location!.OriginalString);
        var designs = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == done.DrawingListId).OrderBy(x => x.SortOrder).ToListAsync());
        Assert.Equal(new[] { "Salon", "Balkon" }, designs.Select(x => x.Name));
        Assert.Equal((ProductCategories.Pvc, "Antrasit", survey.CustomerId), (designs[0].Category, designs[0].Notes!, designs[0].CustomerId!.Value));
        Assert.Empty(WindowDrawings.Validate(designs[0].Category, designs[0].WidthMm, designs[0].HeightMm, designs[0].Shape, 0, 1, designs[0].LayoutJson, designs[0].ParamsJson));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/detay/{done.DrawingListId}")).StatusCode);
        // Ölçü alındı: sıradaki randevu bildirimi gönderilmez; ölçüler kilitlenir; teklif bağlantısı çıkar
        Assert.Equal(NotificationStatus.Skipped, await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Id == job.Id).Select(x => x.Status).SingleAsync()));
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/kesif/detay/{survey.Id}"));
        Assert.Contains($"/yonetim/crm/teklifler/ekle?liste={done.DrawingListId}", details);
        await client.PostFormAsync($"/yonetim/kesif/olculer/{survey.Id}", token, ("lines[0].Location", "Yeni"), ("lines[0].WidthMm", "1000"), ("lines[0].HeightMm", "1000"));
        Assert.Equal(2, await app.WithDbAsync(db => db.SurveyLines.CountAsync(x => x.SiteSurveyId == survey.Id)));
        await client.PostFormAsync($"/yonetim/kesif/sil/{survey.Id}", token); // aktarılan keşif silinmez
        Assert.True(await app.WithDbAsync(db => db.SiteSurveys.AnyAsync(x => x.Id == survey.Id)));
    }

    [Fact]
    public async Task ExistingDatabase_GetsSurveyTables()
    {
        await using (var first = postgres.CreateApp("survey_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE \"NotificationJobs\" DROP COLUMN \"SiteSurveyId\"; DROP TABLE \"SurveyLines\"; DROP TABLE \"SiteSurveys\";"));
        }
        await using var second = postgres.CreateApp("survey_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/kesif")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/bildirimler")).StatusCode);
    }
}
