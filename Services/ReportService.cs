using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Analytics;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services;

public sealed record ReportRange(DateTime FromUtc, DateTime ToUtc, string Key)
{
    // Türkiye'de yaz saati uygulaması yoktur; gün sınırları sabit UTC+3 ile hesaplanır.
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);
    public int Days => (int)(ToUtc - FromUtc).TotalDays;
    // Kısa aralık günlük, orta aralık haftalık, uzun aralık aylık gruplanır; seyrek veri okunur kalır.
    public bool Monthly => Days > 120;
    public bool Weekly => !Monthly && Days > 14;
    public string FromText => (FromUtc + Offset).ToString("yyyy-MM-dd");
    public string ToText => (ToUtc + Offset).AddDays(-1).ToString("yyyy-MM-dd");
    public ReportRange Previous => new(FromUtc.AddDays(-Days), FromUtc, Key);

    public static ReportRange Parse(string? key, string? from, string? to, DateTime? nowUtc = null)
    {
        var today = (nowUtc ?? DateTime.UtcNow).Add(Offset).Date;
        var end = DateTime.SpecifyKind(today.AddDays(1) - Offset, DateTimeKind.Utc);
        if (key == "ozel" && DateTime.TryParse(from, out var start) && DateTime.TryParse(to, out var finish) && start.Date <= finish.Date)
        {
            var first = DateTime.SpecifyKind(start.Date - Offset, DateTimeKind.Utc);
            var last = DateTime.SpecifyKind(finish.Date.AddDays(1) - Offset, DateTimeKind.Utc);
            if ((last - first).TotalDays <= 1100) return new ReportRange(first, last, "ozel");
        }
        var days = key switch { "7" => 7, "90" => 90, "365" => 365, _ => 30 };
        return new ReportRange(end.AddDays(-days), end, days.ToString());
    }
}

public sealed record TimeSeries(string Name, IReadOnlyList<decimal> Values);
public sealed record TimeChart(IReadOnlyList<string> Labels, IReadOnlyList<TimeSeries> Series, bool Money, bool Columns = true);
public sealed record BarRow(string Label, decimal Value, string Display);

public sealed record SalesReport(int QuoteCount, int PreviousQuoteCount, decimal QuoteTotal, decimal PreviousQuoteTotal, decimal AverageQuote, decimal? AcceptanceRate, decimal? PreviousAcceptanceRate,
    int ContractCount, decimal SignedTotal, decimal PreviousSignedTotal, int NewCustomers, int PreviousNewCustomers,
    IReadOnlyList<BarRow> QuotesByStatus, IReadOnlyList<BarRow> TopCustomers, IReadOnlyList<BarRow> PaymentsByKind, IReadOnlyList<BarRow> ContractsByStatus, TimeChart Chart);

public sealed record ProductionReport(int DesignCount, int UnitCount, int GlassListCount, int GlassPieces, decimal GlassAreaM2, decimal GlassValue, IReadOnlyList<BarRow> UnitsByCategory, IReadOnlyList<BarRow> GlassByType);

public sealed record ContactReport(int MessageCount, int PreviousMessageCount, int Unread, IReadOnlyList<BarRow> ByService, TimeChart Chart);

public sealed record WebReport(int Sessions, int PreviousSessions, int PageViews, int Leads, int PreviousLeads, decimal? ConversionRate, IReadOnlyList<BarRow> TopPages, IReadOnlyList<BarRow> Sources, IReadOnlyList<BarRow> Devices, IReadOnlyList<BarRow> Events, IReadOnlyList<BarRow> LeadsBySource, TimeChart Chart);

public sealed record FullReport(ReportRange Range, SalesReport Sales, ProductionReport Production, ContactReport Contact, WebReport Web);

// Panel raporları yalnızca veritabanındaki kayıtlardan üretilir.
public sealed class ReportService
{
    private readonly AppDbContext _db;
    public ReportService(AppDbContext db) => _db = db;

    public async Task<FullReport> BuildAsync(ReportRange range) => new(range, await SalesAsync(range), await ProductionAsync(range), await ContactAsync(range), await WebAsync(range));

    private static string Count(decimal value) => value.ToString("N0", TurkishNumbers.Culture);

