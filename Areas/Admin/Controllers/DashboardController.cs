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
    public required List<DashboardTask> Tasks { get; init; }
    public required List<SetupItem> Setup { get; init; }
    public required List<RecentItem> RecentMessages { get; init; }
    public required List<RecentItem> RecentQuotes { get; init; }
}

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReportService _reports;
    private readonly SiteSettingsProvider _settings;
    public DashboardController(AppDbContext db, ReportService reports, SiteSettingsProvider settings) { _db = db; _reports = reports; _settings = settings; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
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
            new("Taslak sözleşme", await _db.Contracts.CountAsync(x => x.Status == ContractStatus.Draft), "/yonetim/sozlesmeler")
        };

        var setup = new List<SetupItem>
        {
            new("Google Analytics bağlantısı", "/yonetim/ayarlar", !string.IsNullOrWhiteSpace(settings.Ga4MeasurementId)),
            new("Meta Pixel bağlantısı", "/yonetim/ayarlar", !string.IsNullOrWhiteSpace(settings.MetaPixelId)),
            new("Sözleşme imza yetkilisi", "/yonetim/sablonlar/contract", !string.IsNullOrWhiteSpace(contractTemplate.Signatory)),
            new("Doğrama sistemi fiyatları", "/yonetim/cizim/sistemler", await _db.ProfileSystems.AnyAsync(x => x.IsActive && x.FramePricePerM > 0)),
            new("Cam tipi fiyatları", "/yonetim/camlar/tipler", await _db.GlassTypes.AnyAsync(x => x.IsActive && x.PricePerM2 > 0))
        };

        var messages = await _db.ContactMessages.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(5).ToListAsync();
        var quotes = await _db.Quotes.AsNoTracking().Include(x => x.Customer).OrderByDescending(x => x.CreatedUtc).Take(5).ToListAsync();
        return View(new DashboardVm
        {
            Report = await _reports.BuildAsync(ReportRange.Parse("30", null, null)),
            Tasks = tasks,
            Setup = setup,
            RecentMessages = messages.Select(x => new RecentItem(x.Name, x.Service ?? x.ContactInfo, $"/yonetim/mesajlar/{x.Id}")).ToList(),
            RecentQuotes = quotes.Select(x => new RecentItem(x.Title, x.Customer?.Name ?? "", $"/yonetim/crm/teklifler/detay/{x.Id}")).ToList()
        });
    }
}
