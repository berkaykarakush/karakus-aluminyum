using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Tekliflerin önüne eklenen ve tek başına da indirilebilen kurumsal sunum.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/sunum")]
public sealed class PresentationController : Controller
{
    private const int MaxItemTitle = 80;
    private const int MaxItemText = 300;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _audit;
    private readonly PresentationBuilder _builder;
    public PresentationController(AppDbContext db, IWebHostEnvironment env, IAuditService audit, PresentationBuilder builder) { _db = db; _env = env; _audit = audit; _builder = builder; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await Ordered().AsNoTracking().ToListAsync());

    // firma: kapakta "Hazırlanan" satırına yazılır; teklif dışında belirli bir firmaya sunum göndermek için.
    // yon=dikey: dikey A4 (basılı dosya için); varsayılan yatay A4 (ekranda sunmak için).
    [HttpGet("goruntule")]
    public async Task<IActionResult> Show([FromQuery(Name = "firma")] string? customer, [FromQuery(Name = "yon")] string? orientation)
    {
        customer = string.IsNullOrWhiteSpace(customer) ? null : customer.Trim()[..Math.Min(customer.Trim().Length, 160)];
        var vm = await _builder.BuildAsync(new PresentationCover(customer, null, null, DateTime.UtcNow), portrait: orientation == "dikey");
        return View("~/Areas/Admin/Views/Documents/PresentationPrint.cshtml", vm);
    }

    [HttpGet("ekle")]
    public IActionResult Create([FromQuery(Name = "tur")] string? kind)
    {
        kind = PresentationKinds.All.Contains(kind) ? kind! : PresentationKinds.Text;
        ViewBag.Items = new List<PresentationItem>();
        return View("Edit", new PresentationSection { Kind = kind, Title = kind == PresentationKinds.Text ? "" : PresentationKinds.Label(kind), Eyebrow = PresentationKinds.Label(kind) });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PresentationSection input, List<PresentationItem>? items, IFormFile? imageFile)
    {
        var row = new PresentationSection { Kind = input.Kind, SortOrder = (await _db.PresentationSections.MaxAsync(x => (int?)x.SortOrder) ?? -1) + 1 };
        if (!await ApplyAsync(row, input, items, imageFile, removeImage: false)) { ViewBag.Items = items ?? new(); input.Id = 0; return View("Edit", input); }
        _db.PresentationSections.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "PresentationSection", row.Id.ToString(), $"{row.Kind}: {row.Title}");
        TempData["Notice"] = "Bölüm sunuma eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.PresentationSections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Items = DocumentJson.ParseItems(row.ItemsJson);
        return View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PresentationSection input, List<PresentationItem>? items, IFormFile? imageFile, bool removeImage = false)
    {
        var row = await _db.PresentationSections.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (!await ApplyAsync(row, input, items, imageFile, removeImage))
        {
            ViewBag.Items = items ?? new();
            input.Id = id; input.Kind = row.Kind; input.ImagePath = row.ImagePath;
            return View(input);
        }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "PresentationSection", id.ToString(), $"{row.Kind}: {row.Title}");
        TempData["Notice"] = "Bölüm kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.PresentationSections.FindAsync(id);
        if (row is null) return NotFound();
        _db.PresentationSections.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "PresentationSection", id.ToString(), $"{row.Kind}: {row.Title}");
        TempData["Notice"] = "Bölüm silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var row = await _db.PresentationSections.FindAsync(id);
        if (row is null) return NotFound();
        row.IsActive = !row.IsActive;
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Sıra numaraları her taşımada 0'dan yeniden yazılır; eşit sıralı eski kayıtlar da düzelir.
    [HttpPost("tasi/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, int direction)
    {
        var rows = await Ordered().ToListAsync();
        var index = rows.FindIndex(x => x.Id == id);
        if (index < 0) return NotFound();
        var target = index + Math.Sign(direction);
        if (target >= 0 && target < rows.Count) (rows[index], rows[target]) = (rows[target], rows[index]);
        for (var i = 0; i < rows.Count; i++) rows[i].SortOrder = i;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Tüm bölümleri silip başlangıç içeriğini yeniden kurar. Yüklenen görsel dosyaları silinmez.
    [HttpPost("varsayilan"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reset()
    {
        _db.PresentationSections.RemoveRange(await _db.PresentationSections.ToListAsync());
        _db.PresentationSections.AddRange(PresentationSeed.Sections());
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("reset", "PresentationSection", null, "Varsayılan sunum içeriği yüklendi");
        TempData["Notice"] = "Sunum varsayılan içeriğe döndürüldü.";
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<PresentationSection> Ordered() => _db.PresentationSections.OrderBy(x => x.SortOrder).ThenBy(x => x.Id);

    private async Task<bool> ApplyAsync(PresentationSection row, PresentationSection input, List<PresentationItem>? items, IFormFile? imageFile, bool removeImage)
    {
        ModelState.Remove(nameof(input.Kind));
        ModelState.Remove(nameof(input.ItemsJson));
        if (!PresentationKinds.All.Contains(row.Kind)) ModelState.AddModelError(string.Empty, "Geçersiz bölüm türü.");

        var clean = (items ?? new()).Select(i => new PresentationItem { Title = i.Title?.Trim(), Text = i.Text?.Trim() })
            .Where(i => !string.IsNullOrEmpty(i.Title) || !string.IsNullOrEmpty(i.Text)).ToList();
        if (clean.Count > PresentationSection.MaxItems) ModelState.AddModelError(string.Empty, $"En fazla {PresentationSection.MaxItems} madde eklenebilir.");
        if (clean.Any(i => (i.Title?.Length ?? 0) > MaxItemTitle)) ModelState.AddModelError(string.Empty, $"Madde başlığı en fazla {MaxItemTitle} karakter olabilir.");
        if (clean.Any(i => (i.Text?.Length ?? 0) > MaxItemText)) ModelState.AddModelError(string.Empty, $"Madde metni en fazla {MaxItemText} karakter olabilir.");

        string? uploaded = null;
        if (ModelState.IsValid)
        {
            try { uploaded = await AdminImageUpload.SaveAsync(imageFile, _env); }
            catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        }
        if (!ModelState.IsValid) return false;

        row.Eyebrow = string.IsNullOrWhiteSpace(input.Eyebrow) ? null : input.Eyebrow.Trim();
        row.Title = input.Title.Trim();
        row.Body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim();
        row.ImagePath = uploaded ?? (removeImage ? null : row.ImagePath);
        row.ItemsJson = DocumentJson.Serialize(clean);
        row.IsActive = input.IsActive;
        row.UpdatedUtc = DateTime.UtcNow;
        return true;
    }
}
