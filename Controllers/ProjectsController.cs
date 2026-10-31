using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Seo;
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
        var rows = await ProjectQueries.PublishedAsync(_db);
        ViewData["Seo"] = new SeoViewModel { Title="Projelerimiz | Karakuş Alüminyum", Description="Karakuş Alüminyum'un alüminyum doğrama ve mimari sistem uygulamalarını proje portföyünde inceleyin.", CanonicalPath="/projeler", PageType="CollectionPage", Breadcrumbs=new[]{("Projeler","/projeler")}, Schema=new[]{SchemaNodes.ItemList("/projeler", rows.Select(x=>("/projeler/"+x.Slug,x.Title)))} };
        return View((IReadOnlyList<ProjectItem>)rows);
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var x=await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p=>p.IsPublished && p.Slug==slug);
        if(x is null) return NotFound();
        var gallery = await _db.GalleryImages.AsNoTracking()
            .Where(g => g.ProjectId == x.Id && g.IsPublished)
            .OrderBy(g => g.SortOrder).ToListAsync();
        var image = x.Image is null or ProjectQueries.PlaceholderImage ? gallery.FirstOrDefault()?.ImagePath ?? ProjectQueries.PlaceholderImage : x.Image;
        var project=new ProjectItem(x.Slug,x.Title,x.Type,x.Summary,image,x.Location);
        ViewData["Seo"] = new SeoViewModel { Title=project.Title+" | Karakuş Alüminyum", Description=project.Summary, CanonicalPath="/projeler/"+project.Slug, ImagePath=project.Image, Type="article", ModifiedUtc=x.UpdatedUtc, Breadcrumbs=new[]{("Projeler","/projeler"),(project.Title,"/projeler/"+project.Slug)}, Schema=new[]{SchemaNodes.Project(project.Title,project.Summary,project.Type,project.Location,project.Image,"/projeler/"+project.Slug,x.CreatedUtc,x.UpdatedUtc)} };
        ViewBag.Content=x.Content;
        var others = await ProjectQueries.PublishedAsync(_db);
        var index = others.FindIndex(p => p.Slug == x.Slug);
        ViewBag.Next = others.Count > 1 ? others[(index + 1) % others.Count] : null;
        ViewBag.Gallery = gallery.Where(g => g.ImagePath != image).ToList(); // kapak görseli galeride tekrar etmesin
        return View(project);
    }
}
