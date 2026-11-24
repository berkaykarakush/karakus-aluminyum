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

// README 7.20: teslimden birkaç gün sonra Google yorum isteği; açık eksik veya servis talebi varsa gönderilmez.
[Collection("postgres")]
public sealed class ReviewRequestTests(PostgresFixture postgres)
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

    private static async Task<int> DeliveredAsync(AppFactory app, HttpClient client, string token, string name, DateOnly delivered)
    {
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, name);
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, name.Replace(" ", ".").ToLowerInvariant() + "@example.com")));
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", name + " evi"), ("OrderDate", delivered.AddDays(-20).ToString("yyyy-MM-dd")));
        var id = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == id).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OrderStatus.Delivered).SetProperty(o => o.DeliveredOn, delivered)));
        return id;
    }

    private static async Task<int> ScanAsync(AppFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReviewRequests>().RunAsync(DateTime.UtcNow);
    }

    private static async Task<FakeEmail> DispatchAsync(AppFactory app)
    {
        var email = new FakeEmail();
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, new NoWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
        return email;
    }

    [Fact]
    public async Task ReviewRequest_OnlyForCleanJobs_Once()
    {
        await using var app = postgres.CreateApp("review_requests");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var happy = await DeliveredAsync(app, client, token, "Mutlu Müşteri", Today.AddDays(-3));
        var punch = await DeliveredAsync(app, client, token, "Eksikli Müşteri", Today.AddDays(-4));
        var service = await DeliveredAsync(app, client, token, "Servisli Müşteri", Today.AddDays(-3));
        await DeliveredAsync(app, client, token, "Yeni Teslim", Today.AddDays(-1));
        await client.PostFormAsync($"/yonetim/eksikler/ekle/{punch}", token, ("Kind", "Kol / kilit eksik"), ("Description", "Kol eksik"), ("Quantity", "1"), ("ReportedOn", Today.ToString("yyyy-MM-dd")));
        var serviceCustomer = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == service).Select(o => o.CustomerId).SingleAsync());
        await client.PostFormAsync("/yonetim/servis/ekle", token, ("CustomerId", serviceCustomer.ToString()), ("ProductionOrderId", service.ToString()), ("ReceivedOn", Today.ToString("yyyy-MM-dd")), ("Channel", "Telefon"), ("Category", "Bakım"), ("Complaint", "Kanat sarkıyor"), ("Warranty", "InWarranty"));

        // Yorum bağlantısı yoksa hiç çalışmaz
        Assert.Equal(0, await ScanAsync(app));
        await app.WithDbAsync(async db => { (await db.SiteSettingRecords.SingleAsync(x => x.Key == "GoogleReviewUrl")).Value = "https://g.page/r/ornek/review"; return await db.SaveChangesAsync(); });

        Assert.Equal(1, await ScanAsync(app));
        Assert.Equal(0, await ScanAsync(app)); // her işe bir kez
        var job = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().SingleAsync(x => x.Event == CustomerEvents.ReviewRequest));
        Assert.Equal(happy, job.ProductionOrderId);

        var email = await DispatchAsync(app);
        var mail = Assert.Single(email.Sent);
        Assert.StartsWith("Memnun kaldınız mı?", mail.Subject);
        Assert.Contains("https://g.page/r/ornek/review", mail.TextBody);
        Assert.Contains("/takip/", mail.TextBody);

        // Eksik giderilince (pencere içindeyse) sonraki taramada istenir
        var punchItem = await app.WithDbAsync(db => db.PunchItems.Where(x => x.ProductionOrderId == punch).Select(x => x.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/eksikler/durum/{punchItem}", token, ("status", "Resolved"), ("resolution", "Kol takıldı"));
        Assert.Equal(1, await ScanAsync(app));
        // Gönderilmeden önce servis talebi açılırsa yorum istenmez
        var punchCustomer = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == punch).Select(o => o.CustomerId).SingleAsync());
        await client.PostFormAsync("/yonetim/servis/ekle", token, ("CustomerId", punchCustomer.ToString()), ("ProductionOrderId", punch.ToString()), ("ReceivedOn", Today.ToString("yyyy-MM-dd")), ("Channel", "Telefon"), ("Category", "Bakım"), ("Complaint", "Yeni sorun"), ("Warranty", "InWarranty"));
        Assert.Empty((await DispatchAsync(app)).Sent);
        Assert.Equal("İşte açık servis talebi var; yorum istenmedi.", await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.ProductionOrderId == punch && x.Event == CustomerEvents.ReviewRequest).Select(x => x.LastError).SingleAsync()));
    }
}
