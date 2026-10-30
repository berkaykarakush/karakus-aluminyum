using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Çizimlerin sağ üstünde görünen ürün markaları.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/markalar")]
public sealed class BrandsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;
    public BrandsController(AppDbContext db, IAuditService audit, IWebHostEnvironment env) { _db = db; _audit = audit; _env = env; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.Brands.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync());

    [HttpGet("ekle")]
    public IActionResult Create() => View("Edit", new Brand());

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(9_000_000)]
    public async Task<IActionResult> Create(Brand input, IFormFile? logoFile)
    {
        input.Id = 0;
        input.LogoPath = null;
        if (!Check(input) || !await SaveLogoAsync(input, logoFile)) return View("Edit", input);
        _db.Brands.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Brand", input.Id.ToString(), input.Name);
        TempData["Notice"] = "Marka kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.Brands.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(9_000_000)]
    public async Task<IActionResult> Edit(int id, Brand input, IFormFile? logoFile, bool removeLogo = false)
    {
        var row = await _db.Brands.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        input.LogoPath = removeLogo ? null : row.LogoPath;
        if (input.Category != row.Category && await _db.WindowDesigns.AnyAsync(x => x.BrandId == id))
            ModelState.AddModelError(nameof(input.Category), "Bu markayı kullanan çizimler var; ürün türü değiştirilemez.");
        if (!Check(input) || !await SaveLogoAsync(input, logoFile)) return View(input);

        row.Name = input.Name.Trim();
        row.Category = input.Category;
        row.SortOrder = input.SortOrder;
        row.IsActive = input.IsActive;
        row.LogoPath = input.LogoPath;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Brand", id.ToString(), row.Name);
        TempData["Notice"] = "Marka kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.Brands.FindAsync(id);
        if (row is null) return NotFound();
        if (await _db.WindowDesigns.AnyAsync(x => x.BrandId == id))
        {
            TempData["Notice"] = "Bu markayı kullanan çizimler var; silmek yerine pasif yapın.";
            return RedirectToAction(nameof(Index));
        }
        _db.Brands.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "Brand", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    private bool Check(Brand input)
    {
        if (!ProductCategories.All.Contains(input.Category)) ModelState.AddModelError(nameof(input.Category), "Geçerli bir ürün türü seçin.");
        return ModelState.IsValid;
    }

    private async Task<bool> SaveLogoAsync(Brand input, IFormFile? logoFile)
    {
        try
        {
            input.LogoPath = await AdminImageUpload.SaveAsync(logoFile, _env) ?? input.LogoPath;
            return true;
        }
        catch (InvalidOperationException error)
        {
            ModelState.AddModelError(string.Empty, error.Message);
            return false;
        }
    }
}