    public static string QuoteStatusLabel(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "Taslak", QuoteStatus.Sent => "Gönderildi", QuoteStatus.Accepted => "Kabul edildi", QuoteStatus.Rejected => "Reddedildi",
        QuoteStatus.Expired => "Süresi doldu", _ => "Sözleşmeye dönüştürüldü"
    };

    public static decimal? AcceptanceRate(IEnumerable<QuoteStatus> statuses)
    {
        // Taslaklar henüz karara bağlanmadığı için oranın dışında tutulur; gönderilip bekleyenler paydadadır.
        var offered = statuses.Where(s => s != QuoteStatus.Draft).ToList();
        if (offered.Count == 0) return null;
        return Math.Round(offered.Count(s => s is QuoteStatus.Accepted or QuoteStatus.Converted) * 100m / offered.Count, 1, MidpointRounding.AwayFromZero);
    }

    public static TimeChart Chart(ReportRange range, bool money, bool columns, params (string Name, IEnumerable<(DateTime Utc, decimal Value)> Points)[] series)
    {
        var buckets = new List<DateTime>();
        var localStart = (range.FromUtc + ReportRange.Offset).Date;
        var localEnd = (range.ToUtc + ReportRange.Offset).Date;
        // Haftalar dönem sonundan geriye sayılır: eksik gün kalırsa en eski dilimde kalır, son dilim sahte düşüş göstermez.
        DateTime WeekStart(DateTime local)
        {
            var start = localEnd.AddDays(-7 * ((localEnd.AddDays(-1) - local).Days / 7 + 1));
            return start < localStart ? localStart : start;
        }
        if (range.Weekly)
            for (var d = localStart; d < localEnd; d = localEnd.AddDays(-7 * ((localEnd.AddDays(-1) - d).Days / 7)))
                buckets.Add(d);
        else
            for (var d = range.Monthly ? new DateTime(localStart.Year, localStart.Month, 1) : localStart; d < localEnd; d = range.Monthly ? d.AddMonths(1) : d.AddDays(1))
                buckets.Add(d);

        DateTime Bucket(DateTime utc)
        {
            var local = (utc + ReportRange.Offset).Date;
            if (range.Monthly) return new DateTime(local.Year, local.Month, 1);
            return range.Weekly ? WeekStart(local) : local;
        }

        return new TimeChart(
            buckets.Select(b => b.ToString(range.Monthly ? "MMM yyyy" : "d MMM", TurkishNumbers.Culture)).ToList(),
            series.Select(s =>
            {
                var sums = s.Points.GroupBy(p => Bucket(p.Utc)).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
                return new TimeSeries(s.Name, buckets.Select(b => sums.GetValueOrDefault(b)).ToList());
            }).ToList(), money, columns);
    }

