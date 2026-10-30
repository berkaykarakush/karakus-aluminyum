using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/cizim/sistemler")]
public sealed class DrawingSystemsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public DrawingSystemsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.ProfileSystems.AsNoTracking().OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync());

    [HttpGet("ekle")]
    public IActionResult Create(string? tur)
    {
        var category = ProductCategories.All.Contains(tur) ? tur! : ProductCategories.Aluminum;
        ViewBag.Settings = new ProductSettings();
        return View("EditSystem", new ProfileSystem { Category = category });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProfileSystem input, ProductSettings settings)
    {
        input.Id = 0;
        if (!Prepare(input, settings)) { ViewBag.Settings = settings; return View("EditSystem", input); }
        _db.ProfileSystems.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ProfileSystem", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Settings = DrawingCalculation.ParseSettings(row.SettingsJson);
        return View("EditSystem", row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProfileSystem input, ProductSettings settings)
    {
        input.Id = id;
        var current = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (current is null) return NotFound();
        input.Category = current.Category; // kullanılan çizimlerle tutarlı kalsın diye tür değişmez
        if (!Prepare(input, settings)) { ViewBag.Settings = settings; return View("EditSystem", input); }
        _db.ProfileSystems.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ProfileSystem", id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    private bool Prepare(ProfileSystem input, ProductSettings settings)
    {
        if (!ProductCategories.All.Contains(input.Category)) ModelState.AddModelError(nameof(input.Category), "Geçersiz ürün türü.");
        if (!ModelState.IsValid) return false;
        input.Material = ProductCategories.Label(input.Category);
        input.SettingsJson = DrawingCalculation.Serialize(settings);
        return true;
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.ProfileSystems.FindAsync(id);
        if (row is null) return NotFound();
        if (await _db.WindowDesigns.AnyAsync(x => x.ProfileSystemId == id))
        {
            TempData["Notice"] = "Bu sistemi kullanan çizimler var; silmek yerine pasif yapın.";
            return RedirectToAction(nameof(Index));
        }
        _db.ProfileSystems.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ProfileSystem", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }
}
