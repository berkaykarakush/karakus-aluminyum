using System.Net;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KarakusAluminyum.Tests;

// README 7.6: sipariş üretime girdi, sevk tarihi, montaj hatırlatması ve servis ziyareti müşteriye e-posta / WhatsApp ile bildirilir.
public sealed class CustomerNotificationRuleTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc); // TR 12:00
    private static ProductionOrder Order(OrderStatus status, DateOnly? ship = null, DateOnly? install = null) => new() { Id = 1, OrderNumber = "SP-1", Title = "Villa", Status = status, PlannedShipDate = ship, InstallationDate = install };
    private static readonly DateOnly D10 = new(2026, 12, 10);

    [Fact]
    public void Reminder_IsEveningBefore_OrSoonWhenThatPassed()
    {
        Assert.Equal(new DateTime(2026, 12, 9, 15, 0, 0, DateTimeKind.Utc), CustomerNotifier.ReminderUtc(D10, Now)); // 9 Aralık 18:00 TR
        var late = new DateTime(2026, 12, 9, 16, 0, 0, DateTimeKind.Utc);
        Assert.Equal(late + CustomerNotifier.Delay, CustomerNotifier.ReminderUtc(D10, late));
    }

    [Fact]
    public void PlanOrder_PicksEvents()
    {
        string[] Events(OrderSnapshot before, ProductionOrder after) => CustomerNotifier.PlanOrder(before, after, Now).Select(x => x.Event).ToArray();
        // Üretime girince yalnızca üretim bildirimi (sevk tarihi içinde yazar)
        Assert.Equal(new[] { CustomerEvents.Production }, Events(new(OrderStatus.Approved, D10, null), Order(OrderStatus.InProduction, D10)));
        // Onaylı işte sevk tarihi değişince; planlamadaki işte değil
        Assert.Equal(new[] { CustomerEvents.ShipDate }, Events(new(OrderStatus.Approved, null, null), Order(OrderStatus.Approved, D10)));
        Assert.Empty(Events(new(OrderStatus.Plan, null, null), Order(OrderStatus.Plan, D10)));
        // İş onaylanınca mevcut sevk tarihi bildirilir; geçmiş tarih bildirilmez
        Assert.Equal(new[] { CustomerEvents.ShipDate }, Events(new(OrderStatus.AwaitingApproval, D10, null), Order(OrderStatus.Approved, D10)));
        Assert.Empty(Events(new(OrderStatus.Approved, null, null), Order(OrderStatus.Approved, new DateOnly(2026, 11, 1))));
        // Montaj tarihi; montaj hariç ve iptal işte yok
        Assert.Equal(new[] { CustomerEvents.Installation }, Events(new(OrderStatus.Shipped, null, null), Order(OrderStatus.Shipped, null, D10)));
        var excluded = Order(OrderStatus.Shipped, null, D10); excluded.InstallationExcluded = true;
        Assert.Empty(Events(new(OrderStatus.Shipped, null, null), excluded));
        Assert.Empty(Events(new(OrderStatus.Approved, null, null), Order(OrderStatus.Cancelled, D10, D10)));
    }

    [Fact]
    public void PlanService_OnlyWhenVisitChanges()
    {
        var r = new ServiceRequest { Id = 1, Number = "SRV-1", Status = ServiceStatus.Scheduled, PlannedOn = D10, PlannedAt = new TimeOnly(14, 30) };
        Assert.Single(CustomerNotifier.PlanService(ServiceSnapshot.None, r, Now));
        Assert.Empty(CustomerNotifier.PlanService(new(ServiceStatus.Scheduled, D10, new TimeOnly(14, 30)), r, Now));
        Assert.Single(CustomerNotifier.PlanService(new(ServiceStatus.Scheduled, D10, new TimeOnly(10, 0)), r, Now)); // saat değişti
    }

    [Fact]
    public void Obsolete_WhenSourceChanged()
    {
        var order = Order(OrderStatus.Approved, D10);
        var job = new NotificationJob { Event = CustomerEvents.ShipDate, EventDate = D10, ProductionOrderId = 1, ProductionOrder = order };
        Assert.Null(CustomerNotifier.Obsolete(job, Now));
        order.PlannedShipDate = D10.AddDays(2);
        Assert.NotNull(CustomerNotifier.Obsolete(job, Now));
        order.PlannedShipDate = D10; order.Status = OrderStatus.Cancelled;
        Assert.Equal("Sipariş iptal edildi.", CustomerNotifier.Obsolete(job, Now));
    }

    [Fact]
    public void DateText_IsTurkish() => Assert.Equal("10 Aralık 2026 Perşembe, saat 14:30", CustomerNotifier.DateText(D10, new TimeOnly(14, 30)));
}