    private async Task<SalesReport> SalesAsync(ReportRange range)
    {
        var quotes = await _db.Quotes.AsNoTracking().Where(q => q.CreatedUtc >= range.FromUtc && q.CreatedUtc < range.ToUtc)
            .Select(q => new { q.Status, q.GrandTotal, q.CreatedUtc, q.CustomerId, Customer = q.Customer!.Name }).ToListAsync();
        var previous = await _db.Quotes.AsNoTracking().Where(q => q.CreatedUtc >= range.Previous.FromUtc && q.CreatedUtc < range.FromUtc).Select(q => new { q.Status, q.GrandTotal }).ToListAsync();
        var contracts = await _db.Contracts.AsNoTracking().Where(c => c.DocumentDate >= range.FromUtc && c.DocumentDate < range.ToUtc)
            .Select(c => new { c.Status, c.TotalAmount, c.DocumentDate, c.DocumentJson }).ToListAsync();
        var previousSigned = await _db.Contracts.AsNoTracking().Where(c => c.DocumentDate >= range.Previous.FromUtc && c.DocumentDate < range.FromUtc && c.Status == ContractStatus.Signed).SumAsync(c => (decimal?)c.TotalAmount) ?? 0;
        var customers = await _db.Customers.CountAsync(c => c.CreatedUtc >= range.FromUtc && c.CreatedUtc < range.ToUtc);
        var previousCustomers = await _db.Customers.CountAsync(c => c.CreatedUtc >= range.Previous.FromUtc && c.CreatedUtc < range.FromUtc);

        var signed = contracts.Where(c => c.Status == ContractStatus.Signed).ToList();
        var payments = signed.SelectMany(c => DocumentJson.ParseContent(c.DocumentJson).Payments)
            .GroupBy(p => p.Kind).Select(g => new { Kind = g.Key, Total = g.Sum(p => p.Amount) }).OrderByDescending(x => x.Total)
            .Select(x => new BarRow(x.Kind switch { PaymentKinds.Cash => "Peşin", PaymentKinds.Check => "Çek", PaymentKinds.Barter => "Barter", _ => "Diğer" }, x.Total, TurkishNumbers.Money(x.Total))).ToList();

        return new SalesReport(
            quotes.Count, previous.Count, quotes.Sum(q => q.GrandTotal), previous.Sum(q => q.GrandTotal),
            quotes.Count == 0 ? 0 : Math.Round(quotes.Sum(q => q.GrandTotal) / quotes.Count, 2, MidpointRounding.AwayFromZero),
            AcceptanceRate(quotes.Select(q => q.Status)), AcceptanceRate(previous.Select(q => q.Status)),
            contracts.Count, signed.Sum(c => c.TotalAmount), previousSigned, customers, previousCustomers,
            Enum.GetValues<QuoteStatus>().Select(s => new { Status = s, Count = quotes.Count(q => q.Status == s) }).Where(x => x.Count > 0).Select(x => new BarRow(QuoteStatusLabel(x.Status), x.Count, Count(x.Count))).ToList(),
            quotes.GroupBy(q => new { q.CustomerId, q.Customer }).Select(g => new { g.Key.Customer, Total = g.Sum(q => q.GrandTotal) }).OrderByDescending(x => x.Total).Take(10).Select(x => new BarRow(x.Customer, x.Total, TurkishNumbers.Money(x.Total))).ToList(),
            payments,
            contracts.GroupBy(c => c.Status).OrderBy(g => g.Key).Select(g => new BarRow(g.Key switch { ContractStatus.Signed => "İmzalandı", ContractStatus.Cancelled => "İptal", _ => "Taslak" }, g.Count(), Count(g.Count()))).ToList(),
            Chart(range, true, true, ("Teklif tutarı", quotes.Select(q => (q.CreatedUtc, q.GrandTotal))), ("İmzalı sözleşme bedeli", signed.Select(c => (c.DocumentDate, c.TotalAmount)))));
    }

    private async Task<ProductionReport> ProductionAsync(ReportRange range)
    {
        var designs = await _db.WindowDesigns.AsNoTracking().Where(d => d.CreatedUtc >= range.FromUtc && d.CreatedUtc < range.ToUtc).Select(d => new { d.Category, d.Quantity }).ToListAsync();
        var items = await _db.GlassListItems.AsNoTracking().Where(i => i.GlassList!.CreatedUtc >= range.FromUtc && i.GlassList.CreatedUtc < range.ToUtc)
            .Select(i => new { i.GlassListId, i.WidthMm, i.HeightMm, i.Quantity, Type = i.GlassType!.Name, Price = i.GlassType.PricePerM2 }).ToListAsync();
        decimal Area(decimal w, decimal h, int q) => w * h / 1_000_000m * q;

        return new ProductionReport(
            designs.Count, designs.Sum(d => d.Quantity), items.Select(i => i.GlassListId).Distinct().Count(), items.Sum(i => i.Quantity),
            Math.Round(items.Sum(i => Area(i.WidthMm, i.HeightMm, i.Quantity)), 2), Math.Round(items.Sum(i => Area(i.WidthMm, i.HeightMm, i.Quantity) * i.Price), 2, MidpointRounding.AwayFromZero),
            ProductCategories.All.Select(c => new { Category = c, Units = designs.Where(d => d.Category == c).Sum(d => d.Quantity) }).Where(x => x.Units > 0).OrderByDescending(x => x.Units)
                .Select(x => new BarRow(ProductCategories.Label(x.Category), x.Units, $"{Count(x.Units)} adet")).ToList(),
            items.GroupBy(i => i.Type).Select(g => new { Type = g.Key, Area = g.Sum(i => Area(i.WidthMm, i.HeightMm, i.Quantity)) }).OrderByDescending(x => x.Area)
                .Select(x => new BarRow(x.Type, Math.Round(x.Area, 2), $"{x.Area.ToString("N2", TurkishNumbers.Culture)} m²")).ToList());
    }

