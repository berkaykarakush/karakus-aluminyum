using KarakusAluminyum.Web.Models.Production;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record WorkProjectRow(string Key, string Label, string? Url, decimal ManDays, decimal PlannedManDays, int People, int WorkDays, DateOnly? First, DateOnly? Last,
    IReadOnlyDictionary<string, decimal> ByActivity, IReadOnlyList<(string Name, decimal Days)> Crew, decimal LaborCost = 0, bool CostEstimated = false);
public sealed record WorkEmployeeRow(int EmployeeId, string Name, decimal ManDays, decimal PlannedManDays, int Projects, int WorkDays, IReadOnlyDictionary<string, decimal> ByActivity, decimal LaborCost = 0, bool CostEstimated = false);

// Takvim metrikleri: iş başına adam-gün, kişi ve gün sayısı; personel başına toplam gün.
// Bugün ve öncesi gerçekleşen, sonrası planlanan sayılır.
public static class WorkStats
{
    public static (string Key, string Label, string? Url) Target(WorkLog log) => log switch
    {
        { ProductionOrder: { } o } => ($"o{o.Id}", $"{o.OrderNumber} · {o.Title}", $"/yonetim/siparisler/detay/{o.Id}"),
        { ServiceRequest: { } s } => ($"s{s.Id}", $"{s.Number} · Servis", $"/yonetim/servis/detay/{s.Id}"),
        _ => ("general", "Genel (işe bağlı değil)", null)
    };

    public static string ShortLabel(WorkLog log) => log switch
    {
        { ProductionOrder: { } o } => o.OrderNumber,
        { ServiceRequest: { } s } => s.Number,
        _ => WorkActivities.Label(log.Activity)
    };

    public static List<WorkProjectRow> ByProject(IEnumerable<WorkLog> logs, DateOnly today)
        => logs.GroupBy(Target).Select(g =>
        {
            var done = g.Where(x => x.Date <= today).ToList();
            return new WorkProjectRow(g.Key.Key, g.Key.Label, g.Key.Url, done.Sum(x => x.Days), g.Where(x => x.Date > today).Sum(x => x.Days),
                done.Select(x => x.EmployeeId).Distinct().Count(), done.Select(x => x.Date).Distinct().Count(),
                done.Count == 0 ? null : done.Min(x => x.Date), done.Count == 0 ? null : done.Max(x => x.Date),
                done.GroupBy(x => x.Activity).ToDictionary(a => a.Key, a => a.Sum(x => x.Days)),
                done.GroupBy(x => x.Employee?.Name ?? $"#{x.EmployeeId}").Select(e => (e.Key, e.Sum(x => x.Days))).OrderByDescending(x => x.Item2).ToList());
        }).OrderByDescending(x => x.ManDays).ThenByDescending(x => x.PlannedManDays).ToList();

    public static List<WorkEmployeeRow> ByEmployee(IEnumerable<WorkLog> logs, DateOnly today)
        => logs.GroupBy(x => (x.EmployeeId, Name: x.Employee?.Name ?? $"#{x.EmployeeId}")).Select(g =>
        {
            var done = g.Where(x => x.Date <= today).ToList();
            return new WorkEmployeeRow(g.Key.EmployeeId, g.Key.Name, done.Sum(x => x.Days), g.Where(x => x.Date > today).Sum(x => x.Days),
                done.Select(x => Target(x).Key).Distinct().Count(), done.Select(x => x.Date).Distinct().Count(),
                done.GroupBy(x => x.Activity).ToDictionary(a => a.Key, a => a.Sum(x => x.Days)));
        }).OrderBy(x => x.Name).ToList();

    // Aynı gün bir kişiye 1 günden fazla iş yazılmışsa uyarı verilir.
    public static HashSet<(int EmployeeId, DateOnly Date)> Overbooked(IEnumerable<WorkLog> logs)
        => logs.GroupBy(x => (x.EmployeeId, x.Date)).Where(g => g.Sum(x => x.Days) > 1).Select(g => g.Key).ToHashSet();

    public const decimal WorkDaysPerMonth = 26;

    // Personelin günlük maliyeti: kartta girilen değer, yoksa net maaştan tahmin.
    public static (decimal Cost, bool Estimated) DailyCost(KarakusAluminyum.Web.Models.Accounting.Employee? employee)
        => employee is null ? (0, true) : employee.DailyCost > 0 ? (employee.DailyCost, false) : (decimal.Round(employee.NetSalary / WorkDaysPerMonth, 2), true);

    // Gerçekleşen (bugüne kadar) kayıtların işçilik maliyeti.
    public static (decimal Cost, bool Estimated) Labor(IEnumerable<WorkLog> logs, DateOnly today)
    {
        var done = logs.Where(x => x.Date <= today).ToList();
        var parts = done.Select(x => (Days: x.Days, Rate: DailyCost(x.Employee))).ToList();
        return (decimal.Round(parts.Sum(x => x.Days * x.Rate.Cost), 2), parts.Any(x => x.Rate.Estimated));
    }

    public static List<WorkProjectRow> WithCost(List<WorkProjectRow> rows, IEnumerable<WorkLog> logs, DateOnly today)
    {
        var groups = logs.GroupBy(x => Target(x).Key).ToDictionary(g => g.Key, g => Labor(g, today));
        return rows.Select(r => groups.TryGetValue(r.Key, out var c) ? r with { LaborCost = c.Cost, CostEstimated = c.Estimated } : r).ToList();
    }

    public static List<WorkEmployeeRow> WithCost(List<WorkEmployeeRow> rows, IEnumerable<WorkLog> logs, DateOnly today)
    {
        var groups = logs.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => Labor(g, today));
        return rows.Select(r => groups.TryGetValue(r.EmployeeId, out var c) ? r with { LaborCost = c.Cost, CostEstimated = c.Estimated } : r).ToList();
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
