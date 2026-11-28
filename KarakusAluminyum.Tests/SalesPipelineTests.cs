using System.Net;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Prospecting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Müşteri bulma: öncelik puanı, otomatik takip günü, hazır mesaj, komşu işler.
public sealed class SalesPipelineRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 28);

    [Fact]
    public void Score_PutsWarmAndLateFirstThenBigFirms()
    {
        var cold = new Prospect { Name = "Yeni", Phone = "0532 000 00 00" };
        var big = new Prospect { Name = "Büyük", Phone = "1", RatingCount = 120, Rating = 4.7, Email = "a@b.c", Website = "https://b.c/" };
        var quoted = new Prospect { Name = "Teklif", Phone = "1", Stage = ProspectStage.Quoted, NextFollowUp = Today.AddDays(-8) };
        var scores = new[] { cold, big, quoted }.Select(p => ProspectPriority.Score(p, Today).Total).ToArray();
        Assert.True(scores[2] > scores[1] && scores[1] > scores[0], string.Join(",", scores));
        Assert.Contains("Takip 8 gün gecikti", ProspectPriority.Score(quoted, Today).Reasons);
        Assert.True(ProspectPriority.Score(cold, Today, sectorWinRate: 0.25).Total > ProspectPriority.Score(cold, Today).Total);
    }

    [Fact]
    public void Callable_OnlyOpenReachableAndDue()
    {
        Assert.True(ProspectPriority.IsCallable(new Prospect { Phone = "1" }, Today)); // hiç aranmamış
        Assert.False(ProspectPriority.IsCallable(new Prospect { Phone = "1", Stage = ProspectStage.Contacted }, Today)); // takip günü yok
        Assert.False(ProspectPriority.IsCallable(new Prospect { Phone = "1", NextFollowUp = Today.AddDays(1) }, Today));
        Assert.False(ProspectPriority.IsCallable(new Prospect { Phone = "1", DoNotContact = true }, Today));
        Assert.False(ProspectPriority.IsCallable(new Prospect { Phone = "1", Stage = ProspectStage.Won, NextFollowUp = Today }, Today));
        Assert.False(ProspectPriority.IsCallable(new Prospect { NextFollowUp = Today }, Today)); // telefonsuz
    }

    [Theory]
    [InlineData(ProspectStage.New, ProspectActivityKind.NoAnswer, ProspectStage.New, 2)]
    [InlineData(ProspectStage.New, ProspectActivityKind.Call, ProspectStage.Contacted, 3)]
    [InlineData(ProspectStage.New, ProspectActivityKind.Visit, ProspectStage.Meeting, 5)]
    [InlineData(ProspectStage.Quoted, ProspectActivityKind.Call, ProspectStage.Quoted, 7)] // aşama geri gitmez
    [InlineData(ProspectStage.Meeting, ProspectActivityKind.Note, ProspectStage.Meeting, 5)]
    public void FollowUp_IsSuggestedByStage(ProspectStage stage, ProspectActivityKind kind, ProspectStage expected, int days)
    {
        var next = ProspectPriority.AdvanceStage(stage, kind);
        Assert.Equal(expected, next);
        Assert.Equal(Today.AddDays(days), ProspectPriority.SuggestFollowUp(next, kind, Today));
        Assert.Null(ProspectPriority.SuggestFollowUp(ProspectStage.Lost, kind, Today));
    }

    [Fact]
    public void Message_FillsVariables()
    {
        var p = new Prospect { Name = "Ege Yapı", ContactPerson = "Ahmet Bey", District = "Bornova" };
        var text = ProspectPriority.Message(null, p, "Ayşe Karakuş");
        Assert.StartsWith("Merhaba Ahmet Bey, Karakuş Alüminyum'dan yazıyoruz. Bornova bölgesindeki", text);
        Assert.EndsWith("Ayşe Karakuş, Karakuş Alüminyum", text);
        Assert.Equal("Merhaba, Ege Yapı İzmir", ProspectPriority.Message("Merhaba{{yetkili_hitap}}, {{firma}} {{ilce}}", new Prospect { Name = "Ege Yapı" }, null));
    }

    [Theory]
    [InlineData("Kazımdirik Mah. 372/8 Sok. Gül Sitesi B Blok No:5 Bornova", "Kazımdirik Mahallesi, Gül Sitesi A Blok", "Bornova", NeighborLevel.Building)]
    [InlineData("Kazımdirik Mah. 372/8 Sok. No:5", "372/8 Sokak No:12 Kazımdirik Mahallesi", null, NeighborLevel.Street)]
    [InlineData("Kazımdirik Mah. 372/8 Sok. No:5", "Kazımdirik Mh. 300 Sk. No:1", null, NeighborLevel.Neighbourhood)]
    public void Neighbors_MatchByBuildingStreetAndNeighbourhood(string a, string b, string? district, NeighborLevel level)
        => Assert.Equal(level, NeighborWork.Compare(NeighborWork.Parse(a, district), NeighborWork.Parse(b, district)));

    [Fact]
    public void Neighbors_SameSiteNameInAnotherDistrict_DoesNotMatch()
    {
        Assert.Null(NeighborWork.Compare(NeighborWork.Parse("Gül Sitesi", "Bornova"), NeighborWork.Parse("Gül Sitesi", "Buca")));
        Assert.Null(NeighborWork.Compare(NeighborWork.Parse("Lale Sok. Gül Sitesi Buca"), NeighborWork.Parse("Gül Sitesi Bornova")));
        Assert.Null(NeighborWork.Compare(NeighborWork.Parse("Bornova İzmir"), NeighborWork.Parse("Bornova İzmir")));
    }

    [Theory]
    [InlineData("Fiyat yüksek", 30)]
    [InlineData("Vazgeçti / ertelendi", 60)]
    [InlineData("Ulaşılamadı", 7)]
    [InlineData("Rakip tercih edildi", null)]
    [InlineData("Ürün / sistem uymadı", null)]
    public void WinBack_DependsOnLossReason(string reason, int? days) => Assert.Equal(days, QuoteLoss.WinBackDays(reason));
}

