using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README madde 5: personel takvimi ve iş başına adam-gün metrikleri.
[Collection("postgres")]
public sealed class WorkCalendarTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = KarakusAluminyum.Web.Services.Documents.PrintDates.Today;

    [Fact]
    public void Stats_SplitDoneAndPlanned_AndCountPeopleAndDays()
    {
        var order = new ProductionOrder { Id = 5, OrderNumber = "SP-1", Title = "İş" };
        var ali = new Employee { Id = 1, Name = "Ali" };
        var veli = new Employee { Id = 2, Name = "Veli" };
        var day = new DateOnly(2026, 11, 2);
        WorkLog L(Employee e, DateOnly d, decimal days = 1, ProductionOrder? o = null, string activity = WorkActivities.Installation)
            => new() { Employee = e, EmployeeId = e.Id, Date = d, Days = days, ProductionOrder = o, ProductionOrderId = o?.Id, Activity = activity };
        var logs = new[] { L(ali, day, o: order), L(veli, day, o: order), L(ali, day.AddDays(1), 0.5m, order, WorkActivities.Production), L(ali, day.AddDays(1), 0.75m), L(veli, day.AddDays(10), o: order) };

        var rows = WorkStats.ByProject(logs, day.AddDays(5));
        var job = rows.Single(r => r.Key == "o5");
        Assert.Equal((2.5m, 1m, 2, 2), (job.ManDays, job.PlannedManDays, job.People, job.WorkDays));
        Assert.Equal((day, day.AddDays(1)), (job.First!.Value, job.Last!.Value));
        Assert.Equal(0.5m, job.ByActivity[WorkActivities.Production]);
        Assert.Equal(("Ali", 1.5m), job.Crew[0]);
        Assert.Equal(0.75m, rows.Single(r => r.Key == "general").ManDays);
        Assert.Equal(2.25m, WorkStats.ByEmployee(logs, day.AddDays(5)).Single(e => e.Name == "Ali").ManDays);
        Assert.Equal(("e1", day.AddDays(1)), WorkStats.Overbooked(logs).Single()); // Ali o gün 1,25 gün
        Assert.Equal(new DateOnly(2026, 11, 23), WorkStats.WeekStart(new DateOnly(2026, 11, 29))); // Pazar → o haftanın Pazartesisi
    }

    [Fact]
    public async Task Calendar_Entries_Metrics_AndOrderPanel()
    {
        await using var app = postgres.CreateApp("work_calendar");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var (ali, veli) = await app.WithDbAsync(async db =>
        {
            var a = new Employee { Name = "Ali Usta", Position = "Montaj" };
            var v = new Employee { Name = "Veli Kalfa" };
            db.Employees.AddRange(a, v, new Employee { Name = "Eski Personel", IsActive = false });
            await db.SaveChangesAsync();
            return (a.Id, v.Id);
        });
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Takvim işi"), ("OrderDate", Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Takvim işi").Select(o => o.Id).SingleAsync());
        var order = await app.WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId));

        var monday = WorkStats.WeekStart(Today).AddDays(-7); // geçen hafta: tamamı gerçekleşmiş
        (string, string)[] Form(params (string, string)[] extra) => new[] { ("EmployeeIds", ali.ToString()), ("EmployeeIds", veli.ToString()), ("From", monday.ToString("yyyy-MM-dd")), ("To", monday.AddDays(6).ToString("yyyy-MM-dd")),
            ("SkipSundays", "true"), ("ProductionOrderId", orderId.ToString()), ("Activity", WorkActivities.Installation), ("Days", "1") }.Concat(extra).ToArray();

        Assert.Contains("En az bir personel veya montaj ekibi seçin", WebUtility.HtmlDecode(await (await client.PostFormAsync("/yonetim/takvim/ekle", token, ("From", "2026-01-02"), ("Activity", "installation"), ("Days", "1"))).Content.ReadAsStringAsync()));
        Assert.Contains("Bitiş tarihi başlangıçtan önce olamaz", WebUtility.HtmlDecode(await (await client.PostFormAsync("/yonetim/takvim/ekle", token, ("EmployeeIds", ali.ToString()), ("From", "2026-01-05"), ("To", "2026-01-02"), ("Activity", "installation"), ("Days", "1"))).Content.ReadAsStringAsync()));
        Assert.Contains("Süre tam gün", WebUtility.HtmlDecode(await (await client.PostFormAsync("/yonetim/takvim/ekle", token, ("EmployeeIds", ali.ToString()), ("From", "2026-01-05"), ("Activity", "installation"), ("Days", "3"))).Content.ReadAsStringAsync()));

        await client.PostFormAsync("/yonetim/takvim/ekle", token, Form());
        Assert.Equal(12, await app.WithDbAsync(db => db.WorkLogs.CountAsync(w => w.ProductionOrderId == orderId))); // 2 kişi × 6 gün (Pazar atlandı)
        await client.PostFormAsync("/yonetim/takvim/ekle", token, Form()); // aynı kayıtlar tekrar eklenmez
        Assert.Equal(12, await app.WithDbAsync(db => db.WorkLogs.CountAsync(w => w.ProductionOrderId == orderId)));
        // Aynı gün başka işe yarım gün: aşırı yükleme uyarısı
        await client.PostFormAsync("/yonetim/takvim/ekle", token, ("EmployeeIds", ali.ToString()), ("From", monday.ToString("yyyy-MM-dd")), ("Activity", WorkActivities.Workshop), ("Days", "0.5"), ("Note", "Atölye düzeni"));
        // Gelecek hafta: plan
        await client.PostFormAsync("/yonetim/takvim/ekle", token, ("EmployeeIds", veli.ToString()), ("From", Today.AddDays(14).ToString("yyyy-MM-dd")), ("ProductionOrderId", orderId.ToString()), ("Activity", WorkActivities.Service), ("Days", "1"));

        var week = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/takvim?hafta={monday:yyyy-MM-dd}"));
        Assert.Contains("Ali Usta", week);
        Assert.DoesNotContain("Eski Personel", week); // pasif ve kaydı olmayan personel görünmez
        Assert.Contains(order.OrderNumber, week);
        Assert.Contains("Atölye düzeni", week);
        Assert.Contains("is-over", week);

        var report = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/takvim/rapor?bas={monday:yyyy-MM-dd}&bit={Today.AddDays(30):yyyy-MM-dd}"));
        Assert.Contains("<strong>12</strong>", report);   // iş: 12 adam-gün gerçekleşen
        Assert.Contains("Ali Usta 6, Veli Kalfa 6", report);
        Assert.Contains("<strong>6,5</strong>", report);  // Ali: 6 + 0,5
        Assert.Contains("1 kişi-günde", report);
        var csv = await client.GetAsync($"/yonetim/takvim/rapor?bas={monday:yyyy-MM-dd}&bit={Today.AddDays(30):yyyy-MM-dd}&csv=true");
        var csvText = await csv.Content.ReadAsStringAsync();
        Assert.Contains($"{order.OrderNumber} · Takvim işi;12;1;2;6;", csvText);
        Assert.Contains("Ali Usta;6,5;0;2;6", csvText);

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("12 adam-gün", details);
        Assert.Contains("2 kişi · 6 gün", details);
        Assert.Contains("planlanan 1 adam-gün", details);

        var one = await app.WithDbAsync(db => db.WorkLogs.Where(w => w.EmployeeId == ali && w.Activity == WorkActivities.Workshop).Select(w => w.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/takvim/sil/{one}", token);
        Assert.False(await app.WithDbAsync(db => db.WorkLogs.AnyAsync(w => w.Id == one)));
    }
}
