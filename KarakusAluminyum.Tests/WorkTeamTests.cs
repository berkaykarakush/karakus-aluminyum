using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Öneri 7.5: montaj ekipleri (taşeron dahil) takvimde; ekip kaydı kişi sayısı kadar adam-gün, maliyeti ekip günlük ücreti.
[Collection("postgres")]
public sealed class WorkTeamTests(PostgresFixture postgres)
{
    [Fact]
    public void TeamLog_CountsPeople_AndTeamDailyCost()
    {
        var team = new Installer { Id = 4, Name = "Ahmet ekibi", TeamSize = 3, DailyCost = 6000 };
        var usta = new Employee { Id = 1, Name = "Usta", DailyCost = 2000 };
        var order = new ProductionOrder { Id = 9, OrderNumber = "SP-9", Title = "İş" };
        var day = new DateOnly(2026, 11, 2);
        var logs = new[]
        {
            new WorkLog { InstallerId = 4, Installer = team, People = 3, Date = day, Days = 1, ProductionOrder = order, ProductionOrderId = 9 },
            new WorkLog { InstallerId = 4, Installer = team, People = 3, Date = day.AddDays(1), Days = 0.5m, ProductionOrder = order, ProductionOrderId = 9 },
            new WorkLog { EmployeeId = 1, Employee = usta, People = 1, Date = day, Days = 1, ProductionOrder = order, ProductionOrderId = 9 }
        };
        var job = WorkStats.WithCost(WorkStats.ByProject(logs, day.AddDays(5)), logs, day.AddDays(5)).Single();
        Assert.Equal((5.5m, 4, 2), (job.ManDays, job.People, job.WorkDays)); // 1,5 × 3 + 1; 3 + 1 kişi
        Assert.Equal(11000m, job.LaborCost); // 1,5 ekip günü × 6000 + 1 × 2000
        Assert.False(job.CostEstimated);
        Assert.Contains(("Ahmet ekibi (3 kişi)", 4.5m), job.Crew);
        var rows = WorkStats.ByEmployee(logs, day.AddDays(5));
        Assert.Equal(("Ahmet ekibi", true, 4.5m), (rows.Last().Name, rows.Last().IsTeam, rows.Last().ManDays)); // ekipler personelden sonra
    }

    [Fact]
    public async Task Teams_AppearOnCalendar_AndInProjectLabor()
    {
        await using var app = postgres.CreateApp("work_teams");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/montajcilar/ekle", token, ("Name", "Taşeron Mehmet"), ("TeamSize", "3"), ("IsSubcontractor", "true"), ("IsActive", "true"), ("DailyCost", "6000"));
        var teamId = await app.WithDbAsync(db => db.Installers.Where(i => i.Name == "Taşeron Mehmet").Select(i => i.Id).SingleAsync());
        Assert.Equal(6000m, await app.WithDbAsync(db => db.Installers.Where(i => i.Id == teamId).Select(i => i.DailyCost).SingleAsync()));
        var (orderId, projectId) = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "T-TEAM-1", Title = "Site", CustomerId = customer, Status = QuoteStatus.Accepted, TaxRate = 20 };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            var order = new ProductionOrder { OrderNumber = "SP-TEAM-1", CustomerId = customer, QuoteId = quote.Id, Title = "Site", Status = OrderStatus.InProduction, OrderDate = new DateOnly(2026, 10, 1) };
            var project = new CostProject { Name = "Site projesi", CustomerId = customer, QuoteId = quote.Id };
            db.AddRange(order, project);
            await db.SaveChangesAsync();
            return (order.Id, project.Id);
        });

        var monday = new DateOnly(2026, 10, 5);
        await client.PostFormAsync("/yonetim/takvim/ekle", token, ("InstallerIds", teamId.ToString()), ("From", monday.ToString("yyyy-MM-dd")), ("To", monday.AddDays(1).ToString("yyyy-MM-dd")),
            ("ProductionOrderId", orderId.ToString()), ("Activity", WorkActivities.Installation), ("Days", "1"));
        var logs = await app.WithDbAsync(db => db.WorkLogs.AsNoTracking().Where(w => w.InstallerId == teamId).ToListAsync());
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal((3, (int?)null), (l.People, l.EmployeeId)));

        // Ekip büyüklüğü sonradan değişse de geçmiş kayıt değişmez
        await client.PostFormAsync($"/yonetim/siparisler/montajcilar/duzenle/{teamId}", token, ("Name", "Taşeron Mehmet"), ("TeamSize", "5"), ("IsSubcontractor", "true"), ("IsActive", "true"), ("DailyCost", "6000"));
        var week = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/takvim?hafta={monday:yyyy-MM-dd}"));
        Assert.Contains("Taşeron Mehmet", week);
        Assert.Contains("Taşeron · 5 kişi", week);
        Assert.Contains("SP-TEAM-1", week);
        Assert.Contains($"ekip={teamId}", week);

        var report = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/takvim/rapor?bas=2026-10-01&bit=2026-10-31"));
        Assert.Contains("<strong>6</strong>", report); // 2 gün × 3 kişi
        Assert.Contains("12.000,00 ₺", report);      // 2 ekip günü × 6000
        Assert.Contains("(ekip)", report);

        var analysis = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/projeler/detay/{projectId}"));
        Assert.Contains("2 ekip günü (3 kişi) × 6.000,00 TL", analysis);
    }
}
