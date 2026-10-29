using KarakusAluminyum.Web.Services;using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Areas.Admin.Controllers;
[Area("Admin"), Authorize(Roles="Admin,Editor"), Route("yonetim/hizmetler")]
public class ServicesController : Controller
{
 private readonly AppDbContext _db; private readonly IWebHostEnvironment _env; public ServicesController(AppDbContext db, IWebHostEnvironment env){_db=db;_env=env;}
 [HttpGet("")] public async Task<IActionResult> Index()=>View(await _db.Services.OrderBy(x=>x.SortOrder).ToListAsync());
 [HttpGet("ekle")] public IActionResult Create()=>View("Edit",new ServiceContent());
 [HttpPost("ekle"),ValidateAntiForgeryToken] public async Task<IActionResult> Create(ServiceContent m, IFormFile? imageFile){if(!ModelState.IsValid)return View("Edit",m);try{m.Image=await AdminImageUpload.SaveAsync(imageFile,_env) ?? m.Image;}catch(InvalidOperationException ex){ModelState.AddModelError("Image",ex.Message);return View("Edit",m);} m.Slug=Slug(m.Slug);m.CreatedUtc=m.UpdatedUtc=DateTime.UtcNow;_db.Services.Add(m);await _db.SaveChangesAsync();return RedirectToAction(nameof(Index));}
 [HttpGet("duzenle/{id:int}")] public async Task<IActionResult> Edit(int id){var m=await _db.Services.FindAsync(id);return m is null?NotFound():View(m);}
 [HttpPost("duzenle/{id:int}"),ValidateAntiForgeryToken] public async Task<IActionResult> Edit(int id,ServiceContent m, IFormFile? imageFile){if(id!=m.Id)return BadRequest();if(!ModelState.IsValid)return View(m);var row=await _db.Services.FindAsync(id);if(row is null)return NotFound();try{var uploaded=await AdminImageUpload.SaveAsync(imageFile,_env);if(uploaded is not null)m.Image=uploaded;}catch(InvalidOperationException ex){ModelState.AddModelError("Image",ex.Message);return View(m);}row.Name=m.Name;row.Slug=Slug(m.Slug);row.ShortDescription=m.ShortDescription;row.Intro=m.Intro;row.Image=m.Image;row.SeoTitle=m.SeoTitle;row.SeoDescription=m.SeoDescription;row.BenefitsJson=m.BenefitsJson;row.FaqsJson=m.FaqsJson;row.IsPublished=m.IsPublished;row.SortOrder=m.SortOrder;row.UpdatedUtc=DateTime.UtcNow;await _db.SaveChangesAsync();return RedirectToAction(nameof(Index));}
 [HttpPost("sil/{id:int}"),ValidateAntiForgeryToken] public async Task<IActionResult> Delete(int id){var m=await _db.Services.FindAsync(id);if(m is not null){_db.Remove(m);await _db.SaveChangesAsync();}return RedirectToAction(nameof(Index));}
 private static string Slug(string value)=>string.Join('-',value.Trim().ToLowerInvariant().Replace('ı','i').Replace('ğ','g').Replace('ü','u').Replace('ş','s').Replace('ö','o').Replace('ç','c').Split(' ',StringSplitOptions.RemoveEmptyEntries));
}
