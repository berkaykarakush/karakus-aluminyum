using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Controllers;
[Route("blog")]
public class BlogController : Controller
{
    private readonly AppDbContext _db;
    public BlogController(AppDbContext db) => _db=db;
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel { Title="Bilgi Merkezi | Karakuş Alüminyum", Description="Alüminyum doğrama, sürme sistemler, cephe çözümleri ve ürün seçimi hakkında bilgilendirici içerikler.", CanonicalPath="/blog" };
        return View(await _db.BlogPosts.AsNoTracking().Where(x=>x.IsPublished).OrderByDescending(x=>x.PublishedUtc).ToListAsync());
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var post=await _db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x=>x.IsPublished && x.Slug==slug);
        if(post is null) return NotFound();
        ViewData["Seo"] = new SeoViewModel { Title=string.IsNullOrWhiteSpace(post.SeoTitle)?post.Title:post.SeoTitle, Description=string.IsNullOrWhiteSpace(post.SeoDescription)?post.Summary:post.SeoDescription, CanonicalPath="/blog/"+post.Slug, ImagePath=post.Image, Type="article" };
        return View("Detail",post);
    }
}
