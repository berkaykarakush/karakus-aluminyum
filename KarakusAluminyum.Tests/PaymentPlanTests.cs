using System.Net;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Notifications;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KarakusAluminyum.Tests;

// README 7.19: ödeme planı (kapora, ara ödeme, kalan), siparişe bağlı tahsilat ve vade hatırlatması.
public sealed class PaymentPlanRuleTests
{
    private static readonly DateOnly Today = new(2026, 12, 10);
    private static OrderInstallment Row(int sort, string label, int? dueOffset, decimal amount) => new() { Id = sort + 1, SortOrder = sort, Label = label, DueOn = dueOffset is int d ? Today.AddDays(d) : null, Amount = amount };

    [Fact]
    public void Collections_AreSpreadByDueDate()
    {
        var rows = new[] { Row(0, "Kapora", -5, 3000), Row(1, "Ara ödeme", 10, 2000), Row(2, "Teslimde kalan", null, 5000) };
        var s = PaymentPlans.Summarize(rows, 4000, Today);
        Assert.Equal((10000m, 4000m, 6000m, 0m), (s.Planned, s.Collected, s.Remaining, s.Overpaid));
        Assert.Equal(new[] { InstallmentState.Paid, InstallmentState.Partial, InstallmentState.Waiting }, s.Rows.Select(r => r.State));
        Assert.Equal(1000m, s.Rows[1].Remaining);

        var late = PaymentPlans.Summarize(rows, 1000, Today);
        Assert.Equal(InstallmentState.Late, late.Rows[0].State); // vadesi geçmiş, kısmen ödenmiş taksit gecikmiş sayılır
        Assert.Equal(1, late.LateCount);
        Assert.Equal(500m, PaymentPlans.Summarize(rows, 10500, Today).Overpaid);
    }

    [Fact]
    public void Suggest_DepositTodayRestAtDelivery()
    {
        var rows = PaymentPlans.Suggest(10000, 30, Today, Today.AddDays(20));
        Assert.Equal(new[] { ("Kapora", (DateOnly?)Today, 3000m), ("Teslimde kalan", Today.AddDays(20), 7000m) }, rows.Select(r => (r.Label, r.DueOn, r.Amount)));
        Assert.Single(PaymentPlans.Suggest(10000, 100, Today, null));
        Assert.Empty(PaymentPlans.Suggest(0, 30, Today, null));
    }

    [Fact]
    public void Reminders_OnlyForFutureDueDates_DayBefore10()
    {
        var now = new DateTime(2026, 12, 10, 9, 0, 0, DateTimeKind.Utc); // TR 12:00
        var rows = new[] { Row(0, "Kapora", 0, 3000), Row(1, "A", 5, 1), Row(2, "B", 5, 1), Row(3, "C", null, 1) };
        var plan = CustomerNotifier.PlanPayments(rows, now);
        var notice = Assert.Single(plan); // bugünkü kapora ve tarihsiz taksit hatırlatılmaz; aynı gün tek hatırlatma
        Assert.Equal(new DateTime(2026, 12, 14, 7, 0, 0, DateTimeKind.Utc), notice.SendAtUtc);
    }
}

