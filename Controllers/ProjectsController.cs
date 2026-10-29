using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Controllers;
[Route("projeler")]
public class ProjectsController : Controller
{
    private readonly AppDbContext _db;
    public ProjectsController(AppDbContext db) => _db=db;
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel { Title="Projelerimiz | Karakuş Alüminyum", Description="Karakuş Alüminyum'un alüminyum doğrama ve mimari sistem uygulamalarını proje portföyünde inceleyin.", CanonicalPath="/projeler" };
        var items=await _db.Projects.AsNoTracking().Where(x=>x.IsPublished).OrderByDescending(x=>x.CreatedUtc).Select(x=>new ProjectItem(x.Slug,x.Title,x.Type,x.Summary,x.Image,x.Location)).ToListAsync();
        return View((IReadOnlyList<ProjectItem>)items);
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var x=await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p=>p.IsPublished && p.Slug==slug);
        if(x is null) return NotFound();
        var project=new ProjectItem(x.Slug,x.Title,x.Type,x.Summary,x.Image,x.Location);
        ViewData["Seo"] = new SeoViewModel { Title=project.Title+" | Karakuş Alüminyum", Description=project.Summary, CanonicalPath="/projeler/"+project.Slug, ImagePath=project.Image, Type="article" };
        ViewBag.Content=x.Content;
        ViewBag.Gallery = await _db.GalleryImages.AsNoTracking()
            .Where(g => g.ProjectId == x.Id && g.IsPublished)
            .OrderBy(g => g.SortOrder).ToListAsync();
        return View(project);
    }
}
