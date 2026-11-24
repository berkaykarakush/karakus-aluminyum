using System.Net;
using KarakusAluminyum.Web.Areas.Admin.Controllers;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README madde 8: iş listelerindeki eksik ve kusurlar (cam kırık, kol eksik) kaydı ve takibi.
[Collection("postgres")]
public sealed class PunchTests(PostgresFixture postgres)
{
    private static DateOnly Today => KarakusAluminyum.Web.Services.Documents.PrintDates.Today;

    [Theory]
    [InlineData(OrderStatus.InProduction, "Üretim")]
    [InlineData(OrderStatus.Warehouse, "Sevkiyat / yükleme")]
    [InlineData(OrderStatus.Shipped, "Montaj")]
    [InlineData(OrderStatus.Delivered, "Teslim")]
    public void DefaultStage_FollowsOrderStatus(OrderStatus status, string expected) => Assert.Equal(expected, PunchController.DefaultStage(status));

    [Fact]
    public void ChangeStatus_SetsAndClearsResolvedDate()
    {
        var item = new PunchItem { Description = "Kol eksik", ReportedOn = Today };
        PunchController.ChangeStatus(item, PunchStatus.Resolved, "  Kol takıldı ", Today);
        Assert.Equal((Today, "Kol takıldı"), (item.ResolvedOn!.Value, item.Resolution));
        Assert.False(PunchController.IsLate(item, Today.AddDays(10)));
        PunchController.ChangeStatus(item, PunchStatus.Open, null, Today);
        Assert.Null(item.ResolvedOn);
        Assert.Equal("Kol takıldı", item.Resolution); // yeniden açınca önceki işlem notu kalır
        item.DueOn = Today.AddDays(-1);
        Assert.True(PunchController.IsLate(item, Today));
    }