    private async Task<ContactReport> ContactAsync(ReportRange range)
    {
        var messages = await _db.ContactMessages.AsNoTracking().Where(m => m.CreatedUtc >= range.FromUtc && m.CreatedUtc < range.ToUtc).Select(m => new { m.CreatedUtc, m.Service }).ToListAsync();
        var previous = await _db.ContactMessages.CountAsync(m => m.CreatedUtc >= range.Previous.FromUtc && m.CreatedUtc < range.FromUtc);
        return new ContactReport(messages.Count, previous, await _db.ContactMessages.CountAsync(m => !m.IsRead),
            messages.GroupBy(m => string.IsNullOrWhiteSpace(m.Service) ? "Belirtilmedi" : m.Service!).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).Select(x => new BarRow(x.Key, x.Count, Count(x.Count))).ToList(),
            Chart(range, false, true, ("Mesaj", messages.Select(m => (m.CreatedUtc, 1m)))));
    }

    public static string SourceOf(string? utmSource, string? referrerHost)
        => !string.IsNullOrWhiteSpace(utmSource) ? utmSource.Trim().ToLowerInvariant() : string.IsNullOrWhiteSpace(referrerHost) ? "Doğrudan" : referrerHost;

    private async Task<WebReport> WebAsync(ReportRange range)
    {
        var events = await _db.AnalyticsEvents.AsNoTracking().Where(e => e.OccurredUtc >= range.FromUtc && e.OccurredUtc < range.ToUtc)
            .Select(e => new { e.OccurredUtc, e.Type, e.Path, e.ReferrerHost, e.UtmSource, e.Device, e.SessionId }).ToListAsync();
        var previous = await _db.AnalyticsEvents.AsNoTracking().Where(e => e.OccurredUtc >= range.Previous.FromUtc && e.OccurredUtc < range.FromUtc).Select(e => new { e.Type, e.SessionId }).ToListAsync();

        var sessions = events.GroupBy(e => e.SessionId).Select(g =>
        {
            var first = g.OrderBy(e => e.OccurredUtc).First();
            return new { Id = g.Key, first.OccurredUtc, first.Device, Source = SourceOf(first.UtmSource, first.ReferrerHost), HasLead = g.Any(e => e.Type == AnalyticsEventTypes.Lead) };
        }).ToList();
        var views = events.Where(e => e.Type == AnalyticsEventTypes.PageView).ToList();
        var leads = sessions.Count(s => s.HasLead);

        return new WebReport(
            sessions.Count, previous.Select(e => e.SessionId).Distinct().Count(), views.Count, leads,
            previous.Where(e => e.Type == AnalyticsEventTypes.Lead).Select(e => e.SessionId).Distinct().Count(),
            sessions.Count == 0 ? null : Math.Round(leads * 100m / sessions.Count, 1, MidpointRounding.AwayFromZero),
            views.GroupBy(e => e.Path).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).Select(x => new BarRow(x.Key, x.Count, Count(x.Count))).ToList(),
            sessions.GroupBy(s => s.Source).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).Select(x => new BarRow(x.Key, x.Count, Count(x.Count))).ToList(),
            sessions.GroupBy(s => s.Device).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Select(x => new BarRow(x.Key switch { "mobile" => "Mobil", "tablet" => "Tablet", _ => "Masaüstü" }, x.Count, Count(x.Count))).ToList(),
            events.Where(e => e.Type != AnalyticsEventTypes.PageView).GroupBy(e => e.Type).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Select(x => new BarRow(AnalyticsEventTypes.Label(x.Key), x.Count, Count(x.Count))).ToList(),
            sessions.Where(s => s.HasLead).GroupBy(s => s.Source).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).Select(x => new BarRow(x.Key, x.Count, Count(x.Count))).ToList(),
            Chart(range, false, false, ("Oturum", sessions.Select(s => (s.OccurredUtc, 1m))), ("Sayfa görüntüleme", views.Select(v => (v.OccurredUtc, 1m)))));
    }
}
