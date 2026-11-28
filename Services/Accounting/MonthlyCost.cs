using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Accounting;

// Aylık net maliyet raporu: personel, taşeron ekipleri, sabit giderler, gider kayıtları ve alışlar tek tabloda. Tutarlar KDV hariç TL'dir.
// Aktif personel ay boyunca maliyettir (günlük maliyet × 26 iş günü; günlük maliyet girilmemişse net maaş), işe ay içinde başlayan
// kısmi ay olarak sayılır. Pasif personel yalnızca takvimdeki günleri kadar sayılır. Kasadan ödenen maaş bilgi içindir, toplama girmez
// (personel maliyetiyle çift sayılmasın diye).
public sealed record MonthlyStaffRow(int Id, string Name, string? Position, bool IsActive, decimal CalendarDays, decimal DailyCost, bool Estimated, decimal MonthFactor, decimal Cost);
public sealed record MonthlyTeamRow(string Name, bool IsSubcontractor, decimal Days, decimal DailyCost, bool Estimated, decimal Cost);
public sealed record MonthlyFixedRow(int Id, string Name, string? Category, decimal Planned, decimal? Actual, string? Note)
{
    // Kategoride gerçekleşen gider varsa o kullanılır, yoksa tanımlı aylık tutar.
    public decimal Cost => Actual is decimal a && a > 0 ? a : Planned;
}
public sealed record MonthlyExpenseRow(string Category, int Count, decimal Amount);

public sealed record MonthlyCostReport(
    DateOnly Month, bool IncludeTeams,
    IReadOnlyList<MonthlyStaffRow> Staff, IReadOnlyList<MonthlyTeamRow> Teams, IReadOnlyList<MonthlyFixedRow> Fixed, IReadOnlyList<MonthlyExpenseRow> Expenses,
    decimal Purchases, decimal PurchaseReturns, decimal Sales, decimal SaleReturns, decimal SalariesPaid)
{
    public decimal StaffTotal => Staff.Sum(x => x.Cost);
    public decimal TeamTotal => Teams.Sum(x => x.Cost);
    public decimal FixedTotal => Fixed.Sum(x => x.Cost);
    public decimal ExpenseTotal => Expenses.Sum(x => x.Amount);
    public decimal PurchaseNet => Purchases - PurchaseReturns;
    public decimal Total => StaffTotal + (IncludeTeams ? TeamTotal : 0) + FixedTotal + ExpenseTotal + PurchaseNet;
    public decimal SalesNet => Sales - SaleReturns;
    public decimal Result => SalesNet - Total;
    public int DaysInMonth => DateTime.DaysInMonth(Month.Year, Month.Month);
    public decimal DailyAverage => InvoiceRules.Money(Total / DaysInMonth);
}

public sealed record MonthlyCostData(
    IReadOnlyList<Employee> Employees, IReadOnlyList<WorkLog> WorkLogs, IReadOnlyList<FixedCost> FixedCosts,
    IReadOnlyList<(DateOnly Date, int CategoryId, string Category, decimal Net)> Expenses,
    IReadOnlyList<(DateOnly Date, InvoiceKind Kind, decimal NetTry)> Invoices, IReadOnlyList<(DateOnly Date, decimal Amount)> Salaries);

