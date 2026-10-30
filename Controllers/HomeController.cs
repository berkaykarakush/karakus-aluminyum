using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Controllers;

public class HomeController : Controller
{
    private readonly SeoService _seo;
    private readonly AppDbContext _db;
    public HomeController(SeoService seo, AppDbContext db) { _seo = seo; _db = db; }

    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Karakuş Alüminyum | İzmir Mimari Alüminyum Sistemleri",
            Description = "Alüminyum doğrama, sürme sistemler, kapı ve pencere, giydirme cephe ve cam balkon çözümleri. Karakuş Alüminyum ile projenizi değerlendirin.",
            CanonicalPath = "/",
            ImagePath = "/images/aluminyum-dograma.png"
        };
        ViewData["BusinessSchema"] = _seo.LocalBusinessSchema();
        ViewBag.Faqs = await _db.FaqItems.AsNoTracking()
            .Where(f => f.ServiceId == null && f.IsPublished)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync();
        ViewBag.Projects = await ProjectQueries.PublishedAsync(_db, take: 3);
        return View(await _db.Services.AsNoTracking().Where(s => s.IsPublished).OrderBy(s => s.SortOrder).ToListAsync());
    }

    [Route("kurumsal")]
    public IActionResult About()
    {
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Hakkımızda | Karakuş Alüminyum",
            Description = "Karakuş Alüminyum'un çalışma yaklaşımı, hizmet anlayışı ve mimari alüminyum çözümleri hakkında bilgi alın.",
            CanonicalPath = "/kurumsal"
        };
        return View();
    }

    [Route("gizlilik-politikasi")]
    public IActionResult Privacy()
    {
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Gizlilik Politikası | Karakuş Alüminyum",
            Description = "Karakuş Alüminyum web sitesinin gizlilik ve veri işleme yaklaşımı.",
            CanonicalPath = "/gizlilik-politikasi",
            Robots = "noindex,follow"
        };
        return View("~/Views/Legal/Privacy.cshtml");
    }

    [Route("kvkk-aydinlatma-metni")]
    public IActionResult Kvkk()
    {
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "KVKK Aydınlatma Metni | Karakuş Alüminyum",
            Description = "İletişim formu kapsamında kişisel verilerin işlenmesine ilişkin aydınlatma metni.",
            CanonicalPath = "/kvkk-aydinlatma-metni",
            Robots = "noindex,follow"
        };
        return View("~/Views/Legal/Kvkk.cshtml");
    }

    [Route("robots.txt")]
    [ResponseCache(Duration = 3600)]
    public ContentResult Robots() =>
        Content($"User-agent: *\nAllow: /\nDisallow: /yonetim/\nSitemap: {_seo.Canonical("/sitemap.xml")}\n", "text/plain");

    [Route("sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<ContentResult> Sitemap()
    {
        var paths = new List<string> { "/", "/kurumsal", "/urunler", "/projeler", "/galeri", "/blog", "/iletisim" };
        paths.AddRange(await _db.Services.AsNoTracking().Where(s => s.IsPublished).Select(s => "/urunler/" + s.Slug).ToListAsync());
        paths.AddRange(await _db.Projects.AsNoTracking().Where(p => p.IsPublished).Select(p => "/projeler/" + p.Slug).ToListAsync());
        paths.AddRange(await _db.BlogPosts.AsNoTracking().Where(p => p.IsPublished).Select(p => "/blog/" + p.Slug).ToListAsync());
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var path in paths.Distinct())
            xml.AppendLine($"  <url><loc>{System.Security.SecurityElement.Escape(_seo.Canonical(path))}</loc></url>");
        xml.AppendLine("</urlset>");
        return Content(xml.ToString(), "application/xml; charset=utf-8");
    }

    [Route("hata/404")]
    public IActionResult NotFoundPage() => View("~/Views/Shared/NotFound.cshtml");
}
