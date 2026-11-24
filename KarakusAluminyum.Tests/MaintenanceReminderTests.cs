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

// README 7.8: teslimin yıldönümünde bakım ve garanti bitmeden önce hatırlatma; bağlantı bakım talebini hazır açar.
[Collection("postgres")]
public sealed class MaintenanceReminderTests(PostgresFixture postgres)
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

    [Fact]
    public void Schedule_YearlyAndBeforeWarrantyEnds()
    {
        var plan = MaintenanceReminders.Schedule(new DateOnly(2025, 1, 10), 2);
        Assert.Equal((CustomerEvents.MaintenanceAnnual, new DateOnly(2026, 1, 10), 1), (plan[0].Event, plan[0].Date, plan[0].Year));
        Assert.Equal((CustomerEvents.MaintenanceWarranty, new DateOnly(2026, 12, 11)), (plan[1].Event, plan[1].Date));
        Assert.DoesNotContain(plan, d => d.Event == CustomerEvents.MaintenanceAnnual && d.Year == 2); // garanti hatırlatması yerine geçer
        Assert.Equal(MaintenanceReminders.MaxYears, plan.Count);
    }

    private static async Task<int> DeliveredOrderAsync(AppFactory app, HttpClient client, string token, string name, DateOnly deliveredOn, bool optOut = false)
    {
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, name);
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, name.Replace(" ", ".").ToLowerInvariant() + "@example.com").SetProperty(c => c.NotificationsOptOut, optOut)));
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", name + " işi"), ("OrderDate", deliveredOn.AddDays(-30).ToString("yyyy-MM-dd")));
        var id = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == id).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OrderStatus.Delivered).SetProperty(o => o.DeliveredOn, deliveredOn)));
        return id;
    }

    private static async Task<int> ScanAsync(AppFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MaintenanceReminders>().RunAsync(DateTime.UtcNow);
    }

    private static async Task DispatchAsync(AppFactory app, FakeEmail email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, new NoWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
    }

    [Fact]
    public async Task Scan_Enqueues_Once_Sends_AndLinksToMaintenanceForm()
    {
        await using var app = postgres.CreateApp("maintenance_reminders");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var annual = await DeliveredOrderAsync(app, client, token, "Yıl Dönümü", Today.AddYears(-1));
        var warranty = await DeliveredOrderAsync(app, client, token, "Garanti Biten", Today.AddDays(30).AddYears(-2)); // varsayılan garanti 2 yıl
        await DeliveredOrderAsync(app, client, token, "Bildirim İstemez", Today.AddYears(-1), optOut: true);
        await DeliveredOrderAsync(app, client, token, "Henüz Değil", Today.AddMonths(-6));

        Assert.Equal(2, await ScanAsync(app));
        Assert.Equal(0, await ScanAsync(app)); // aynı hatırlatma ikinci kez eklenmez
        var jobs = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().Where(x => x.Event != null && x.Event.StartsWith("maintenance")).ToListAsync());
        Assert.Equal(new[] { (annual, CustomerEvents.MaintenanceAnnual), (warranty, CustomerEvents.MaintenanceWarranty) }.OrderBy(x => x.Item1), jobs.Select(j => (j.ProductionOrderId!.Value, j.Event!)).OrderBy(x => x.Item1));

        var email = new FakeEmail();
        await DispatchAsync(app, email);
        var yearly = Assert.Single(email.Sent, m => m.Subject.StartsWith("Yıllık bakım zamanı"));
        Assert.Contains("1. yılı doldu", yearly.TextBody);
        Assert.Contains("Garantiniz", yearly.TextBody);
        var key = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == annual).Select(o => o.TrackingToken!).SingleAsync());
        Assert.Contains($"/takip/{key}?bakim=1#servis", yearly.TextBody);
        var ending = Assert.Single(email.Sent, m => m.Subject.StartsWith("Garantiniz"));
        Assert.Contains("sona eriyor", ending.TextBody);

        // Bağlantı bakım talebini hazır açar
        var form = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/takip/{key}?bakim=1"));
        Assert.Contains("<option selected=\"selected\">Bakım</option>", form);
        Assert.Contains("Yıllık bakım ve kontrol yaptırmak istiyorum.", form);
    }

    [Fact]
    public async Task ChangedDeliveryDate_SkipsQueuedReminder()
    {
        await using var app = postgres.CreateApp("maintenance_skip");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var order = await DeliveredOrderAsync(app, client, token, "Tarih Değişen", Today.AddYears(-1));
        Assert.Equal(1, await ScanAsync(app));
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == order).ExecuteUpdateAsync(x => x.SetProperty(o => o.DeliveredOn, Today.AddYears(-1).AddDays(-10))));
        var email = new FakeEmail();
        await DispatchAsync(app, email);
        Assert.Empty(email.Sent);
        Assert.Equal(NotificationStatus.Skipped, await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.ProductionOrderId == order).Select(x => x.Status).SingleAsync()));
    }
}
