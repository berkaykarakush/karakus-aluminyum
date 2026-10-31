using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Seo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Controllers;
[Route("urunler")]
public class ServicesController : Controller
{
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public ServicesController(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var rows = await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).ToListAsync();
        ViewData["Seo"] = new SeoViewModel { Title="Ürün ve Hizmetler | Karakuş Alüminyum", Description="Alüminyum ve PVC doğrama, cam balkon, giyotin ve sürme cam sistemleri, panjur, ofis bölme ve otomatik kapı çözümlerini inceleyin.", CanonicalPath="/urunler", PageType="CollectionPage", Breadcrumbs=new[]{("Ürün ve Hizmetler","/urunler")}, Schema=new[]{SchemaNodes.ItemList("/urunler", rows.Select(x=>("/urunler/"+x.Slug,x.Name)))} };
        return View(rows.Select(ToPage).ToList());
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var row = await _db.Services.AsNoTracking().FirstOrDefaultAsync(x=>x.IsPublished && x.Slug==slug);
        if (row is null) return NotFound();
        var service=ToPage(row);
        ViewData["Service"] = service;
        ViewData["TrackEvent"] = "view_service";
        ViewData["TrackLabel"] = service.Name;
        ViewBag.Gallery = await _db.GalleryImages.AsNoTracking()
            .Where(g => g.ServiceId == row.Id && g.IsPublished && g.ImagePath != row.Image)
            .OrderBy(g => g.SortOrder).ToListAsync();
        ViewBag.Faqs = await _db.FaqItems.AsNoTracking()
            .Where(f => f.ServiceId == row.Id && f.IsPublished)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync();
        // Sıralamada bu hizmetten sonra gelenler önce gösterilir; liste sonunda başa döner.
        var others = await _db.Services.AsNoTracking().Where(x=>x.IsPublished && x.Id!=row.Id).OrderBy(x=>x.SortOrder).ThenBy(x=>x.Id).ToListAsync();
        ViewBag.Related = others.Where(x=>x.SortOrder>row.SortOrder).Concat(others.Where(x=>x.SortOrder<=row.SortOrder)).Take(3).Select(ToPage).ToList();
        var description = string.IsNullOrWhiteSpace(service.SeoDescription) ? service.ShortDescription : service.SeoDescription;
        var path = "/urunler/" + service.Slug;
        var site = await _settings.GetAsync();
        ViewData["Seo"] = new SeoViewModel
        {
            Title = string.IsNullOrWhiteSpace(service.SeoTitle) ? service.Name + " | Karakuş Alüminyum" : service.SeoTitle, Description = description, CanonicalPath = path, ImagePath = service.Image, ModifiedUtc = row.UpdatedUtc,
            Breadcrumbs = new[] { ("Ürün ve Hizmetler", "/urunler"), (service.Name, path) },
            Schema = new[] { SchemaNodes.Service(service.Name, description, path, service.Image, site.City), SchemaNodes.FaqPage(path, (List<FaqItem>)ViewBag.Faqs) }
        };
        return View(service);
    }
    private static ServicePage ToPage(ServiceContent x) => new(x.Slug,x.Name,x.ShortDescription,x.Intro,x.Image ?? "/images/image-placeholder.svg",x.SeoTitle,x.SeoDescription,ParseList(x.BenefitsJson));
    private static IReadOnlyList<string> ParseList(string json) { try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); } catch { return new List<string>(); } }
}
