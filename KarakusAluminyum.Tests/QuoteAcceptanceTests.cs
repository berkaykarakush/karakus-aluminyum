using System.Net;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KarakusAluminyum.Tests;

// README 7.18: müşterinin QR'lı teklif sayfasından online onayı; sipariş kendiliğinden açılır.
[Collection("postgres")]
public sealed class QuoteAcceptanceTests(PostgresFixture postgres)
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

    [Theory]
    [InlineData(QuoteStatus.Sent, null, true)]
    [InlineData(QuoteStatus.Draft, null, false)]
    [InlineData(QuoteStatus.Accepted, null, false)]
    [InlineData(QuoteStatus.Rejected, null, false)]
    [InlineData(QuoteStatus.Sent, -1, false)] // geçerlilik dün bitti
    [InlineData(QuoteStatus.Sent, 0, true)]   // bugün son gün
    public void OnlyOpenSentQuotes_CanBeAccepted(QuoteStatus status, int? validDays, bool open)
    {
        var now = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
        var quote = new Quote { Status = status, ValidUntilUtc = validDays is int d ? now.AddDays(d) : null };
        Assert.Equal(open, QuoteAcceptance.Blocked(quote, now) is null);
    }

    [Fact]
    public void Hash_ChangesWithContent()
    {
        var quote = new Quote { QuoteNumber = "T-1", GrandTotal = 1000, DocumentJson = "{\"a\":1}", SnapshotJson = "{}" };
        var first = QuoteAcceptance.Hash(quote);
        Assert.Equal(64, first.Length);
        quote.GrandTotal = 1001;
        Assert.NotEqual(first, QuoteAcceptance.Hash(quote));
    }

    [Fact]
    public async Task Customer_AcceptsOnline_OrderOpens_EmailsGo()
    {
        await using var app = postgres.CreateApp("quote_accept");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        await app.WithDbAsync(async db => { (await db.SiteSettingRecords.SingleAsync(x => x.Key == "NotifyEmailTo")).Value = "ekip@example.com"; return await db.SaveChangesAsync(); });
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Deniz Aksoy");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "deniz@example.com").SetProperty(c => c.Phone, "0532 777 88 99")));
        await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, "Ev pencereleri", QuoteDocumentTests.BlocksJson(("", new[] { ("PVC pencere", 12000m) })));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(q => q.Title == "Ev pencereleri"));
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quote.Id}"));
        var key = await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quote.Id).Select(q => q.PublicToken!).SingleAsync());
        Assert.Contains("Online onay, durumu \"Gönderildi\"", details); // taslakta kapalı

        var visitor = app.NewClient();
        var draft = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/belge/teklif/{key}"));
        Assert.Contains("Teklif henüz hazırlanıyor.", draft);
        Assert.DoesNotContain("name=\"agree\"", draft);

        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{quote.Id}", token, ("status", ((int)QuoteStatus.Sent).ToString()));
        details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quote.Id}"));
        Assert.Contains("https://wa.me/905327778899?text=", details);
        var page = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/belge/teklif/{key}"));
        Assert.Contains("Teklifi onayla ↓", page);
        Assert.Contains("name=\"agree\"", page);
        var formToken = await visitor.GetTokenAsync($"/belge/teklif/{key}");

        // Ad veya onay kutusu eksikse kabul edilmez
        await visitor.PostFormAsync($"/belge/teklif/{key}/kabul", formToken, ("name", "Deniz Aksoy"));
        Assert.Equal(QuoteStatus.Sent, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quote.Id).Select(q => q.Status).SingleAsync()));
        Assert.Contains("kutuyu işaretleyin", WebUtility.HtmlDecode(await visitor.GetStringAsync($"/belge/teklif/{key}")));

        var accepted = await visitor.PostFormAsync($"/belge/teklif/{key}/kabul", formToken, ("name", "  Deniz Aksoy "), ("agree", "true"));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal($"/belge/teklif/{key}#onay", accepted.Headers.Location!.OriginalString);
        var saved = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quote.Id));
        Assert.Equal((QuoteStatus.Accepted, "Deniz Aksoy", saved.GrandTotal), (saved.Status, saved.AcceptedName!, saved.AcceptedTotal!.Value));
        Assert.Equal(QuoteAcceptance.Hash(saved), saved.AcceptedHash);
        Assert.NotNull(saved.AcceptedUtc);
        var order = await app.WithDbAsync(db => db.ProductionOrders.AsNoTracking().Include(o => o.Events).SingleAsync(o => o.QuoteId == quote.Id));
        Assert.Equal(OrderStatus.Approved, order.Status);
        Assert.NotNull(order.TrackingToken);
        Assert.Contains(order.Events, e => e.Text.StartsWith("Müşteri teklifi online onayladı: Deniz Aksoy"));
        var after = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/belge/teklif/{key}"));
        Assert.Contains($"{order.OrderNumber} numaralı siparişiniz oluşturuldu", after);
        Assert.Contains("tarafından", after);
        Assert.DoesNotContain("name=\"agree\"", after);

        // İkinci gönderim ikinci sipariş açmaz
        await visitor.PostFormAsync($"/belge/teklif/{key}/kabul", formToken, ("name", "Deniz Aksoy"), ("agree", "true"));
        Assert.Equal(1, await app.WithDbAsync(db => db.ProductionOrders.CountAsync(o => o.QuoteId == quote.Id)));

        // Panelde kanıt
        details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quote.Id}"));
        Assert.Contains("teklif onaydan sonra değişmedi", details);

        // Ekibe ve müşteriye e-posta; müşteri e-postasında takip bağlantısı
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Status == NotificationStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptUtc, DateTime.UtcNow.AddMinutes(-1))));
        var email = new FakeEmail();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var s = scope.ServiceProvider;
            await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, new NoWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
        }
        var team = Assert.Single(email.Sent, m => m.To == "ekip@example.com");
        Assert.StartsWith($"Teklif onaylandı: {quote.QuoteNumber}", team.Subject);
        Assert.Contains($"/yonetim/siparisler/detay/{order.Id}", team.TextBody);
        var mine = Assert.Single(email.Sent, m => m.To == "deniz@example.com");
        Assert.Contains("onayladınız", mine.TextBody);
        Assert.Contains($"/takip/{order.TrackingToken}", mine.TextBody);
    }

    [Fact]
    public async Task ExistingDatabase_GetsAcceptanceColumns()
    {
        await using (var first = postgres.CreateApp("quote_accept_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Quotes\" DROP COLUMN \"AcceptedUtc\", DROP COLUMN \"AcceptedName\", DROP COLUMN \"AcceptedIp\", DROP COLUMN \"AcceptedUserAgent\", DROP COLUMN \"AcceptedTotal\", DROP COLUMN \"AcceptedHash\""));
        }
        await using var second = postgres.CreateApp("quote_accept_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/crm/teklifler")).StatusCode);
    }
}