[Collection("postgres")]
public sealed class PaymentPlanFlowTests(PostgresFixture postgres)
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
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task DispatchAsync(AppFactory app, FakeEmail email)
    {
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Status == NotificationStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptUtc, DateTime.UtcNow.AddMinutes(-1))));
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email, new NoWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
    }

    [Fact]
    public async Task Plan_Collections_Reminders_AndTracking()
    {
        await using var app = postgres.CreateApp("payment_plan");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app, "Selin Koç");
        var other = await QuoteDocumentTests.NewCustomerAsync(app, "Başka Müşteri");
        await app.WithDbAsync(db => db.Customers.Where(c => c.Id == customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.Email, "selin@example.com")));
        var account = await app.WithDbAsync(db => db.FinanceAccounts.Select(x => x.Id).FirstAsync());
        var ship = Today.AddDays(20);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Daire doğramaları"), ("OrderDate", Iso(Today)), ("PlannedShipDate", Iso(ship)));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.CustomerId == customer).Select(o => o.Id).SingleAsync());

        // Hızlı plan: %30 kapora bugün, kalan sevk gününde
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("Kapora + kalan planı oluştur", details);
        await client.PostFormAsync($"/yonetim/siparisler/odeme-plani-oner/{orderId}", token, ("total", "10000"), ("depositPercent", "30"));
        var rows = await app.WithDbAsync(db => db.OrderInstallments.AsNoTracking().Where(x => x.ProductionOrderId == orderId).OrderBy(x => x.SortOrder).ToListAsync());
        Assert.Equal(new[] { ("Kapora", (DateOnly?)Today, 3000m), ("Teslimde kalan", ship, 7000m) }, rows.Select(r => (r.Label, r.DueOn, r.Amount)));
        // Hatırlatma yalnızca gelecekteki taksit için
        var reminder = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().SingleAsync(x => x.ProductionOrderId == orderId && x.Event == CustomerEvents.PaymentDue));
        Assert.Equal((ship, "selin@example.com"), (reminder.EventDate!.Value, reminder.Recipient));
        Assert.Contains(await app.WithDbAsync(db => db.OrderEvents.Where(e => e.ProductionOrderId == orderId).Select(e => e.Text).ToListAsync()), t => t.StartsWith("Ödeme planı: Kapora 3.000,00 ₺"));

        // Tahsilat formu siparişle açılır; başka müşterinin siparişine bağlanamaz
        var form = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/kasa-banka/yeni?tur=tahsilat&cari={customer}&siparis={orderId}&tutar=3000"));
        Assert.Contains($"<option value=\"{orderId}\" selected=\"selected\">", form);
        Assert.Contains("value=\"3000\"", form);
        (string, string)[] Collect(int cari, string amount) => new[] { ("tur", "tahsilat"), ("accountId", account.ToString()), ("customerId", cari.ToString()), ("date", Iso(Today)), ("amount", amount), ("orderId", orderId.ToString()) };
        var wrong = await client.PostFormAsync("/yonetim/muhasebe/kasa-banka/yeni?donus=siparis", token, Collect(other, "100"));
        Assert.Contains("bu cariye ait değil", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()));
        var paid = await client.PostFormAsync("/yonetim/muhasebe/kasa-banka/yeni?donus=siparis", token, Collect(customer, "2000"));
        Assert.Equal($"/yonetim/siparisler/detay/{orderId}#odeme", paid.Headers.Location!.OriginalString);
        details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("tahsil edilen 2.000,00 ₺", details);
        Assert.Contains("kalan 8.000,00 ₺", details);
        Assert.Contains("Kısmen ödendi", details);

        // Müşteri takip sayfasında ödemeler
        var key = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.TrackingToken!).SingleAsync());
        var tracking = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/takip/{key}"));
        foreach (var text in new[] { "Ödemeler", "10.000,00 ₺", "2.000,00 ₺", "8.000,00 ₺", "Kısmen ödendi" }) Assert.Contains(text, tracking);

        // Hatırlatma gönderim anındaki kalanla gider; banka bilgisi eklenir
        await app.WithDbAsync(async db => { var t = await db.DocumentTemplates.FirstAsync(x => x.Kind == KarakusAluminyum.Web.Models.Documents.DocumentKinds.Quote); t.BankInfo = "Örnek Bank TR00 0000"; return await db.SaveChangesAsync(); });
        await client.PostFormAsync("/yonetim/muhasebe/kasa-banka/yeni", token, Collect(customer, "1500"));
        var email = new FakeEmail();
        await DispatchAsync(app, email);
        var mail = Assert.Single(email.Sent);
        Assert.StartsWith("Ödeme hatırlatması", mail.Subject);
        foreach (var text in new[] { "teslimde kalan ödemesinin", "6.500,00 ₺", "Örnek Bank TR00 0000", $"/takip/{key}" }) Assert.Contains(text, mail.TextBody);

        // Plan değişince eski hatırlatma düşer; tamamı ödenince hatırlatma gönderilmez
        var later = ship.AddDays(5);
        await client.PostFormAsync($"/yonetim/siparisler/odeme-plani/{orderId}", token, ("rows[0].Label", "Kapora"), ("rows[0].DueOn", Iso(Today)), ("rows[0].Amount", "3000"),
            ("rows[1].Label", "Teslimde kalan"), ("rows[1].DueOn", Iso(later)), ("rows[1].Amount", "7000"), ("rows[2].Label", ""), ("rows[2].Amount", ""));
        var jobs = await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().Where(x => x.ProductionOrderId == orderId && x.Event == CustomerEvents.PaymentDue).ToListAsync());
        Assert.Single(jobs, j => j.Status == NotificationStatus.Pending && j.EventDate == later);
        await client.PostFormAsync("/yonetim/muhasebe/kasa-banka/yeni", token, Collect(customer, "6500"));
        email.Sent.Clear();
        await DispatchAsync(app, email);
        Assert.Empty(email.Sent);
        Assert.Contains(await app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().Where(x => x.ProductionOrderId == orderId && x.EventDate == later).Select(x => x.LastError).ToListAsync()), e => e == "Taksit ödendi veya plan değişti; gönderilmedi.");

        // Ödeme takibi: geciken taksit listelenir
        await client.PostFormAsync($"/yonetim/siparisler/odeme-plani/{orderId}", token, ("rows[0].Label", "Kapora"), ("rows[0].DueOn", Iso(Today.AddDays(-3))), ("rows[0].Amount", "3000"),
            ("rows[1].Label", "Ek iş"), ("rows[1].DueOn", Iso(Today.AddDays(-1))), ("rows[1].Amount", "12000"));
        var due = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/odemeler?goster=geciken"));
        Assert.Contains("Ek iş", due);
        Assert.Contains("5.000,00 ₺", due); // 15.000 plan - 10.000 tahsilat
        Assert.Contains("Gecikti", due);
    }

    [Fact]
    public async Task ExistingDatabase_GetsPaymentPlanTables()
    {
        await using (var first = postgres.CreateApp("payment_plan_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"OrderInstallments\"; ALTER TABLE \"CashTransactions\" DROP COLUMN \"ProductionOrderId\";"));
        }
        await using var second = postgres.CreateApp("payment_plan_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/odemeler")).StatusCode);
    }
}
