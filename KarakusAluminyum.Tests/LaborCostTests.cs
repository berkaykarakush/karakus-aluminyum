using System.Net;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KarakusAluminyum.Tests;

// Öneri 7.2: takvimdeki adam-gün × günlük maliyet; raporda yalnızca yetkili roller görür, proje analizine işçilik olarak girer.
[Collection("postgres")]
public sealed class LaborCostTests(PostgresFixture postgres)
{
    [Fact]
    public void DailyCost_UsesCardValue_OrEstimatesFromSalary()
    {
        Assert.Equal((2000m, false), WorkStats.DailyCost(new Employee { DailyCost = 2000, NetSalary = 52000 }));
        Assert.Equal((1000m, true), WorkStats.DailyCost(new Employee { NetSalary = 26000 }));
    }

    [Fact]
    public async Task Report_And_ProjectAnalysis_IncludeLabor()
    {
        await using var app = postgres.CreateApp("labor_cost");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var day = new DateOnly(2026, 10, 5);
        var (projectId, orderId) = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "T-LAB-1", Title = "Villa", CustomerId = customer, Status = QuoteStatus.Accepted, TaxRate = 20 };
            db.Quotes.Add(quote);
            var usta = new Employee { Name = "Usta", DailyCost = 2000 };
            var kalfa = new Employee { Name = "Kalfa", NetSalary = 26000 };
            db.Employees.AddRange(usta, kalfa);
            await db.SaveChangesAsync();
            var order = new ProductionOrder { OrderNumber = "SP-LAB-1", CustomerId = customer, QuoteId = quote.Id, Title = "Villa", OrderDate = day };
            var project = new CostProject { Name = "Villa projesi", CustomerId = customer, QuoteId = quote.Id };
            db.AddRange(order, project);
            await db.SaveChangesAsync();
            db.WorkLogs.AddRange(
                new WorkLog { EmployeeId = usta.Id, ProductionOrderId = order.Id, Date = day, Days = 1 },
                new WorkLog { EmployeeId = usta.Id, ProductionOrderId = order.Id, Date = day.AddDays(1), Days = 0.5m },
                new WorkLog { EmployeeId = kalfa.Id, ProductionOrderId = order.Id, Date = day, Days = 1 },
                new WorkLog { EmployeeId = kalfa.Id, ProductionOrderId = order.Id, Date = KarakusAluminyum.Web.Services.Documents.PrintDates.Today.AddDays(10), Days = 1 }); // plan: maliyete girmez
            await db.SaveChangesAsync();
            return (project.Id, order.Id);
        });

        var report = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/takvim/rapor?bas=2026-10-01&bit={KarakusAluminyum.Web.Services.Documents.PrintDates.Today.AddDays(30):yyyy-MM-dd}"));
        Assert.Contains("4.000,00 ₺ *", report); // 1,5 × 2000 + 1 × 1000 (tahmini)
        Assert.Contains("net maaş ÷ 26", report);
        Assert.Contains("işçilik 4.000,00 ₺ (tahmini)", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}")));

        var analysis = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/projeler/detay/{projectId}"));
        Assert.Contains("İşçilik (takvim)", analysis);
        Assert.Contains("1,5 adam-gün × 2.000,00 TL", analysis);
        Assert.Contains("1 adam-gün × 1.000,00 TL (net maaştan tahmini)", analysis);
        Assert.Contains("4.000,00", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/muhasebe/projeler")));

        // Editör takvimi kullanır ama maaştan gelen maliyeti görmez
        var email = $"{Guid.NewGuid():N}@test.local";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Editor")).Succeeded);
        }
        var editor = app.NewClient();
        await editor.LoginAsync(email);
        var editorReport = WebUtility.HtmlDecode(await editor.GetStringAsync("/yonetim/takvim/rapor?bas=2026-10-01&bit=2026-10-31"));
        Assert.Contains("Usta", editorReport);
        Assert.DoesNotContain("İşçilik", editorReport);
        Assert.DoesNotContain("İşçilik maliyeti", await (await editor.GetAsync("/yonetim/takvim/rapor?bas=2026-10-01&bit=2026-10-31&csv=true")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("işçilik 4.000", WebUtility.HtmlDecode(await editor.GetStringAsync($"/yonetim/siparisler/detay/{orderId}")));
    }
}
