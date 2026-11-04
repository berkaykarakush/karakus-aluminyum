using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Prospecting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record ProspectFilter(string? Q, int? Sector, string? District, ProspectStage? Stage, string? Show, int Page = 1);
public sealed record ProspectListVm(IReadOnlyList<Prospect> Items, ProspectFilter Filter, int Total, int PageSize, IReadOnlyDictionary<ProspectStage, int> StageCounts, int DueCount);
public sealed record ProspectScanVm(IReadOnlyList<ProspectSector> Sectors, IReadOnlyDictionary<int, int> SectorCounts, IReadOnlyList<ProspectDistrict> Districts, IReadOnlyList<ProspectScan> Scans, int MonthlyUsage, ProspectingOptions Options);

// Firma havuzu: sektörel olarak toplanan potansiyel firmalar ve satış takibi.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/crm/firmalar")]
public sealed class ProspectsController : Controller
{
    public const int PageSize = 50;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ProspectScanner _scanner;
    private readonly ProspectSignal _signal;
    private readonly ProspectingOptions _options;
    public ProspectsController(AppDbContext db, IAuditService audit, ProspectScanner scanner, ProspectSignal signal, IOptions<ProspectingOptions> options)
    { _db = db; _audit = audit; _scanner = scanner; _signal = signal; _options = options.Value; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    private static readonly ProspectStage[] Closed = { ProspectStage.Won, ProspectStage.Lost, ProspectStage.Unqualified };

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int? sector, string? district, ProspectStage? stage, string? show, int page = 1)
    {
        var filter = new ProspectFilter(q, sector, district, stage, show, Math.Max(1, page));
        var query = Filtered(filter);
        var total = await query.CountAsync();
        var items = await query.Include(x => x.Sector)
            .OrderBy(x => x.NextFollowUp == null).ThenBy(x => x.NextFollowUp).ThenByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id)
            .Skip((filter.Page - 1) * PageSize).Take(PageSize).AsNoTracking().ToListAsync();
        var counts = await _db.Prospects.GroupBy(x => x.Stage).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var today = Today;
        var due = await _db.Prospects.CountAsync(x => x.NextFollowUp != null && x.NextFollowUp <= today && !Closed.Contains(x.Stage) && !x.DoNotContact);
        await LoadOptionsAsync();
        return View(new ProspectListVm(items, filter, total, PageSize, counts, due));
    }

    // Arama listesi için Excel'de açılan CSV.
    [HttpGet("disa-aktar")]
    public async Task<IActionResult> Export(string? q, int? sector, string? district, ProspectStage? stage, string? show)
    {
        var rows = await Filtered(new ProspectFilter(q, sector, district, stage, show)).Include(x => x.Sector).OrderBy(x => x.Name).AsNoTracking().Take(10_000).ToListAsync();
        var header = new[] { "Firma", "Sektör", "İlçe", "Telefon", "E-posta", "Web", "Instagram", "Facebook", "LinkedIn", "Aşama", "Takip", "Yetkili", "Not" };
        var csv = ProspectText.Csv(new[] { header }.Concat(rows.Select(x => new[]
        {
            x.Name, x.Sector?.Name, x.District, x.Phone, x.Email, x.Website, x.Instagram, x.Facebook, x.LinkedIn, ProspectLabels.Stage(x.Stage),
            x.NextFollowUp?.ToString("dd.MM.yyyy"), x.ContactPerson, x.Notes
        })));
        await _audit.RecordAsync("export", "Prospect", null, $"{rows.Count} firma");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", $"firma-havuzu-{Today:yyyy-MM-dd}.csv");
    }

    private IQueryable<Prospect> Filtered(ProspectFilter f)
    {
        var query = _db.Prospects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%") || (x.Phone != null && x.Phone.Contains(term)) || (x.Email != null && EF.Functions.ILike(x.Email, $"%{term}%")) || (x.ContactPerson != null && EF.Functions.ILike(x.ContactPerson, $"%{term}%")));
        }
        if (f.Sector is int sector) query = query.Where(x => x.SectorId == sector);
        if (!string.IsNullOrWhiteSpace(f.District)) query = query.Where(x => x.District == f.District);
        if (f.Stage is ProspectStage stage) query = query.Where(x => x.Stage == stage);
        var today = Today;
        query = f.Show switch
        {
            "takip" => query.Where(x => x.NextFollowUp != null && x.NextFollowUp <= today && !Closed.Contains(x.Stage) && !x.DoNotContact),
            "telefon" => query.Where(x => x.Phone != null),
            "eposta" => query.Where(x => x.Email != null),
            "web" => query.Where(x => x.Website != null),
            "acik" => query.Where(x => !Closed.Contains(x.Stage) && !x.DoNotContact),
            "iletisim-yok" => query.Where(x => x.DoNotContact),
            _ => query
        };
        return query;
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create() { await LoadOptionsAsync(); return View(new Prospect()); }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Prospect input)
    {
        var row = new Prospect { Source = ProspectSources.Manual };
        if (!await ApplyAsync(row, input)) { await LoadOptionsAsync(); return View(input); }
        _db.Prospects.Add(row);
        await _db.SaveChangesAsync();
        _signal.Wake(); // web sitesi varsa e-posta ve sosyal hesaplar arka planda okunur
        await _audit.RecordAsync("create", "Prospect", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Firma havuza eklendi.";
        return RedirectToAction(nameof(Details), new { id = row.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await _db.Prospects.Include(x => x.Sector).Include(x => x.Customer).Include(x => x.Activities.OrderByDescending(a => a.CreatedUtc)).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptionsAsync();
        return View(row);
    }

    [HttpPost("{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(int id, Prospect input)
    {
        var row = await _db.Prospects.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (!await ApplyAsync(row, input))
        {
            await LoadOptionsAsync();
            var view = await _db.Prospects.Include(x => x.Sector).Include(x => x.Customer).Include(x => x.Activities.OrderByDescending(a => a.CreatedUtc)).AsNoTracking().FirstAsync(x => x.Id == id);
            return View(view);
        }
        await _db.SaveChangesAsync();
        _signal.Wake();
        await _audit.RecordAsync("update", "Prospect", id.ToString(), row.Name);
        TempData["Notice"] = "Firma bilgileri kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Görüşme kaydı. "Yeni" aşamadaki firma ile ilk temas kurulunca aşama kendiliğinden ilerler.
    [HttpPost("{id:int}/gorusme"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddActivity(int id, ProspectActivityKind kind, string? text, DateOnly? nextFollowUp)
    {
        var row = await _db.Prospects.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(text) || text.Trim().Length > 1000)
        {
            TempData["Notice"] = "Görüşme notu boş olamaz ve en fazla 1000 karakter olabilir.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.ProspectActivities.Add(new ProspectActivity { ProspectId = id, Kind = kind, Text = text.Trim(), CreatedBy = User.Identity?.Name });
        if (row.Stage == ProspectStage.New && kind != ProspectActivityKind.Note) row.Stage = kind is ProspectActivityKind.Meeting or ProspectActivityKind.Visit ? ProspectStage.Meeting : ProspectStage.Contacted;
        row.NextFollowUp = nextFollowUp;
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Görüşme kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/musteriye-donustur"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id)
    {
        var row = await _db.Prospects.Include(x => x.Sector).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (row.CustomerId is int existing) return Redirect($"/yonetim/crm/musteriler/duzenle/{existing}");
        var customer = new Customer
        {
            Name = ProspectText.Truncate(row.Name, 160), Kind = CustomerKind.Company, Status = CustomerStatus.Lead, ContactPerson = row.ContactPerson,
            Phone = row.Phone, Email = row.Email, Address = row.Address, City = _options.City, District = row.District,
            Source = ProspectText.Truncate("Firma havuzu" + (row.Sector is null ? "" : " · " + row.Sector.Name), 80), Notes = row.Notes
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        row.CustomerId = customer.Id;
        if (row.Stage is ProspectStage.New or ProspectStage.Contacted or ProspectStage.Meeting) row.Stage = ProspectStage.Quoted;
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("convert", "Prospect", id.ToString(), $"customer={customer.Id}");
        TempData["Notice"] = "Firma müşterilere eklendi; artık teklif hazırlayabilirsiniz.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/web-sitesini-oku"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrich(int id, CancellationToken cancellationToken)
    {
        var row = await _db.Prospects.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (row.Website is null) { TempData["Notice"] = "Firmanın web sitesi kayıtlı değil."; return RedirectToAction(nameof(Details), new { id }); }
        await _scanner.EnrichAsync(row, cancellationToken);
        TempData["Notice"] = row.EnrichError ?? "Web sitesi okundu; bulunan bilgiler boş alanlara eklendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/sil"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.Prospects.FindAsync(id);
        if (row is null) return NotFound();
        _db.Prospects.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "Prospect", id.ToString(), row.Name);
        TempData["Notice"] = "Firma havuzdan silindi. Google taramasında yeniden bulunursa tekrar eklenir; bunun yerine \"İletişim kurulmasın\" işaretlenebilir.";
        return RedirectToAction(nameof(Index));
    }

    // ---- Tarama ayarları ----

    [HttpGet("tarama")]
    public async Task<IActionResult> Scan()
    {
        var sectors = await _db.ProspectSectors.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
        var counts = await _db.Prospects.Where(x => x.SectorId != null).GroupBy(x => x.SectorId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var districts = await _db.ProspectDistricts.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        var scans = await _db.ProspectScans.Include(x => x.Sector).AsNoTracking().OrderByDescending(x => x.Id).Take(20).ToListAsync();
        return View(new ProspectScanVm(sectors, counts, districts, scans, await _scanner.MonthlyUsageAsync(), _options));
    }

    [HttpPost("tarama/ilceler"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDistricts(List<int>? active)
    {
        var ids = (active ?? new()).ToHashSet();
        foreach (var district in await _db.ProspectDistricts.ToListAsync()) district.IsActive = ids.Contains(district.Id);
        await _db.SaveChangesAsync();
        TempData["Notice"] = $"{ids.Count} ilçe taramaya açık.";
        return RedirectToAction(nameof(Scan));
    }

    [HttpPost("tarama/baslat"), ValidateAntiForgeryToken]
    public async Task<IActionResult> StartScan(int? sectorId)
    {
        if (!_options.HasApiKey) { TempData["Notice"] = "Önce Google Places API anahtarını tanımlayın (bu sayfadaki kurulum adımları)."; return RedirectToAction(nameof(Scan)); }
        var sectors = await _db.ProspectSectors.Where(x => x.IsActive && (sectorId == null || x.Id == sectorId)).OrderBy(x => x.SortOrder).ToListAsync();
        var waiting = await _db.ProspectScans.Where(x => x.Status == ProspectScanStatus.Queued || x.Status == ProspectScanStatus.Running).Select(x => x.SectorId).ToListAsync();
        var added = 0;
        foreach (var sector in sectors.Where(s => !waiting.Contains(s.Id))) { await _scanner.EnqueueAsync(sector.Id, User.Identity?.Name); added++; }
        _signal.Wake();
        TempData["Notice"] = added == 0 ? "Seçilen sektör zaten sırada veya kapalı." : $"{added} sektör taramaya alındı. Sonuçlar birkaç dakika içinde havuza düşer.";
        return RedirectToAction(nameof(Scan));
    }

    [HttpGet("sektor/ekle")]
    public IActionResult CreateSector() { ViewBag.Keywords = ""; return View("Sector", new ProspectSector()); }

    [HttpPost("sektor/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSector(ProspectSector input, string? keywords)
    {
        var row = new ProspectSector { SortOrder = (await _db.ProspectSectors.MaxAsync(x => (int?)x.SortOrder) ?? -1) + 1 };
        if (!ApplySector(row, input, keywords)) { ViewBag.Keywords = keywords; return View("Sector", input); }
        _db.ProspectSectors.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ProspectSector", row.Id.ToString(), row.Name);
        return RedirectToAction(nameof(Scan));
    }

    [HttpGet("sektor/{id:int}")]
    public async Task<IActionResult> EditSector(int id)
    {
        var row = await _db.ProspectSectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Keywords = string.Join('\n', ProspectText.ParseKeywords(row.KeywordsJson));
        return View("Sector", row);
    }

    [HttpPost("sektor/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSector(int id, ProspectSector input, string? keywords)
    {
        var row = await _db.ProspectSectors.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (!ApplySector(row, input, keywords)) { ViewBag.Keywords = keywords; input.Id = id; return View("Sector", input); }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ProspectSector", id.ToString(), row.Name);
        return RedirectToAction(nameof(Scan));
    }

    // Sektör silinince firmaları havuzda kalır, yalnızca sektörsüz görünür.
    [HttpPost("sektor/{id:int}/sil"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSector(int id)
    {
        var row = await _db.ProspectSectors.FindAsync(id);
        if (row is null) return NotFound();
        _db.ProspectSectors.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ProspectSector", id.ToString(), row.Name);
        return RedirectToAction(nameof(Scan));
    }

    private bool ApplySector(ProspectSector row, ProspectSector input, string? keywords)
    {
        var list = ProspectText.KeywordsFromLines(keywords);
        if (list.Count == 0) ModelState.AddModelError(string.Empty, "En az bir anahtar kelime yazın.");
        if (list.Count > ProspectSector.MaxKeywords) ModelState.AddModelError(string.Empty, $"En fazla {ProspectSector.MaxKeywords} anahtar kelime yazılabilir.");
        if (list.Any(k => k.Length > 80)) ModelState.AddModelError(string.Empty, "Anahtar kelime en fazla 80 karakter olabilir.");
        ModelState.Remove(nameof(input.KeywordsJson));
        if (!ModelState.IsValid) return false;
        row.Name = input.Name.Trim();
        row.KeywordsJson = JsonSerializer.Serialize(list);
        row.IsActive = input.IsActive;
        return true;
    }

    private async Task<bool> ApplyAsync(Prospect row, Prospect input)
    {
        foreach (var key in new[] { nameof(input.Source), nameof(input.Sector), nameof(input.Customer), nameof(input.Activities) }) ModelState.Remove(key);
        if (!Enum.IsDefined(input.Stage)) ModelState.AddModelError(nameof(input.Stage), "Geçersiz aşama.");
        if (input.SectorId is int s && !await _db.ProspectSectors.AnyAsync(x => x.Id == s)) input.SectorId = null;
        var website = ProspectText.NormalizeWebsite(input.Website);
        if (!string.IsNullOrWhiteSpace(input.Website) && website is null) ModelState.AddModelError(nameof(input.Website), "Web sitesi adresi geçersiz.");
        string? Social(string? value, string field, params string[] hosts)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && hosts.Any(h => uri.Host == h || uri.Host.EndsWith("." + h)) && uri.AbsoluteUri.Length <= 300) return uri.AbsoluteUri;
            ModelState.AddModelError(field, "Bağlantı https:// ile başlamalı ve ilgili sosyal medya sitesine ait olmalı.");
            return null;
        }
        var instagram = Social(input.Instagram, nameof(input.Instagram), "instagram.com");
        var facebook = Social(input.Facebook, nameof(input.Facebook), "facebook.com");
        var linkedin = Social(input.LinkedIn, nameof(input.LinkedIn), "linkedin.com");
        var tiktok = Social(input.TikTok, nameof(input.TikTok), "tiktok.com");
        if (!ModelState.IsValid) return false;

        if (row.Website != website) row.EnrichedUtc = null; // yeni site yeniden okunur
        row.Name = input.Name.Trim();
        row.SectorId = input.SectorId;
        row.Stage = input.Stage;
        row.ContactPerson = Clean(input.ContactPerson);
        row.Phone = Clean(input.Phone);
        row.Email = Clean(input.Email)?.ToLowerInvariant();
        row.Website = website;
        row.Instagram = instagram; row.Facebook = facebook; row.LinkedIn = linkedin; row.TikTok = tiktok;
        row.Address = Clean(input.Address);
        row.District = Clean(input.District);
        row.NextFollowUp = input.NextFollowUp;
        row.Notes = Clean(input.Notes);
        row.DoNotContact = input.DoNotContact;
        row.UpdatedUtc = DateTime.UtcNow;
        return true;

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task LoadOptionsAsync()
    {
        ViewBag.Sectors = await _db.ProspectSectors.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync();
        ViewBag.Districts = await _db.ProspectDistricts.AsNoTracking().OrderBy(x => x.Name).Select(x => x.Name).ToListAsync();
    }
}
