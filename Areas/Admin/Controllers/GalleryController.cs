using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize] // diğer admin controller'ların attribute'u farklıysa onunla değiştir
public class GalleryController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public GalleryController(AppDbContext db, IWebHostEnvironment env) => (_db, _env) = (db, env);

    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<IActionResult> Index()
    {
        var images = await _db.GalleryImages.AsNoTracking()
            .OrderBy(g => g.ServiceId).ThenBy(g => g.ProjectId).ThenBy(g => g.SortOrder)
            .ToListAsync();
        return View(images);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await FillBags();
        return View(new GalleryUploadVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GalleryUploadVm vm, IFormFile? file)
    {
        if (vm.ServiceId is not null && vm.ProjectId is not null)
            ModelState.AddModelError("", "Hizmet ve proje aynı anda seçilemez; biri boş kalmalı. Genel galeri için ikisi de boş bırakılabilir.");

        if (file is null || file.Length == 0)
            ModelState.AddModelError("file", "Dosya seçilmedi.");
        else if (file.Length > MaxFileBytes)
            ModelState.AddModelError("file", "Dosya en fazla 5 MB olabilir.");
        else
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                ModelState.AddModelError("file", "Sadece .jpg, .jpeg, .png, .webp yüklenebilir.");
        }

        if (!ModelState.IsValid) { await FillBags(); return View(vm); }

        var serviceSlug = vm.ServiceId is null ? null
            : await _db.Services.Where(s => s.Id == vm.ServiceId).Select(s => s.Slug).FirstOrDefaultAsync();
        var projectSlug = vm.ProjectId is null ? null
            : await _db.Projects.Where(p => p.Id == vm.ProjectId).Select(p => p.Slug).FirstOrDefaultAsync();

        var sub = serviceSlug is not null ? "hizmet-" + serviceSlug
                : projectSlug is not null ? "proje-" + projectSlug
                : "genel";
        var dir = Path.Combine(_env.WebRootPath, "uploads", "galeri", sub);
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file!.FileName).ToLowerInvariant()}";
        await using (var stream = System.IO.File.Create(Path.Combine(dir, fileName)))
            await file.CopyToAsync(stream);

        _db.GalleryImages.Add(new GalleryImage
        {
            ServiceId = vm.ServiceId,
            ProjectId = vm.ProjectId,
            ImagePath = $"/uploads/galeri/{sub}/{fileName}",
            Title = vm.Title,
            Caption = vm.Caption,
            SortOrder = vm.SortOrder,
            IsPublished = vm.IsPublished
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var img = await _db.GalleryImages.FindAsync(id);
        if (img is null) return NotFound();

        // Yalnızca admin'in yüklediği dosyaları diskten sil; /images/ altındaki seed görsellerine dokunma.
        if (img.ImagePath.StartsWith("/uploads/galeri/", StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(_env.WebRootPath, img.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        _db.GalleryImages.Remove(img);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task FillBags()
    {
        ViewBag.Services = new SelectList(await _db.Services.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Projects = new SelectList(await _db.Projects.AsNoTracking().OrderBy(p => p.Title)
            .Select(p => new { p.Id, p.Title }).ToListAsync(), "Id", "Title");
    }
}