    [Fact]
    public async Task Punch_RecordTrackPrint_AndHandover()
    {
        await using var app = postgres.CreateApp("punch_items");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await DrawingLists.PostAsync(client, token, "Eksik listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 2), DrawingLists.Row("Balkon kapısı", 900, 2200, quantity: 1)));
        var listId = await DrawingLists.ListIdAsync(app, "Eksik listesi");
        var designs = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).Select(d => d.Id).ToArrayAsync());
        var otherList = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId != listId).Select(d => (int?)d.Id).FirstOrDefaultAsync());
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Eksikli iş"), ("DrawingListId", listId.ToString()), ("OrderDate", "2026-10-01"), ("InstallAddress", "Karşıyaka İzmir"));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Eksikli iş").Select(o => o.Id).SingleAsync());

        // Sipariş sayfasında hızlı ekleme formu ve menü bağlantısı
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("id=\"eksikler\"", details);
        Assert.Contains("Poz 2: Balkon kapısı", details);
        Assert.Contains("href=\"/yonetim/eksikler\"", details);

        // Hızlı ekleme: cam kırığı 1. poza, camcı sorumlu
        var added = await client.PostFormAsync($"/yonetim/eksikler/ekle/{orderId}", token, ("WindowDesignId", designs[0].ToString()), ("Kind", "Cam kırık / çatlak"),
            ("Description", "Sabit cam köşeden çatlak"), ("Quantity", "1"), ("Responsible", "Camcı"), ("Stage", "Montaj"), ("ReportedOn", Today.ToString("yyyy-MM-dd")));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.EndsWith($"/yonetim/siparisler/detay/{orderId}#eksikler", added.Headers.Location!.ToString());
        // Pozsuz kayıt: kol eksik, geçersiz tür "Diğer" olur
        await client.PostFormAsync($"/yonetim/eksikler/ekle/{orderId}", token, ("Location", "Mutfak penceresi"), ("Kind", "uydurma"), ("Description", "Pencere kolu takılmamış"),
            ("Quantity", "2"), ("Responsible", "Montaj ekibi"), ("Stage", "Montaj"), ("ReportedOn", Today.ToString("yyyy-MM-dd")), ("DueOn", Today.AddDays(-1).ToString("yyyy-MM-dd")));
        var items = await app.WithDbAsync(db => db.PunchItems.AsNoTracking().Where(x => x.ProductionOrderId == orderId).OrderBy(x => x.Id).ToListAsync());
        Assert.Equal(2, items.Count);
        Assert.Equal((designs[0], "Camcı", PunchStatus.Open, AppFactory.AdminEmail), (items[0].WindowDesignId!.Value, items[0].Responsible, items[0].Status, items[0].ReportedBy!));
        Assert.Equal(("Diğer", "Mutfak penceresi", 2), (items[1].Kind, items[1].Location!, items[1].Quantity));
        Assert.Contains(await app.WithDbAsync(db => db.OrderEvents.Where(e => e.ProductionOrderId == orderId).Select(e => e.Text).ToListAsync()), t => t.StartsWith("Eksik kaydedildi: Cam kırık / çatlak"));

        // Başka listenin pozu reddedilir
        if (otherList is int foreign)
        {
            var bad = await client.PostFormAsync($"/yonetim/eksikler/ekle/{orderId}", token, ("WindowDesignId", foreign.ToString()), ("Description", "x"), ("Quantity", "1"), ("ReportedOn", Today.ToString("yyyy-MM-dd")));
            Assert.Contains("bu siparişin çizim listesinde yok", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        }

        // Genel liste: açıklar, geciken işaretli; sorumluya göre süzülür
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/eksikler"));
        foreach (var text in new[] { "Sabit cam köşeden çatlak", "Pencere kolu takılmamış", "Poz 1: Salon (1200 × 1400 mm)", "gecikti" }) Assert.Contains(text, list);
        var glazier = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/eksikler?sorumlu=" + Uri.EscapeDataString("Camcı")));
        Assert.Contains("Sabit cam köşeden çatlak", glazier);
        Assert.DoesNotContain("Pencere kolu takılmamış", glazier);

        // Camcıya verilecek liste
        var print = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/eksikler/yazdir/{orderId}?sorumlu=" + Uri.EscapeDataString("Camcı")));
        foreach (var text in new[] { "EKSİK LİSTESİ – CAMCI", "Sabit cam köşeden çatlak", "Karşıyaka İzmir", "name=\"tarih\"" }) Assert.Contains(text, print);
        Assert.DoesNotContain("Pencere kolu takılmamış", print);

        // Teslim tutanağına açık eksikler yazılır
        var handover = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/teslim-tutanagi/{orderId}"));
        Assert.Contains("Cam kırık / çatlak: Sabit cam köşeden çatlak (1 adet)", handover);

        // Bekleniyor → giderildi; giderilen teslim tutanağından ve açık listeden düşer
        await client.PostFormAsync($"/yonetim/eksikler/durum/{items[0].Id}", token, ("status", "Waiting"));
        Assert.Equal(PunchStatus.Waiting, await app.WithDbAsync(db => db.PunchItems.Where(x => x.Id == items[0].Id).Select(x => x.Status).SingleAsync()));
        await client.PostFormAsync($"/yonetim/eksikler/durum/{items[0].Id}", token, ("status", "Resolved"), ("resolution", "Cam değiştirildi"));
        var resolved = await app.WithDbAsync(db => db.PunchItems.AsNoTracking().SingleAsync(x => x.Id == items[0].Id));
        Assert.Equal((PunchStatus.Resolved, Today, "Cam değiştirildi"), (resolved.Status, resolved.ResolvedOn!.Value, resolved.Resolution!));
        Assert.DoesNotContain("Sabit cam köşeden çatlak", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/teslim-tutanagi/{orderId}")));
        Assert.DoesNotContain("Sabit cam köşeden çatlak", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/eksikler")));
        Assert.Contains("Cam değiştirildi", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/eksikler/yazdir/{orderId}?tumu=true")));

        // Düzenleme: tarih sırası denetlenir
        var editForm = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/eksikler/duzenle/{items[1].Id}"));
        Assert.Contains("Pencere kolu takılmamış", editForm);
        var wrongDate = await client.PostFormAsync($"/yonetim/eksikler/duzenle/{items[1].Id}", token, ("Kind", "Kol / kilit eksik"), ("Description", "Pencere kolu takılmamış"), ("Quantity", "2"),
            ("Status", "Resolved"), ("ReportedOn", Today.ToString("yyyy-MM-dd")), ("ResolvedOn", Today.AddDays(-3).ToString("yyyy-MM-dd")));
        Assert.Contains("bildirim tarihinden önce olamaz", WebUtility.HtmlDecode(await wrongDate.Content.ReadAsStringAsync()));
        await client.PostFormAsync($"/yonetim/eksikler/duzenle/{items[1].Id}", token, ("Kind", "Kol / kilit eksik"), ("Description", "Pencere kolu takılmamış"), ("Quantity", "2"),
            ("Responsible", "Montaj ekibi"), ("Status", "Open"), ("ReportedOn", Today.ToString("yyyy-MM-dd")));
        Assert.Equal("Kol / kilit eksik", await app.WithDbAsync(db => db.PunchItems.Where(x => x.Id == items[1].Id).Select(x => x.Kind).SingleAsync()));

        // Poz silinince kayıt kalır; silme ve sipariş silme ile birlikte gider
        await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == designs[0]).ExecuteDeleteAsync());
        Assert.Null(await app.WithDbAsync(db => db.PunchItems.Where(x => x.Id == items[0].Id).Select(x => x.WindowDesignId).SingleAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/eksikler/sil/{items[0].Id}", token)).StatusCode);
        Assert.Equal(1, await app.WithDbAsync(db => db.PunchItems.CountAsync(x => x.ProductionOrderId == orderId)));
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).ExecuteDeleteAsync());
        Assert.Equal(0, await app.WithDbAsync(db => db.PunchItems.CountAsync(x => x.ProductionOrderId == orderId)));
    }

    [Fact]
    public async Task ExistingDatabase_GetsPunchTable()
    {
        await using (var first = postgres.CreateApp("punch_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"PunchItems\""));
        }
        await using var second = postgres.CreateApp("punch_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/eksikler")).StatusCode);
    }
}
