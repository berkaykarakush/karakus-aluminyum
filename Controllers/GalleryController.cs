using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Controllers;

public class GalleryController : Controller
{
    private readonly AppDbContext _db;
    private readonly SeoService _seo;
    public GalleryController(AppDbContext db, SeoService seo) => (_db, _seo) = (db, seo);

    [Route("galeri")]
    public async Task<IActionResult> Index(string? k)
    {
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Galeri | Karakuş Alüminyum",
            Description = "Alüminyum doğrama, sürme sistemler, kapı-pencere, giydirme cephe ve cam balkon uygulamalarımızdan seçtiğimiz görüntüler.",
            CanonicalPath = "/galeri"
        };

        var images = await _db.GalleryImages.AsNoTracking()
            .Where(g => g.IsPublished)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync();

        // Düz veriyi çek, demetleri bellekte kur (CS8143: expression tree'de demet literali olamaz).
        var serviceCats = await _db.Services.AsNoTracking()
            .Where(s => s.IsPublished && _db.GalleryImages.Any(g => g.ServiceId == s.Id && g.IsPublished))
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Slug, s.Name })
            .ToListAsync();
        var projectCats = await _db.Projects.AsNoTracking()
            .Where(p => p.IsPublished && _db.GalleryImages.Any(g => g.ProjectId == p.Id && g.IsPublished))
            .OrderBy(p => p.Id) // PortfolioProject'te SortOrder yok; gerçek sıralama alanı buysa değiştir
            .Select(p => new { p.Slug, p.Title })
            .ToListAsync();

        var categories = new List<(string Key, string Name)> { ("", "Tümü"), ("genel", "Genel") };
        categories.AddRange(serviceCats.Select(s => ("hizmet-" + s.Slug, s.Name)));
        categories.AddRange(projectCats.Select(p => ("proje-" + p.Slug, p.Title)));

        var activeKey = k ?? "";
        List<GalleryImage> list = images;
        if (activeKey == "genel")
        {
            list = images.Where(i => i.ServiceId == null && i.ProjectId == null).ToList();
        }
        else if (activeKey.StartsWith("hizmet-", StringComparison.Ordinal))
        {
            var slug = activeKey["hizmet-".Length..]; // dilim lambda DIŞINDA (CS8790)
            var id = await _db.Services.AsNoTracking()
                .Where(s => s.Slug == slug).Select(s => (int?)s.Id).FirstOrDefaultAsync();
            list = id is null ? new List<GalleryImage>() : images.Where(i => i.ServiceId == id).ToList();
        }
        else if (activeKey.StartsWith("proje-", StringComparison.Ordinal))
        {
            var slug = activeKey["proje-".Length..];
            var id = await _db.Projects.AsNoTracking()
                .Where(p => p.Slug == slug).Select(p => (int?)p.Id).FirstOrDefaultAsync();
            list = id is null ? new List<GalleryImage>() : images.Where(i => i.ProjectId == id).ToList();
        }
        // bilinmeyen k değerinde filtre uygulanmaz → tümü gösterilir

        var activeName = categories.FirstOrDefault(c => c.Key == activeKey).Name ?? "Tümü";
        ViewBag.Categories = categories;
        ViewBag.ActiveKey = activeKey;
        ViewData["GalleryTitle"] = activeName;
        return View(list);
    }
}