[Collection("postgres")]
public sealed class CustomerNotificationFlowTests(PostgresFixture postgres)
{
    private sealed class FakeEmail : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = new();
        public bool IsConfigured => true;
        public Task<string?> SendAsync(OutgoingEmail email, CancellationToken cancellationToken) { lock (Sent) Sent.Add(email); return Task.FromResult<string?>("mail"); }
    }

    private sealed class FakeWhatsApp : IWhatsAppSender
    {
        public List<OutgoingWhatsApp> Sent { get; } = new();
        public bool IsConfigured => true;
        public Task<string?> SendAsync(OutgoingWhatsApp message, CancellationToken cancellationToken) { lock (Sent) Sent.Add(message); return Task.FromResult<string?>("wamid"); }
    }

    private static DateOnly Today => KarakusAluminyum.Web.Services.Documents.PrintDates.Today;
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    // Gecikmeli bildirimleri öne çekip bir tur gönderir.
    private static async Task DispatchAsync(AppFactory app, FakeEmail email, FakeWhatsApp whatsApp)
    {
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Status == NotificationStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptUtc, DateTime.UtcNow.AddMinutes(-1))));
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, whatsApp, NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
    }

    private static Task SetAsync(AppFactory app, params (string Key, string Value)[] values) => app.WithDbAsync(async db =>
    {
        foreach (var (key, value) in values) (await db.SiteSettingRecords.SingleAsync(x => x.Key == key)).Value = value;
        return await db.SaveChangesAsync();
    });

    private static Task<List<NotificationJob>> JobsAsync(AppFactory app, int orderId) => app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().Where(x => x.ProductionOrderId == orderId).OrderBy(x => x.Id).ToListAsync());

    [Fact]
    public async Task Order_ShipDate_Production_Installation_AndCancel()
    {
        await using var app = postgres.CreateApp("customer_notify_order");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        await SetAsync(app, ("CustomerNotifyWhatsAppEnabled", "true"), ("WhatsAppTemplateProduction", "siparis_uretimde"));
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Ayşe Yılmaz");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "ayse@example.com").SetProperty(c => c.Phone, "0532 111 22 33")));
        var ship = Today.AddDays(20);
        (string, string)[] Form(DateOnly? shipDate) => new[] { ("CustomerId", customer.ToString()), ("Title", "Villa doğramaları"), ("OrderDate", Iso(Today)), ("PlannedShipDate", shipDate is DateOnly d ? Iso(d) : ""), ("InstallAddress", "Urla İzmir") };
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, Form(ship));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());
        Assert.Empty(await JobsAsync(app, orderId)); // planlamadaki iş bildirilmez

        // Onaylanınca sevk tarihi: e-posta sırada, WhatsApp şablonu yok → atlandı
        await client.PostFormAsync($"/yonetim/siparisler/durum/{orderId}", token, ("status", "Approved"));
        var jobs = await JobsAsync(app, orderId);
        Assert.Equal(2, jobs.Count);
        var mail = jobs.Single(j => j.Channel == NotificationChannel.CustomerEmail);
        Assert.Equal((CustomerEvents.ShipDate, "ayse@example.com", NotificationStatus.Pending, ship), (mail.Event, mail.Recipient, mail.Status, mail.EventDate!.Value));
        Assert.True(mail.NextAttemptUtc > DateTime.UtcNow.AddMinutes(3)); // kısa bekleme
        var wa = jobs.Single(j => j.Channel == NotificationChannel.CustomerWhatsApp);
        Assert.Equal(("905321112233", NotificationStatus.Skipped), (wa.Recipient, wa.Status));

        // Sevk tarihi düzeltilince eskisi gönderilmez, yenisi sıraya girer
        var newShip = ship.AddDays(3);
        await client.PostFormAsync($"/yonetim/siparisler/duzenle/{orderId}", token, Form(newShip));
        jobs = await JobsAsync(app, orderId);
        Assert.Equal(NotificationStatus.Skipped, jobs.Single(j => j.Id == mail.Id).Status);
        Assert.Single(jobs, j => j.Channel == NotificationChannel.CustomerEmail && j.Status == NotificationStatus.Pending && j.EventDate == newShip);
        // Aynı kayıt tekrar kaydedilince yinelenmez
        await client.PostFormAsync($"/yonetim/siparisler/duzenle/{orderId}", token, Form(newShip));
        Assert.Equal(jobs.Count, (await JobsAsync(app, orderId)).Count);

        // Üretime alınca: e-posta ve WhatsApp (şablonlu)
        await client.PostFormAsync($"/yonetim/siparisler/durum/{orderId}", token, ("status", "InProduction"));
        var email = new FakeEmail(); var whatsApp = new FakeWhatsApp();
        await DispatchAsync(app, email, whatsApp);
        Assert.Contains(email.Sent, m => m.To == "ayse@example.com" && m.Subject.StartsWith("Sevk tarihiniz:") && m.TextBody.Contains("Villa doğramaları"));
        var production = Assert.Single(email.Sent, m => m.Subject.StartsWith("Siparişiniz üretime alındı"));
        Assert.Contains("Planlanan sevk tarihi", production.TextBody);
        var message = Assert.Single(whatsApp.Sent);
        Assert.Equal(("905321112233", "siparis_uretimde"), (message.To, message.TemplateName!));
        Assert.Equal("Villa doğramaları", message.Parameters[3]);

        // Montaj tarihi: önceki akşam 18:00'de hatırlatma; tarih değişince eskisi düşer
        var install = Today.AddDays(30);
        await client.PostFormAsync($"/yonetim/siparisler/montaj/{orderId}", token, ("installationDate", Iso(install)));
        var reminder = (await JobsAsync(app, orderId)).Single(j => j.Event == CustomerEvents.Installation && j.Channel == NotificationChannel.CustomerEmail);
        Assert.Equal(CustomerNotifier.ReminderUtc(install, DateTime.UtcNow).Date, reminder.NextAttemptUtc.Date);
        Assert.Equal(15, reminder.NextAttemptUtc.Hour);
        await client.PostFormAsync($"/yonetim/siparisler/montaj/{orderId}", token, ("installationDate", Iso(install.AddDays(1))));
        Assert.Equal(NotificationStatus.Skipped, (await JobsAsync(app, orderId)).Single(j => j.Id == reminder.Id).Status);

        // Sipariş sayfasında bildirim listesi
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("Müşteri bildirimleri", details);
        Assert.Contains("Montaj hatırlatması", details);

        // Gönderim anında tarih panel dışından değiştiyse gitmez
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(x => x.SetProperty(o => o.InstallationDate, install.AddDays(5))));
        email.Sent.Clear();
        await DispatchAsync(app, email, whatsApp);
        Assert.DoesNotContain(email.Sent, m => m.Subject.StartsWith("Montaj"));
        Assert.Contains(await JobsAsync(app, orderId), j => j.Event == CustomerEvents.Installation && j.Status == NotificationStatus.Skipped && j.LastError == "Montaj tarihi değişti.");

        // İptal edilen işin sıradaki bildirimleri düşer
        await client.PostFormAsync($"/yonetim/siparisler/montaj/{orderId}", token, ("installationDate", Iso(install.AddDays(10))));
        await client.PostFormAsync($"/yonetim/siparisler/durum/{orderId}", token, ("status", "Cancelled"));
        Assert.DoesNotContain(await JobsAsync(app, orderId), j => j.Status == NotificationStatus.Pending);
    }

    [Fact]
    public async Task OptOut_And_ServiceVisit()
    {
        await using var app = postgres.CreateApp("customer_notify_service");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Mehmet Demir");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "mehmet@example.com")));

        (string, string)[] Request(DateOnly? date, string? time) => new[] { ("CustomerId", customer.ToString()), ("ReceivedOn", Iso(Today)), ("Channel", "Telefon"), ("Category", "Bakım"),
            ("Complaint", "Yıllık bakım"), ("Warranty", "Paid"), ("Address", "Bornova İzmir"), ("PlannedOn", date is DateOnly d ? Iso(d) : ""), ("PlannedAt", time ?? "") };
        var visit = Today.AddDays(3);
        await client.PostFormAsync("/yonetim/servis/ekle", token, Request(visit, "14:30"));
        var request = await app.WithDbAsync(db => db.ServiceRequests.AsNoTracking().SingleAsync(r => r.CustomerId == customer));
        Assert.Equal((ServiceStatus.Scheduled, new TimeOnly(14, 30)), (request.Status, request.PlannedAt!.Value));
        var job = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().SingleAsync(x => x.ServiceRequestId == request.Id));
        Assert.Equal((CustomerEvents.ServiceVisit, new TimeOnly(14, 30)), (job.Event!, job.EventTime!.Value));

        // Saat değişince eski düşer, yeni gider
        await client.PostFormAsync($"/yonetim/servis/duzenle/{request.Id}", token, Request(visit, "10:00"));
        var email = new FakeEmail();
        await DispatchAsync(app, email, new FakeWhatsApp());
        var sent = Assert.Single(email.Sent);
        Assert.Contains("saat 10:00", sent.TextBody);
        Assert.Contains(request.Number, sent.TextBody);
        Assert.Contains("Bornova İzmir", sent.TextBody);
        Assert.Contains("Servis ziyareti", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/servis/detay/{request.Id}")));

        // Bildirim istemeyen müşteriye kayıt açılmaz; cari kartından işaretlenir
        var edit = await client.GetStringAsync($"/yonetim/crm/musteriler/duzenle/{customer}");
        Assert.Contains("NotificationsOptOut", edit);
        await client.PostFormAsync($"/yonetim/crm/musteriler/duzenle/{customer}", token, ("Id", customer.ToString()), ("Name", "Mehmet Demir"), ("Kind", "Individual"), ("Status", "Active"),
            ("PartyType", "Customer"), ("Email", "mehmet@example.com"), ("NotificationsOptOut", "true"));
        Assert.True(await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).Select(c => c.NotificationsOptOut).SingleAsync()));
        var before = await app.WithDbAsync(db => db.NotificationJobs.CountAsync());
        await client.PostFormAsync($"/yonetim/servis/duzenle/{request.Id}", token, Request(visit.AddDays(1), "09:00"));
        Assert.Equal(before, await app.WithDbAsync(db => db.NotificationJobs.CountAsync()));
    }

    [Fact]
    public async Task ExistingDatabase_GetsCustomerNotificationColumns()
    {
        await using (var first = postgres.CreateApp("customer_notify_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "NotificationJobs" DROP COLUMN "ProductionOrderId", DROP COLUMN "ServiceRequestId", DROP COLUMN "Event", DROP COLUMN "EventDate", DROP COLUMN "EventTime";
                ALTER TABLE "NotificationJobs" ALTER COLUMN "ContactMessageId" SET NOT NULL;
                ALTER TABLE "ServiceRequests" DROP COLUMN "PlannedAt"; ALTER TABLE "Customers" DROP COLUMN "NotificationsOptOut";
                """));
        }
        await using var second = postgres.CreateApp("customer_notify_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/bildirimler")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/servis")).StatusCode);
    }
}