[Collection("postgres")]
public sealed class SalesPipelineTests(PostgresFixture postgres)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [Fact]
    public async Task CallList_RanksRecordsAndOpensManualMessages()
    {
        await using var app = postgres.CreateApp("call_list");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var ids = await app.WithDbAsync(async db =>
        {
            var sector = await db.ProspectSectors.OrderBy(x => x.Id).FirstAsync();
            sector.MessageTemplate = "Merhaba{{yetkili_hitap}}, {{firma}} için yazıyoruz.";
            var cold = new Prospect { Name = "Soğuk Firma", Phone = "0232 111 11 11", SectorId = sector.Id };
            var warm = new Prospect { Name = "Sıcak Firma", Phone = "0532 222 22 22", Stage = ProspectStage.Quoted, NextFollowUp = Today.AddDays(-3), ContactPerson = "Ahmet Bey", SectorId = sector.Id };
            var later = new Prospect { Name = "Sonra Firma", Phone = "0532 333 33 33", Stage = ProspectStage.Contacted, NextFollowUp = Today.AddDays(5) };
            var refused = new Prospect { Name = "İstemeyen Firma", Phone = "0532 444 44 44", DoNotContact = true };
            db.Prospects.AddRange(cold, warm, later, refused);
            await db.SaveChangesAsync();
            return (cold.Id, warm.Id);
        });

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/arama-listesi"));
        Assert.True(page.IndexOf("Sıcak Firma", StringComparison.Ordinal) < page.IndexOf("Soğuk Firma", StringComparison.Ordinal));
        Assert.DoesNotContain("Sonra Firma", page);
        Assert.DoesNotContain("İstemeyen Firma", page);
        Assert.Contains("href=\"/yonetim/crm/arama-listesi\"", page); // menüde

        // Ulaşılamadı: not gerekmez, aşama değişmez, 2 gün sonra yeniden gelir; arama listesine dönülür.
        var noAnswer = await client.PostFormAsync($"/yonetim/crm/firmalar/{ids.Item1}/gorusme", token, ("kind", "NoAnswer"), ("geri", "/yonetim/crm/arama-listesi"));
        Assert.Equal("/yonetim/crm/arama-listesi", noAnswer.Headers.Location!.OriginalString);
        var cold = await app.WithDbAsync(db => db.Prospects.Include(x => x.Activities).AsNoTracking().SingleAsync(x => x.Id == ids.Item1));
        Assert.Equal((ProspectStage.New, Today.AddDays(2), ProspectActivityKind.NoAnswer), (cold.Stage, cold.NextFollowUp!.Value, cold.Activities.Single().Kind));
        var afterNoAnswer = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/arama-listesi"));
        Assert.Contains("Soğuk Firma: görüşme kaydedildi.", afterNoAnswer); // kayıt bildirimi
        Assert.DoesNotContain("<strong>Soğuk Firma</strong>", afterNoAnswer); // listeden düştü
        // Dışarıya yönlendirme yapılmaz
        var outside = await client.PostFormAsync($"/yonetim/crm/firmalar/{ids.Item1}/gorusme", token, ("kind", "NoAnswer"), ("geri", "https://kotu.example"));
        Assert.Equal($"/yonetim/crm/firmalar/{ids.Item1}", outside.Headers.Location!.OriginalString);

        // Hazır mesaj: sektör metniyle wa.me açılır (otomatik gönderim yok), görüşme kaydı düşer, takip 7 gün sonra.
        var message = await client.PostFormAsync($"/yonetim/crm/firmalar/{ids.Item2}/mesaj", token, ("kanal", "whatsapp"));
        Assert.Equal(HttpStatusCode.Redirect, message.StatusCode);
        Assert.Equal("https://wa.me/905322222222?text=" + Uri.EscapeDataString("Merhaba Ahmet Bey, Sıcak Firma için yazıyoruz."), message.Headers.Location!.OriginalString);
        var warm = await app.WithDbAsync(db => db.Prospects.Include(x => x.Activities).AsNoTracking().SingleAsync(x => x.Id == ids.Item2));
        Assert.Equal((ProspectActivityKind.WhatsApp, Today.AddDays(7)), (warm.Activities.Single().Kind, warm.NextFollowUp!.Value));
        Assert.Equal(0, await app.WithDbAsync(db => db.NotificationJobs.CountAsync()));
        // Sabit hatta WhatsApp açılmaz
        var landline = await client.PostFormAsync($"/yonetim/crm/firmalar/{ids.Item1}/mesaj", token, ("kanal", "whatsapp"));
        Assert.Equal($"/yonetim/crm/firmalar/{ids.Item1}", landline.Headers.Location!.OriginalString);

        // Sonuçlanan firmada takip günü kalmaz
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/crm/firmalar/{ids.Item2}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/crm/firmalar/verim")).StatusCode);
        var yield = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/firmalar/verim"));
        Assert.Contains("Kazanma oranı", yield);
    }

    [Fact]
    public async Task Yield_CountsFunnelPerSector()
    {
        await using var app = postgres.CreateApp("prospect_yield");
        app.NewClient();
        var sectorId = await app.WithDbAsync(async db =>
        {
            var sector = await db.ProspectSectors.OrderBy(x => x.Id).FirstAsync();
            var customer = new Customer { Name = "Kazanılan Yapı" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            db.Quotes.Add(new Quote { QuoteNumber = "Q-Y1", CustomerId = customer.Id, Title = "İş", Status = QuoteStatus.Accepted, GrandTotal = 50000 });
            var contacted = new Prospect { Name = "Arandı", SectorId = sector.Id, Phone = "1", Stage = ProspectStage.Contacted };
            contacted.Activities.Add(new ProspectActivity { Kind = ProspectActivityKind.Call, Text = "x" });
            db.Prospects.AddRange(new Prospect { Name = "Yeni", SectorId = sector.Id }, contacted,
                new Prospect { Name = "Kazanılan", SectorId = sector.Id, Phone = "2", CustomerId = customer.Id, Stage = ProspectStage.Quoted });
            db.ProspectScans.Add(new ProspectScan { SectorId = sector.Id, Status = ProspectScanStatus.Done, RequestCount = 40 });
            await db.SaveChangesAsync();
            return sector.Id;
        });
        var report = await app.WithDbAsync(db => ProspectYield.BuildAsync(db));
        var row = report.Sectors.Single(x => x.SectorId == sectorId);
        Assert.Equal((3, 2, 2, 1, 1, 50000m, 40), (row.Firms, row.WithPhone, row.Contacted, row.Quoted, row.Won, row.Revenue, row.GoogleRequests));
        Assert.Equal(0.5, row.WinRate);
        Assert.Equal(40, row.RequestsPerWon);
    }

    [Fact]
    public async Task RejectedQuote_ComesBackForWinBack()
    {
        await using var app = postgres.CreateApp("win_back");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var id = await app.WithDbAsync(async db =>
        {
            var q = new Quote { QuoteNumber = "Q-WB1", CustomerId = customer, Title = "Salon pencereleri", Status = QuoteStatus.Sent, GrandTotal = 42000 };
            db.Quotes.Add(q); await db.SaveChangesAsync(); return q.Id;
        });
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{id}", token, ("status", ((int)QuoteStatus.Rejected).ToString()), ("lostReason", "Fiyat yüksek"));
        Assert.Equal(Today.AddDays(30), await app.WithDbAsync(db => db.Quotes.Where(x => x.Id == id).Select(x => x.WinBackOn).SingleAsync()));
        Assert.DoesNotContain("Q-WB1", await client.GetStringAsync("/yonetim/crm/arama-listesi"));

        await app.WithDbAsync(db => db.Quotes.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.WinBackOn, Today)));
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/arama-listesi"));
        Assert.Contains("Q-WB1", page);
        Assert.Contains("Fiyat yüksek", page);

        await client.PostFormAsync($"/yonetim/crm/arama-listesi/teklif/{id}", token, ("sonuc", "sonra"), ("note", "Bahara düşünüyor"));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(Today.AddDays(30), quote.WinBackOn);
        Assert.EndsWith("arandı: Bahara düşünüyor", quote.WinBackNote);
        await client.PostFormAsync($"/yonetim/crm/arama-listesi/teklif/{id}", token, ("sonuc", "kapat"));
        Assert.Null(await app.WithDbAsync(db => db.Quotes.Where(x => x.Id == id).Select(x => x.WinBackOn).SingleAsync()));
    }

    [Fact]
    public async Task Referrals_AndNeighborWork_AreShown()
    {
        await using var app = postgres.CreateApp("referrals");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (referrer, orderNumber) = await app.WithDbAsync(async db =>
        {
            var r = new Customer { Name = "Mehmet Kaya", Phone = "0532 555 55 55", Address = "Kazımdirik Mah. Gül Sitesi A Blok", District = "Bornova" };
            db.Customers.Add(r); await db.SaveChangesAsync();
            db.Quotes.Add(new Quote { QuoteNumber = "Q-R0", CustomerId = r.Id, Title = "Kendi işi", Status = QuoteStatus.Converted, GrandTotal = 10000 });
            var order = new ProductionOrder { OrderNumber = "SP-KOMSU", CustomerId = r.Id, Title = "Gül Sitesi pencereler", Status = OrderStatus.Delivered, OrderDate = Today, InstallAddress = "Kazımdirik Mah. 372/8 Sok. Gül Sitesi A Blok No:3 Bornova" };
            db.ProductionOrders.Add(order); await db.SaveChangesAsync();
            return (r.Id, order.OrderNumber);
        });

        // Tavsiye eden seçilince kaynak boşsa "Tavsiye" yazılır; kendisi seçilemez.
        var created = await client.PostFormAsync("/yonetim/crm/musteriler/ekle", token, ("Name", "Zeynep Ak"), ("Kind", "Individual"), ("Status", "Lead"), ("PartyType", "Customer"),
            ("ReferredByCustomerId", referrer.ToString()), ("Address", "Gül Sitesi C Blok Kazımdirik"), ("District", "Bornova"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var zeynep = await app.WithDbAsync(db => db.Customers.AsNoTracking().SingleAsync(x => x.Name == "Zeynep Ak"));
        Assert.Equal((referrer, "Tavsiye"), (zeynep.ReferredByCustomerId!.Value, zeynep.Source!));
        await app.WithDbAsync(db => { db.Quotes.Add(new Quote { QuoteNumber = "Q-R1", CustomerId = zeynep.Id, Title = "Balkon", Status = QuoteStatus.Accepted, GrandTotal = 25000 }); return db.SaveChangesAsync(); });
        var self = await client.PostFormAsync($"/yonetim/crm/musteriler/duzenle/{referrer}", token, ("Id", referrer.ToString()), ("Name", "Mehmet Kaya"), ("Kind", "Individual"), ("Status", "Active"), ("PartyType", "Customer"), ("ReferredByCustomerId", referrer.ToString()));
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Null(await app.WithDbAsync(db => db.Customers.Where(x => x.Id == referrer).Select(x => x.ReferredByCustomerId).SingleAsync()));

        var report = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/musteriler/tavsiyeler"));
        Assert.Contains("Mehmet Kaya", report);
        Assert.Contains("Zeynep Ak", report);
        Assert.Contains("25.000,00 TL", report);

        // Yeni müşteri kartında aynı sitedeki teslim edilmiş iş görünür; tavsiye edenin kartında tavsiye ettiği müşteri.
        var edit = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/musteriler/duzenle/{zeynep.Id}"));
        Assert.Contains("Aynı site / apartman", edit);
        Assert.Contains(orderNumber, edit);
        Assert.Contains("Zeynep Ak", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/musteriler/duzenle/{referrer}")));

        // Web talebinde de görünür
        var messageId = await app.WithDbAsync(async db =>
        {
            var m = new ContactMessage { Name = "Komşu", ContactInfo = "0533 000 00 00", ProjectLocation = "Gül Sitesi D Blok, Kazımdirik Bornova", Message = "Pencere", PrivacyAccepted = true };
            db.ContactMessages.Add(m); await db.SaveChangesAsync(); return m.Id;
        });
        Assert.Contains(orderNumber, await client.GetStringAsync($"/yonetim/mesajlar/{messageId}"));
    }

    [Fact]
    public async Task WhatsAppAutoNotifications_AreTurnedOffOnce()
    {
        await using (var first = postgres.CreateApp("whatsapp_off"))
        {
            first.NewClient();
            await first.WithDbAsync(async db =>
            {
                foreach (var key in new[] { "NotifyWhatsAppEnabled", "CustomerNotifyWhatsAppEnabled" }) (await db.SiteSettingRecords.SingleAsync(x => x.Key == key)).Value = "true";
                db.SiteSettingRecords.Remove(await db.SiteSettingRecords.SingleAsync(x => x.Key == SeedData.WhatsAppOffKey));
                return await db.SaveChangesAsync();
            });
        }
        await using (var second = postgres.CreateApp("whatsapp_off", schemaMode: "existing"))
        {
            second.NewClient();
            var values = await second.WithDbAsync(db => db.SiteSettingRecords.Where(x => x.Key == "NotifyWhatsAppEnabled" || x.Key == "CustomerNotifyWhatsAppEnabled").Select(x => x.Value).ToListAsync());
            Assert.All(values, v => Assert.Equal("false", v));
            // Yönetici yeniden açarsa bir sonraki açılışta kapatılmaz.
            await second.WithDbAsync(async db => { (await db.SiteSettingRecords.SingleAsync(x => x.Key == "NotifyWhatsAppEnabled")).Value = "true"; return await db.SaveChangesAsync(); });
        }
        await using var third = postgres.CreateApp("whatsapp_off", schemaMode: "existing");
        third.NewClient();
        Assert.Equal("true", await third.WithDbAsync(db => db.SiteSettingRecords.Where(x => x.Key == "NotifyWhatsAppEnabled").Select(x => x.Value).SingleAsync()));
    }
}
