using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Controllers;
[Route("urunler")]
public class ServicesController : Controller
{
    private readonly AppDbContext _db;
    public ServicesController(AppDbContext db) => _db = db;
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel { Title="Ürün ve Hizmetler | Karakuş Alüminyum", Description="Alüminyum doğrama, sürme sistemler, kapı-pencere, giydirme cephe, cam balkon ve otomatik kapı çözümlerini inceleyin.", CanonicalPath="/urunler" };
        var rows = await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).ToListAsync();
        return View(rows.Select(ToPage).ToList());
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var row = await _db.Services.AsNoTracking().FirstOrDefaultAsync(x=>x.IsPublished && x.Slug==slug);
        if (row is null) return NotFound();
        var service=ToPage(row);
        ViewData["Seo"] = new SeoViewModel { Title=string.IsNullOrWhiteSpace(service.SeoTitle)?service.Name+" | Karakuş Alüminyum":service.SeoTitle, Description=service.SeoDescription, CanonicalPath="/urunler/"+service.Slug, ImagePath=service.Image, Type="article" };
        ViewData["Service"] = service;
        ViewData["TrackEvent"] = "view_service";
        ViewData["TrackLabel"] = service.Name;
        ViewBag.Gallery = await _db.GalleryImages.AsNoTracking()
            .Where(g => g.ServiceId == row.Id && g.IsPublished && g.ImagePath != row.Image)
            .OrderBy(g => g.SortOrder).ToListAsync();
        ViewBag.Faqs = await _db.FaqItems.AsNoTracking()
            .Where(f => f.ServiceId == row.Id && f.IsPublished)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync();
        return View(service);
    }
    private static ServicePage ToPage(ServiceContent x) => new(x.Slug,x.Name,x.ShortDescription,x.Intro,x.Image ?? "/images/image-placeholder.svg",x.SeoTitle,x.SeoDescription,ParseList(x.BenefitsJson));
    private static IReadOnlyList<string> ParseList(string json) { try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); } catch { return new List<string>(); } }
}
