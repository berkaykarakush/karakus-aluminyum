using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Seo;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var faqs = await _db.FaqItems.AsNoTracking()
            .Where(f => f.ServiceId == null && f.IsPublished)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync();
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Karakuş Alüminyum | İzmir Mimari Alüminyum Sistemleri",
            Description = "İzmir'de alüminyum ve PVC doğrama, cam balkon, giyotin ve sürme cam sistemleri ile otomatik kapı çözümleri. Projeniz için ölçü ve teklif alın.",
            CanonicalPath = "/",
            ImagePath = "/images/aluminyum-dograma.png",
            Schema = new[] { SchemaNodes.FaqPage("/", faqs) }
        };
        ViewBag.Faqs = faqs;
        ViewBag.Projects = await ProjectQueries.PublishedAsync(_db, take: 3);
        return View(await _db.Services.AsNoTracking().Where(s => s.IsPublished).OrderBy(s => s.SortOrder).ToListAsync());
    }

    [Route("kurumsal")]
    public async Task<IActionResult> About()
    {
        ViewBag.Services = await _db.Services.AsNoTracking().Where(s => s.IsPublished).OrderBy(s => s.SortOrder).ToListAsync();
        // Görseli olmayan proje vitrinde yer almaz.
        ViewBag.Projects = (await ProjectQueries.PublishedAsync(_db)).Where(p => p.Image != ProjectQueries.PlaceholderImage).Take(3).ToList();
        ViewData["Seo"] = new SeoViewModel
        {
            Title = "Hakkımızda | Karakuş Alüminyum",
            Description = "Karakuş Alüminyum'un çalışma yaklaşımı, hizmet anlayışı ve mimari alüminyum çözümleri hakkında bilgi alın.",
            CanonicalPath = "/kurumsal",
            PageType = "AboutPage",
            Breadcrumbs = new[] { ("Hakkımızda", "/kurumsal") }
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

    [Route("hata/{code:int}")]
    public IActionResult ErrorPage(int code)
    {
        // Durum kodu korunur: arama motoru var olmayan sayfayı 200 olarak görmez.
        if (code == 404) return View("~/Views/Shared/NotFound.cshtml");
        Response.StatusCode = code is >= 400 and < 600 ? code : 500;
        ViewData["Seo"] = new SeoViewModel { Title = "Bir sorun oluştu | Karakuş Alüminyum", Description = "İsteğiniz tamamlanamadı.", CanonicalPath = "/hata/500", Robots = "noindex,nofollow" };
        return View("~/Views/Shared/Error.cshtml");
    }
}
