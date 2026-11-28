using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Prospecting;

// Bir sektör veya ilçedeki firmaların hunisi. Temas: ulaşılmış (aşama ilerlemiş ya da telefon/ziyaret/e-posta kaydı var).
public sealed record YieldRow(string Name, int? SectorId, int Firms, int WithPhone, int Contacted, int Met, int Quoted, int Won, int Closed, decimal Revenue, int GoogleRequests)
{
    public double? WinRate => Contacted == 0 ? null : (double)Won / Contacted;
    public double? ContactRate => Firms == 0 ? null : (double)Contacted / Firms;
    public double? RequestsPerWon => Won == 0 ? null : (double)GoogleRequests / Won;
}

public sealed record YieldReport(IReadOnlyList<YieldRow> Sectors, IReadOnlyList<YieldRow> Districts, YieldRow Total, int MonthlyRequests);

public static class ProspectYield
{
    private sealed record Facts(int Id, int? SectorId, string? District, ProspectStage Stage, bool HasPhone, bool Reached, bool Met, bool Quoted, bool Won, decimal Revenue);

    public static async Task<YieldReport> BuildAsync(AppDbContext db)
    {
        var facts = await FactsAsync(db);
        var sectors = await db.ProspectSectors.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
        var requests = await db.ProspectScans.AsNoTracking().Where(x => x.SectorId != null).GroupBy(x => x.SectorId!.Value).Select(g => new { g.Key, Count = g.Sum(x => x.RequestCount) }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var bySector = sectors.Select(s => Row(s.Name, s.Id, facts.Where(f => f.SectorId == s.Id), requests.GetValueOrDefault(s.Id))).ToList();
        if (facts.Any(f => f.SectorId is null)) bySector.Add(Row("Sektörsüz / elle eklenen", null, facts.Where(f => f.SectorId is null), 0));
        var byDistrict = facts.GroupBy(f => string.IsNullOrWhiteSpace(f.District) ? "İlçe yok" : f.District!).Select(g => Row(g.Key, null, g, 0))
            .OrderByDescending(r => r.Won).ThenByDescending(r => r.Contacted).ThenBy(r => r.Name).ToList();
        var monthStart = ProspectScanner.MonthStartUtc(DateTime.UtcNow);
        var monthly = await db.ProspectScans.Where(x => x.CreatedUtc >= monthStart).SumAsync(x => (int?)x.RequestCount) ?? 0;
        return new YieldReport(bySector, byDistrict, Row("Toplam", null, facts, requests.Values.Sum()), monthly);
    }

    // Sektörün kazanma oranı (kazanılan / temas); arama listesindeki puana eklenir. En az 5 temas yoksa hesaplanmaz.
    public static async Task<Dictionary<int, double>> SectorWinRatesAsync(AppDbContext db)
        => (await FactsAsync(db)).Where(f => f.SectorId is not null).GroupBy(f => f.SectorId!.Value)
            .Select(g => (g.Key, Contacted: g.Count(f => f.Reached), Won: g.Count(f => f.Won)))
            .Where(x => x.Contacted >= 5).ToDictionary(x => x.Key, x => (double)x.Won / x.Contacted);

    private static async Task<List<Facts>> FactsAsync(AppDbContext db)
    {
        var contactKinds = new[] { ProspectActivityKind.Call, ProspectActivityKind.Visit, ProspectActivityKind.Email, ProspectActivityKind.WhatsApp, ProspectActivityKind.Meeting };
        var meetingKinds = new[] { ProspectActivityKind.Visit, ProspectActivityKind.Meeting };
        var rows = await db.Prospects.AsNoTracking().Select(p => new
        {
            p.Id, p.SectorId, p.District, p.Stage, HasPhone = p.Phone != null, p.CustomerId,
            Contact = p.Activities.Any(a => contactKinds.Contains(a.Kind)),
            Meeting = p.Activities.Any(a => meetingKinds.Contains(a.Kind))
        }).ToListAsync();
        var customerIds = rows.Where(r => r.CustomerId != null).Select(r => r.CustomerId!.Value).Distinct().ToList();
        var quotes = await db.Quotes.AsNoTracking().Where(q => customerIds.Contains(q.CustomerId))
            .Select(q => new { q.CustomerId, q.Status, q.GrandTotal }).ToListAsync();
        var won = new[] { QuoteStatus.Accepted, QuoteStatus.Converted };
        return rows.Select(r =>
        {
            var own = r.CustomerId is int c ? quotes.Where(q => q.CustomerId == c).ToList() : new();
            var isWon = r.Stage == ProspectStage.Won || own.Any(q => won.Contains(q.Status));
            var quoted = isWon || r.Stage == ProspectStage.Quoted || own.Count > 0;
            var met = quoted || r.Meeting || r.Stage == ProspectStage.Meeting;
            var reached = met || r.Contact || r.Stage is ProspectStage.Contacted;
            return new Facts(r.Id, r.SectorId, r.District, r.Stage, r.HasPhone, reached, met, quoted, isWon, own.Where(q => won.Contains(q.Status)).Sum(q => q.GrandTotal));
        }).ToList();
    }

    private static YieldRow Row(string name, int? sectorId, IEnumerable<Facts> source, int requests)
    {
        var list = source.ToList();
        return new YieldRow(name, sectorId, list.Count, list.Count(f => f.HasPhone), list.Count(f => f.Reached), list.Count(f => f.Met), list.Count(f => f.Quoted), list.Count(f => f.Won),
            list.Count(f => f.Stage is ProspectStage.Lost or ProspectStage.Unqualified), list.Sum(f => f.Revenue), requests);
    }
}
