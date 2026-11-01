using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/referanslar")]
public sealed class ReferencesController : Controller
{
    private const string LogoFolder = "uploads/referanslar";
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;
    public ReferencesController(AppDbContext db, IAuditService audit, IWebHostEnvironment env) { _db = db; _audit = audit; _env = env; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.ClientReferences.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync());

    [HttpGet("ekle")]
    public IActionResult Create() => View("Edit", new ClientReference());

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(9_000_000)]
    public async Task<IActionResult> Create(ClientReference input, IFormFile? logoFile)
    {
        var row = new ClientReference();
        if (!await ApplyAsync(row, input, logoFile)) return View("Edit", input);
        _db.ClientReferences.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ClientReference", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Referans kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.ClientReferences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(9_000_000)]
    public async Task<IActionResult> Edit(int id, ClientReference input, IFormFile? logoFile)
    {
        var row = await _db.ClientReferences.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        input.LogoPath = row.LogoPath;
        var previousLogo = row.LogoPath;
        if (!await ApplyAsync(row, input, logoFile)) return View(input);
        await _db.SaveChangesAsync();
        if (previousLogo != row.LogoPath) DeleteLogo(previousLogo);
        await _audit.RecordAsync("update", "ClientReference", id.ToString(), row.Name);
        TempData["Notice"] = "Referans kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.ClientReferences.FindAsync(id);
        if (row is null) return NotFound();
        _db.ClientReferences.Remove(row);
        await _db.SaveChangesAsync();
        DeleteLogo(row.LogoPath);
        await _audit.RecordAsync("delete", "ClientReference", id.ToString(), row.Name);
        TempData["Notice"] = "Referans silindi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ApplyAsync(ClientReference row, ClientReference input, IFormFile? logoFile)
    {
        if (!ClientReference.TryNormalizeUrl(input.WebsiteUrl, out var url))
            ModelState.AddModelError(nameof(input.WebsiteUrl), "Geçerli bir web sitesi adresi yazın, örn. https://www.ornek.com");
        if (logoFile is null or { Length: 0 } && string.IsNullOrEmpty(row.LogoPath))
            ModelState.AddModelError("logoFile", "Logo görseli seçin.");
        if (!ModelState.IsValid) return false;

        string? uploaded;
        try { uploaded = await AdminImageUpload.SaveAsync(logoFile, _env, LogoFolder, ImageOptimizer.LogoEdge); }
        catch (InvalidOperationException error)
        {
            ModelState.AddModelError("logoFile", error.Message);
            return false;
        }
        row.Name = input.Name.Trim();
        row.WebsiteUrl = url;
        row.SortOrder = input.SortOrder;
        row.IsPublished = input.IsPublished;
        row.LogoPath = uploaded ?? row.LogoPath;
        return true;
    }

    private void DeleteLogo(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith($"/{LogoFolder}/", StringComparison.OrdinalIgnoreCase)) return;
        var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        ImageOptimizer.DeleteWithVariants(Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }
}