public static class MonthlyCost
{
    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);
    public static DateOnly EndOf(DateOnly month) => month.AddMonths(1).AddDays(-1);

    // Aktif personelin o aydaki payı: işe ay içinde başladıysa kalan günler, ay başlamadan önce başladıysa tam ay.
    public static decimal MonthFactor(Employee employee, DateOnly month)
    {
        if (employee.StartDate is not DateOnly start || start <= month) return 1m;
        var end = EndOf(month);
        if (start > end) return 0m;
        return decimal.Round((end.Day - start.Day + 1) / (decimal)end.Day, 4);
    }

    // Personelin aylık maliyeti: günlük maliyet × 26 iş günü; günlük maliyet girilmemişse net maaş (tahmini).
    public static (decimal Monthly, decimal Daily, bool Estimated) MonthlyCostOf(Employee employee)
        => employee.DailyCost > 0 ? (employee.DailyCost * WorkStats.WorkDaysPerMonth, employee.DailyCost, false) : (employee.NetSalary, WorkStats.DailyCost(employee).Cost, true);

    public static MonthlyCostReport Build(DateOnly month, MonthlyCostData data, DateOnly today, bool includeTeams)
    {
        month = MonthOf(month);
        var end = EndOf(month);
        bool InMonth(DateOnly d) => d >= month && d <= end;
        var logs = data.WorkLogs.Where(x => InMonth(x.Date)).ToList();

        var staff = new List<MonthlyStaffRow>();
        foreach (var e in data.Employees)
        {
            var days = logs.Where(x => x.EmployeeId == e.Id && x.Date <= today).Sum(x => x.Days);
            var (monthly, daily, estimated) = MonthlyCostOf(e);
            if (e.IsActive)
            {
                var factor = MonthFactor(e, month);
                if (factor == 0) continue;
                staff.Add(new MonthlyStaffRow(e.Id, e.Name, e.Position, true, days, daily, estimated, factor, InvoiceRules.Money(monthly * factor)));
            }
            else if (days > 0)
                staff.Add(new MonthlyStaffRow(e.Id, e.Name, e.Position, false, days, daily, estimated, 0, InvoiceRules.Money(days * daily)));
        }

        // Montaj ekipleri ve taşeronlar: takvimde gerçekleşen günler × ekibin günlük ücreti.
        var teams = logs.Where(x => x.InstallerId is not null).GroupBy(x => x.InstallerId!.Value).Select(g =>
        {
            var installer = g.First().Installer;
            var (cost, estimated) = WorkStats.Labor(g, today);
            return new MonthlyTeamRow(installer?.Name ?? "Ekip", installer?.IsSubcontractor ?? false, g.Where(x => x.Date <= today).Sum(x => x.Days), installer?.DailyCost ?? 0, estimated, cost);
        }).Where(x => x.Days > 0).OrderBy(x => x.Name).ToList();

        var expenses = data.Expenses.Where(x => InMonth(x.Date)).ToList();
        var fixedRows = data.FixedCosts.Where(x => x.AppliesTo(month)).OrderBy(x => x.Name).Select(f =>
        {
            decimal? actual = f.CategoryId is int c ? expenses.Where(x => x.CategoryId == c).Sum(x => x.Net) : null;
            return new MonthlyFixedRow(f.Id, f.Name, f.Category?.Name, f.MonthlyAmount, actual, f.Note);
        }).ToList();
        // Sabit gidere bağlı kategorinin kayıtları sabit gider satırında sayıldı; diğer giderlere tekrar girmez.
        var linked = data.FixedCosts.Where(x => x.AppliesTo(month) && x.CategoryId is not null).Select(x => x.CategoryId!.Value).ToHashSet();
        var expenseRows = expenses.Where(x => !linked.Contains(x.CategoryId)).GroupBy(x => x.Category)
            .Select(g => new MonthlyExpenseRow(g.Key, g.Count(), g.Sum(x => x.Net))).OrderByDescending(x => x.Amount).ToList();

        var invoices = data.Invoices.Where(x => InMonth(x.Date)).ToList();
        decimal Sum(InvoiceKind kind) => invoices.Where(x => x.Kind == kind).Sum(x => x.NetTry);
        return new MonthlyCostReport(month, includeTeams,
            staff.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToList(), teams, fixedRows, expenseRows,
            Sum(InvoiceKind.Purchase), Sum(InvoiceKind.PurchaseReturn), Sum(InvoiceKind.Sale), Sum(InvoiceKind.SaleReturn),
            data.Salaries.Where(x => InMonth(x.Date)).Sum(x => x.Amount));
    }

    public static async Task<MonthlyCostData> LoadAsync(AppDbContext db, DateOnly from, DateOnly to)
    {
        var employees = await db.Employees.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var logs = await db.WorkLogs.AsNoTracking().Include(x => x.Installer).Where(x => x.Date >= from && x.Date <= to).ToListAsync();
        foreach (var log in logs.Where(x => x.EmployeeId is not null)) log.Employee = employees.FirstOrDefault(e => e.Id == log.EmployeeId);
        var fixedCosts = await db.FixedCosts.AsNoTracking().Include(x => x.Category).ToListAsync();
        var expenses = (await db.Expenses.AsNoTracking().Where(x => !x.IsCancelled && x.Date >= from && x.Date <= to)
            .Select(x => new { x.Date, x.CategoryId, Category = x.Category!.Name, x.Net }).ToListAsync())
            .Select(x => (x.Date, x.CategoryId, x.Category, x.Net)).ToList();
        var invoices = (await db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved && x.Date >= from && x.Date <= to)
            .Select(x => new { x.Date, x.Kind, x.Net, x.ExchangeRate }).ToListAsync())
            .Select(x => (x.Date, x.Kind, InvoiceRules.Money(x.Net * x.ExchangeRate))).ToList();
        var salaries = (await db.CashTransactions.AsNoTracking().Where(x => !x.IsCancelled && x.Kind == CashKind.Salary && x.Date >= from && x.Date <= to)
            .Select(x => new { x.Date, x.AmountTry }).ToListAsync()).Select(x => (x.Date, x.AmountTry)).ToList();
        return new MonthlyCostData(employees, logs, fixedCosts, expenses, invoices, salaries);
    }
}
