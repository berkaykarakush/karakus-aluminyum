using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record WorkProjectRow(string Key, string Label, string? Url, decimal ManDays, decimal PlannedManDays, int People, int WorkDays, DateOnly? First, DateOnly? Last,
    IReadOnlyDictionary<string, decimal> ByActivity, IReadOnlyList<(string Name, decimal Days)> Crew, decimal LaborCost = 0, bool CostEstimated = false);
// Personel veya montaj ekibi satırı. Key: "e{personel}" ya da "t{ekip}".
public sealed record WorkEmployeeRow(string Key, string Name, bool IsTeam, decimal ManDays, decimal PlannedManDays, int Projects, int WorkDays, IReadOnlyDictionary<string, decimal> ByActivity, decimal LaborCost = 0, bool CostEstimated = false);

// Takvim metrikleri: iş başına adam-gün, kişi ve gün sayısı; personel / ekip başına toplam gün.
// Bugün ve öncesi gerçekleşen, sonrası planlanan sayılır. Ekip kaydının adam-günü gün × ekipteki kişi sayısıdır.
public static class WorkStats
{
    public const decimal WorkDaysPerMonth = 26;

    public static (string Key, string Label, string? Url) Target(WorkLog log) => log switch
    {
        { ProductionOrder: { } o } => ($"o{o.Id}", $"{o.OrderNumber} · {o.Title}", $"/yonetim/siparisler/detay/{o.Id}"),
        { ServiceRequest: { } s } => ($"s{s.Id}", $"{s.Number} · Servis", $"/yonetim/servis/detay/{s.Id}"),
        _ => ("general", "Genel (işe bağlı değil)", null)
    };

    public static (string Key, string Name, bool IsTeam) Worker(WorkLog log) => log.InstallerId is int team
        ? ($"t{team}", (log.Installer?.Name ?? $"Ekip #{team}") + $" ({log.People} kişi)", true)
        : ($"e{log.EmployeeId}", log.Employee?.Name ?? $"#{log.EmployeeId}", false);

    public static string ShortLabel(WorkLog log) => log switch
    {
        { ProductionOrder: { } o } => o.OrderNumber,
        { ServiceRequest: { } s } => s.Number,
        _ => WorkActivities.Label(log.Activity)
    };

    public static decimal ManDays(WorkLog log) => log.Days * Math.Max(1, log.People);

    // Kişi sayısı: farklı personel + her ekibin en kalabalık kaydındaki kişi sayısı.
    private static int People(IEnumerable<WorkLog> logs)
        => logs.Where(x => x.InstallerId is null).Select(x => x.EmployeeId).Distinct().Count()
           + logs.Where(x => x.InstallerId is not null).GroupBy(x => x.InstallerId).Sum(g => g.Max(x => Math.Max(1, x.People)));

    public static List<WorkProjectRow> ByProject(IEnumerable<WorkLog> logs, DateOnly today)
        => logs.GroupBy(Target).Select(g =>
        {
            var done = g.Where(x => x.Date <= today).ToList();
            return new WorkProjectRow(g.Key.Key, g.Key.Label, g.Key.Url, done.Sum(ManDays), g.Where(x => x.Date > today).Sum(ManDays),
                People(done), done.Select(x => x.Date).Distinct().Count(),
                done.Count == 0 ? null : done.Min(x => x.Date), done.Count == 0 ? null : done.Max(x => x.Date),
                done.GroupBy(x => x.Activity).ToDictionary(a => a.Key, a => a.Sum(ManDays)),
                done.GroupBy(x => Worker(x).Name).Select(e => (e.Key, e.Sum(ManDays))).OrderByDescending(x => x.Item2).ToList());
        }).OrderByDescending(x => x.ManDays).ThenByDescending(x => x.PlannedManDays).ToList();

    public static List<WorkEmployeeRow> ByEmployee(IEnumerable<WorkLog> logs, DateOnly today)
        => logs.GroupBy(x => (Worker(x).Key, Name: x.InstallerId is null ? Worker(x).Name : x.Installer?.Name ?? Worker(x).Name, Worker(x).IsTeam)).Select(g =>
        {
            var done = g.Where(x => x.Date <= today).ToList();
            return new WorkEmployeeRow(g.Key.Key, g.Key.Name, g.Key.IsTeam, done.Sum(ManDays), g.Where(x => x.Date > today).Sum(ManDays),
                done.Select(x => Target(x).Key).Distinct().Count(), done.Select(x => x.Date).Distinct().Count(),
                done.GroupBy(x => x.Activity).ToDictionary(a => a.Key, a => a.Sum(ManDays)));
        }).OrderBy(x => x.IsTeam).ThenBy(x => x.Name).ToList();

    // Aynı gün bir kişiye veya ekibe 1 günden fazla iş yazılmışsa uyarı verilir.
    public static HashSet<(string Worker, DateOnly Date)> Overbooked(IEnumerable<WorkLog> logs)
        => logs.GroupBy(x => (Worker(x).Key, x.Date)).Where(g => g.Sum(x => x.Days) > 1).Select(g => g.Key).ToHashSet();

    // Personelin günlük maliyeti: kartta girilen değer, yoksa net maaştan tahmin.
    public static (decimal Cost, bool Estimated) DailyCost(Employee? employee)
        => employee is null ? (0, true) : employee.DailyCost > 0 ? (employee.DailyCost, false) : (decimal.Round(employee.NetSalary / WorkDaysPerMonth, 2), true);

    // Kaydın bir günlük maliyeti: ekipte ekip ücreti (girilmemişse 0, tahmini sayılır), personelde personel maliyeti.
    public static (decimal Cost, bool Estimated) DailyCost(WorkLog log)
        => log.InstallerId is not null ? (log.Installer?.DailyCost ?? 0, (log.Installer?.DailyCost ?? 0) == 0) : DailyCost(log.Employee);

    // Gerçekleşen (bugüne kadar) kayıtların işçilik maliyeti.
    public static (decimal Cost, bool Estimated) Labor(IEnumerable<WorkLog> logs, DateOnly today)
    {
        var parts = logs.Where(x => x.Date <= today).Select(x => (x.Days, Rate: DailyCost(x))).ToList();
        return (decimal.Round(parts.Sum(x => x.Days * x.Rate.Cost), 2), parts.Any(x => x.Rate.Estimated));
    }

    public static List<WorkProjectRow> WithCost(List<WorkProjectRow> rows, IEnumerable<WorkLog> logs, DateOnly today)
    {
        var groups = logs.GroupBy(x => Target(x).Key).ToDictionary(g => g.Key, g => Labor(g, today));
        return rows.Select(r => groups.TryGetValue(r.Key, out var c) ? r with { LaborCost = c.Cost, CostEstimated = c.Estimated } : r).ToList();
    }

    public static List<WorkEmployeeRow> WithCost(List<WorkEmployeeRow> rows, IEnumerable<WorkLog> logs, DateOnly today)
    {
        var groups = logs.GroupBy(x => Worker(x).Key).ToDictionary(g => g.Key, g => Labor(g, today));
        return rows.Select(r => groups.TryGetValue(r.Key, out var c) ? r with { LaborCost = c.Cost, CostEstimated = c.Estimated } : r).ToList();
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
