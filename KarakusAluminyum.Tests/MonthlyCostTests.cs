using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Accounting;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class MonthlyCostTests
{
    private static readonly DateOnly Month = new(2026, 11, 1);
    private static readonly DateOnly Today = new(2026, 11, 28);

    private static MonthlyCostData Data(IReadOnlyList<Employee>? employees = null, IReadOnlyList<WorkLog>? logs = null, IReadOnlyList<FixedCost>? fixedCosts = null,
        IReadOnlyList<(DateOnly, int, string, decimal)>? expenses = null, IReadOnlyList<(DateOnly, InvoiceKind, decimal)>? invoices = null)
        => new(employees ?? Array.Empty<Employee>(), logs ?? Array.Empty<WorkLog>(), fixedCosts ?? Array.Empty<FixedCost>(),
            expenses ?? Array.Empty<(DateOnly, int, string, decimal)>(), invoices ?? Array.Empty<(DateOnly, InvoiceKind, decimal)>(), Array.Empty<(DateOnly, decimal)>());

    [Fact]
    public void Staff_ActiveFullMonth_PartialStart_InactiveByCalendar()
    {
        var full = new Employee { Id = 1, Name = "A", DailyCost = 1500m, IsActive = true };
        var salaryOnly = new Employee { Id = 2, Name = "B", NetSalary = 26000m, IsActive = true };
        var late = new Employee { Id = 3, Name = "C", DailyCost = 1000m, IsActive = true, StartDate = new DateOnly(2026, 11, 16) };
        var gone = new Employee { Id = 4, Name = "D", DailyCost = 1200m, IsActive = false };
        var logs = new[] { new WorkLog { EmployeeId = 4, Employee = gone, Date = new DateOnly(2026, 11, 3), Days = 1 }, new WorkLog { EmployeeId = 4, Employee = gone, Date = new DateOnly(2026, 11, 4), Days = 0.5m } };
        var report = MonthlyCost.Build(Month, Data(new[] { full, salaryOnly, late, gone }, logs), Today, true);
        var cost = report.Staff.ToDictionary(x => x.Name, x => x.Cost);
        Assert.Equal(39000m, cost["A"]);
        Assert.Equal(26000m, cost["B"]);
        Assert.Equal(13000m, cost["C"]); // 15/30 gün × 26.000
        Assert.Equal(1800m, cost["D"]);
    }

    [Fact]
    public void FixedCost_UsesActualExpenseOfLinkedCategory_AndExcludesItFromExpenses()
    {
        var category = new ExpenseCategory { Id = 5, Name = "Elektrik" };
        var fixedCosts = new[]
        {
            new FixedCost { Id = 1, Name = "Elektrik", MonthlyAmount = 4000m, CategoryId = 5, Category = category, StartMonth = new DateOnly(2026, 1, 1) },
            new FixedCost { Id = 2, Name = "Kira", MonthlyAmount = 30000m, StartMonth = new DateOnly(2026, 1, 1) },
            new FixedCost { Id = 3, Name = "Eski kredi", MonthlyAmount = 9000m, StartMonth = new DateOnly(2025, 1, 1), EndMonth = new DateOnly(2026, 10, 1) }
        };
        var expenses = new[] { (new DateOnly(2026, 11, 10), 5, "Elektrik", 4650m), (new DateOnly(2026, 11, 12), 7, "Yakıt", 2000m), (new DateOnly(2026, 10, 12), 7, "Yakıt", 999m) };
        var invoices = new[] { (new DateOnly(2026, 11, 2), InvoiceKind.Purchase, 10000m), (new DateOnly(2026, 11, 3), InvoiceKind.PurchaseReturn, 1000m), (new DateOnly(2026, 11, 5), InvoiceKind.Sale, 90000m) };
        var report = MonthlyCost.Build(Month, Data(fixedCosts: fixedCosts, expenses: expenses, invoices: invoices), Today, true);
        Assert.Equal(new[] { ("Elektrik", 4650m), ("Kira", 30000m) }, report.Fixed.Select(x => (x.Name, x.Cost)));
        Assert.Equal(2000m, Assert.Single(report.Expenses).Amount);
        Assert.Equal(9000m, report.PurchaseNet);
        Assert.Equal(4650m + 30000m + 2000m + 9000m, report.Total);
        Assert.Equal(90000m - report.Total, report.Result);
    }

    [Fact]
    public void FixedCost_WithoutActualExpense_UsesPlannedAmount()
    {
        var fixedCost = new FixedCost { Id = 1, Name = "Muhasebeci", MonthlyAmount = 5000m, CategoryId = 9, StartMonth = Month };
        Assert.Equal(5000m, MonthlyCost.Build(Month, Data(fixedCosts: new[] { fixedCost }), Today, true).FixedTotal);
    }

    [Fact]
    public void Teams_CountedOnlyWhenIncluded_AndOnlyPastDays()
    {
        var team = new Installer { Id = 1, Name = "Taşeron", DailyCost = 3000m, IsSubcontractor = true };
        var logs = new[]
        {
            new WorkLog { InstallerId = 1, Installer = team, Date = new DateOnly(2026, 11, 20), Days = 1, People = 2 },
            new WorkLog { InstallerId = 1, Installer = team, Date = new DateOnly(2026, 11, 30), Days = 1, People = 2 } // plan, henüz gerçekleşmedi
        };
        var included = MonthlyCost.Build(Month, Data(logs: logs), Today, true);
        Assert.Equal(3000m, included.TeamTotal);
        Assert.Equal(3000m, included.Total);
        Assert.Equal(0m, MonthlyCost.Build(Month, Data(logs: logs), Today, false).Total);
    }
}
