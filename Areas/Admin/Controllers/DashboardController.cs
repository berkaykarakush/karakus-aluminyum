using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record DashboardTask(string Text, int Count, string Url);
public sealed record SetupItem(string Text, string Url, bool Done);
public sealed record RecentItem(string Title, string Detail, string Url);

public sealed class DashboardVm
{
    public required FullReport Report { get; init; }
    public required DashboardPipeline Pipeline { get; init; }
    public required DashboardFinance? Finance { get; init; }
    public required List<DashboardTask> Tasks { get; init; }
    public required List<SetupItem> Setup { get; init; }
    public required List<RecentItem> RecentMessages { get; init; }
    public required List<RecentItem> RecentQuotes { get; init; }
}

[Area("Admin"), Authorize(Roles = "Admin,Editor,Accountant"), Route("yonetim")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReportService _reports;
    private readonly SiteSettingsProvider _settings;
    private readonly DashboardService _dashboard;
    public DashboardController(AppDbContext db, ReportService reports, SiteSettingsProvider settings, DashboardService dashboard) { _db = db; _reports = reports; _settings = settings; _dashboard = dashboard; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? aralik, string? bas, string? bit)
    {
        if (User.IsInRole("Accountant") && !User.IsInRole("Admin") && !User.IsInRole("Editor")) return Redirect("/yonetim/muhasebe");
        var now = DateTime.UtcNow;
        var settings = await _settings.GetAsync();
        var contractTemplate = await _db.DocumentTemplates.AsNoTracking().FirstAsync(t => t.Kind == DocumentKinds.Contract);

        var tasks = new List<DashboardTask>
        {
            new("Okunmamış mesaj", await _db.ContactMessages.CountAsync(x => !x.IsRead), "/yonetim/mesajlar"),
            new("Taslak teklif", await _db.Quotes.CountAsync(x => x.Status == QuoteStatus.Draft), "/yonetim/crm/teklifler?status=0"),
            new("Yanıt bekleyen teklif", await _db.Quotes.CountAsync(x => x.Status == QuoteStatus.Sent), "/yonetim/crm/teklifler?status=1"),
            new("Süresi geçmiş gönderilmiş teklif", await _db.Quotes.CountAsync(x => x.Status == QuoteStatus.Sent && x.ValidUntilUtc != null && x.ValidUntilUtc < now), "/yonetim/crm/teklifler?status=1"),
            new("Sözleşmeye dönüşmeyi bekleyen kabul", await _db.Quotes.CountAsync(x => x.Status == QuoteStatus.Accepted), "/yonetim/crm/teklifler?status=2"),
            new("Taslak sözleşme", await _db.Contracts.CountAsync(x => x.Status == ContractStatus.Draft), "/yonetim/sozlesmeler"),
            new("Gönderilemeyen bildirim", await _db.NotificationJobs.CountAsync(x => x.Status == KarakusAluminyum.Web.Models.NotificationStatus.Failed), "/yonetim/bildirimler?durum=Failed")
        };

        var setup = new List<SetupItem>
        {
            new("Google Analytics bağlantısı", "/yonetim/ayarlar", !string.IsNullOrWhiteSpace(settings.Ga4MeasurementId)),
            new("Meta Pixel bağlantısı", "/yonetim/ayarlar", !string.IsNullOrWhiteSpace(settings.MetaPixelId)),
            new("Sözleşme imza yetkilisi", "/yonetim/sablonlar/contract", !string.IsNullOrWhiteSpace(contractTemplate.Signatory)),
            new("Cam tipi fiyatları", "/yonetim/camlar/tipler", await _db.GlassTypes.AnyAsync(x => x.IsActive && x.PricePerM2 > 0))
        };
        if (settings.ProductionDetails) setup.Add(new("Doğrama sistemi fiyatları", "/yonetim/cizim/sistemler", await _db.ProfileSystems.AnyAsync(x => x.IsActive && x.FramePricePerM > 0)));

        var messages = await _db.ContactMessages.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(5).ToListAsync();
        var quotes = await _db.Quotes.AsNoTracking().Include(x => x.Customer).OrderByDescending(x => x.CreatedUtc).Take(5).ToListAsync();
        var range = ReportRange.Parse(aralik, bas, bit, now);
        return View(new DashboardVm
        {
            Report = await _reports.BuildAsync(range),
            Pipeline = await _dashboard.PipelineAsync(range, now),
            // Muhasebe verisi editöre kapalıdır: bu rolde hesaplanmaz, sayfaya da girmez.
            Finance = User.IsInRole("Admin") ? await _dashboard.FinanceAsync(range, DateOnly.FromDateTime(now + ReportRange.Offset)) : null,
            Tasks = tasks,
            Setup = setup,
            RecentMessages = messages.Select(x => new RecentItem(x.Name, x.Service ?? x.ContactInfo, $"/yonetim/mesajlar/{x.Id}")).ToList(),
            RecentQuotes = quotes.Select(x => new RecentItem(x.Title, x.Customer?.Name ?? "", $"/yonetim/crm/teklifler/detay/{x.Id}")).ToList()
        });
    }
}
