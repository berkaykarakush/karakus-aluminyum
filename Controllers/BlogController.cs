using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services.Seo;
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
        var rows = await _db.BlogPosts.AsNoTracking().Where(x=>x.IsPublished).OrderByDescending(x=>x.PublishedUtc).ToListAsync();
        ViewData["Seo"] = new SeoViewModel { Title="Bilgi Merkezi | Karakuş Alüminyum", Description="Alüminyum doğrama, sürme sistemler, cephe çözümleri ve ürün seçimi hakkında bilgilendirici içerikler.", CanonicalPath="/blog", PageType="CollectionPage", Breadcrumbs=new[]{("Bilgi Merkezi","/blog")}, Schema=new[]{SchemaNodes.ItemList("/blog", rows.Select(x=>("/blog/"+x.Slug,x.Title)))} };
        return View(rows);
    }
    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var post=await _db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x=>x.IsPublished && x.Slug==slug);
        if(post is null) return NotFound();
        ViewData["Seo"] = new SeoViewModel { Title=string.IsNullOrWhiteSpace(post.SeoTitle)?post.Title:post.SeoTitle, Description=string.IsNullOrWhiteSpace(post.SeoDescription)?post.Summary:post.SeoDescription, CanonicalPath="/blog/"+post.Slug, ImagePath=post.Image, Type="article", PublishedUtc=post.PublishedUtc??post.CreatedUtc, ModifiedUtc=post.UpdatedUtc, Breadcrumbs=new[]{("Bilgi Merkezi","/blog"),(post.Title,"/blog/"+post.Slug)}, Schema=new[]{SchemaNodes.Article(post,"/blog/"+post.Slug)} };
        return View("Detail",post);
    }
}
