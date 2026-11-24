using System.Net;
using KarakusAluminyum.Web.Controllers;
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

// README 7.7: müşteri takip sayfası (QR / bağlantı) ve sayfadan servis talebi.
[Collection("postgres")]
public sealed class TrackingTests(PostgresFixture postgres)
{
    private sealed class FakeEmail : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = new();
        public bool IsConfigured => true;
        public Task<string?> SendAsync(OutgoingEmail email, CancellationToken cancellationToken) { lock (Sent) Sent.Add(email); return Task.FromResult<string?>("mail"); }
    }

    private sealed class NoWhatsApp : IWhatsAppSender
    {
        public bool IsConfigured => false;
        public Task<string?> SendAsync(OutgoingWhatsApp message, CancellationToken cancellationToken) => throw new NotificationNotConfiguredException("yok");
    }

    private static DateOnly Today => KarakusAluminyum.Web.Services.Documents.PrintDates.Today;

    private static async Task DispatchAsync(AppFactory app, FakeEmail email)
    {
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Status == NotificationStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptUtc, DateTime.UtcNow.AddMinutes(-1))));
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, new NoWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
    }

    [Theory]
    [InlineData(OrderStatus.Plan, 0)]
    [InlineData(OrderStatus.Approved, 0)]
    [InlineData(OrderStatus.InProduction, 1)]
    [InlineData(OrderStatus.Warehouse, 2)]
    [InlineData(OrderStatus.Delivered, 4)]
    [InlineData(OrderStatus.Cancelled, -1)]
    public void Steps_AreSimplifiedForCustomer(OrderStatus status, int step) => Assert.Equal(step, TrackingController.StepOf(status));

    [Fact]
    public async Task TrackingPage_StatusAndServiceRequest()
    {
        await using var app = postgres.CreateApp("tracking_page");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        await app.WithDbAsync(async db => { (await db.SiteSettingRecords.SingleAsync(x => x.Key == "NotifyEmailTo")).Value = "ekip@example.com"; return await db.SaveChangesAsync(); });
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Zeynep Kaya");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "zeynep@example.com")));
        await DrawingLists.PostAsync(client, token, "Takip listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 2), DrawingLists.Row("Banyo", 600, 600, quantity: 1)));
        var listId = await DrawingLists.ListIdAsync(app, "Takip listesi");
        var designs = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).Select(d => d.Id).ToArrayAsync());
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Ev doğramaları"), ("DrawingListId", listId.ToString()), ("OrderDate", Today.ToString("yyyy-MM-dd")),
            ("PlannedShipDate", Today.AddDays(15).ToString("yyyy-MM-dd")), ("InstallAddress", "Gizli Sokak 7 Buca"), ("Notes", "İç not: indirim yapıldı"));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());

        // Panelde bağlantı ve QR; anahtar ilk açılışta oluşur
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        var key = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.TrackingToken!).SingleAsync());
        Assert.Equal(22, key.Length);
        Assert.Contains($"/takip/{key}", details);
        Assert.Contains("WhatsApp ile gönder", details);

        // Herkese açık sayfa: durum ve pozlar var; adres, iç not ve fiyat yok; arama motoru ve önbellek kapalı
        var visitor = app.NewClient();
        var page = await visitor.GetAsync($"/takip/{key}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("noindex", page.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        foreach (var text in new[] { "Ev doğramaları", "Sipariş alındı", "Planlanan sevk tarihi", "1. Salon", "1200 × 1400", "teslim edildikten sonra" }) Assert.Contains(text, html);
        foreach (var text in new[] { "Gizli Sokak", "indirim yapıldı", "tracking-config" }) Assert.DoesNotContain(text, html);
        Assert.DoesNotContain("name=\"Complaint\"", html); // teslimden önce form yok
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/takip/aaaaaaaaaaaaaaaaaaaaaa")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/takip/kisa")).StatusCode);

        // Teslimden önce gönderilen talep kabul edilmez
        var formToken = await visitor.GetTokenAsync("/iletisim"); // teslimden önce sayfada form yok; doğrulama anahtarı sayfaya bağlı değil
        (string, string)[] Form(string complaint, bool privacy = true, params (string, string)[] extra) => new[] { ("ContactName", "Zeynep Kaya"), ("ContactPhone", "0532 000 11 22"),
            ("Category", "Ayar / kanat sarkması"), ("Complaint", complaint), ("PrivacyAccepted", privacy ? "true" : "false") }.Concat(extra).ToArray();
        var early = await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Kanat sarkıyor"));
        Assert.Contains("teslim edilen işler için", WebUtility.HtmlDecode(await early.Content.ReadAsStringAsync()));

        // Teslim edildikten sonra form açılır
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OrderStatus.Delivered).SetProperty(o => o.DeliveredOn, Today.AddDays(-30))));
        html = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/takip/{key}"));
        Assert.Contains("name=\"Complaint\"", html);
        Assert.Contains("✓ Sevk edildi", html);
        var noPrivacy = await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Kanat sarkıyor", privacy: false));
        Assert.Contains("KVKK aydınlatma metnini onaylayın", WebUtility.HtmlDecode(await noPrivacy.Content.ReadAsStringAsync()));

        var sent = await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Salon kanadı sarkıyor, zor kapanıyor.", true, ("PozIds", designs[0].ToString()), ("PozIds", "999999")));
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        Assert.Equal($"/takip/{key}#servis-alindi", sent.Headers.Location!.OriginalString);
        var request = await app.WithDbAsync(db => db.ServiceRequests.AsNoTracking().SingleAsync(r => r.ProductionOrderId == orderId));
        Assert.Equal(("Web sitesi", "Ayar / kanat sarkması", designs[0].ToString(), "Gizli Sokak 7 Buca", ServiceWarranty.InWarranty, ServiceStatus.Open),
            (request.Channel, request.Category, request.PozIds!, request.Address!, request.Warranty, request.Status));
        Assert.Contains(await app.WithDbAsync(db => db.OrderEvents.Where(e => e.ProductionOrderId == orderId).Select(e => e.Text).ToListAsync()), t => t.Contains($"servis talebi açtı: {request.Number}"));
        var afterRedirect = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/takip/{key}"));
        Assert.Contains($"Servis talebiniz alındı ({request.Number})", afterRedirect);
        Assert.Contains(request.Number, afterRedirect); // servis talepleri listesinde

        // Çift gönderim ikinci talep açmaz; günlük sınır aşılamaz
        await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Salon kanadı sarkıyor, zor kapanıyor."));
        Assert.Equal(1, await app.WithDbAsync(db => db.ServiceRequests.CountAsync(r => r.ProductionOrderId == orderId)));
        await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("İkinci sorun"));
        await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Üçüncü sorun"));
        var limited = await visitor.PostFormAsync($"/takip/{key}/servis", formToken, Form("Dördüncü sorun"));
        Assert.Contains("yeterince talep aldık", WebUtility.HtmlDecode(await limited.Content.ReadAsStringAsync()));
        Assert.Equal(TrackingController.DailyWebRequestLimit, await app.WithDbAsync(db => db.ServiceRequests.CountAsync(r => r.ProductionOrderId == orderId)));

        // Ekibe e-posta gider
        var email = new FakeEmail();
        await DispatchAsync(app, email);
        var team = email.Sent.First(m => m.To == "ekip@example.com" && m.Subject.Contains(request.Number));
        Assert.StartsWith("Web servis talebi", team.Subject);
        Assert.Contains("Salon kanadı sarkıyor", team.TextBody);
        Assert.Contains($"/yonetim/servis/detay/{request.Id}", team.TextBody);

        // Teslim tutanağında takip QR'ı
        Assert.Contains($"/takip/{key}", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/teslim-tutanagi/{orderId}")));

        // Bağlantı yenilenince eskisi açılmaz
        await client.PostFormAsync($"/yonetim/siparisler/takip-yenile/{orderId}", token);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/takip/{key}")).StatusCode);
        var renewed = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.TrackingToken!).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/takip/{renewed}")).StatusCode);
    }

    [Fact]
    public async Task CustomerEmail_ContainsTrackingLink()
    {
        await using var app = postgres.CreateApp("tracking_email");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Ali Veli");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "ali@example.com")));
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Ofis"), ("OrderDate", Today.ToString("yyyy-MM-dd")), ("PlannedShipDate", Today.AddDays(10).ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/siparisler/durum/{orderId}", token, ("status", "Approved"));
        var key = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.TrackingToken).SingleAsync());
        Assert.NotNull(key); // bildirim sıraya girerken oluşur
        var email = new FakeEmail();
        await DispatchAsync(app, email);
        var mail = Assert.Single(email.Sent);
        Assert.Contains($"/takip/{key}", mail.TextBody);
        Assert.Contains("Siparişimi takip et", mail.HtmlBody);
    }

    [Fact]
    public async Task ExistingDatabase_GetsTrackingColumn()
    {
        await using (var first = postgres.CreateApp("tracking_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ProductionOrders\" DROP COLUMN \"TrackingToken\""));
        }
        await using var second = postgres.CreateApp("tracking_upgrade", schemaMode: "existing");
        Assert.Equal(HttpStatusCode.NotFound, (await second.NewClient().GetAsync("/takip/aaaaaaaaaaaaaaaaaaaaaa")).StatusCode);
    }
}
