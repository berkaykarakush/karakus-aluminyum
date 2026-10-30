using KarakusAluminyum.Web.Data;using KarakusAluminyum.Web.Models;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles="Admin"),Route("yonetim/ayarlar")]
public class SettingsController:Controller{private readonly AppDbContext _db;public SettingsController(AppDbContext db)=>_db=db;
[HttpGet("")]public async Task<IActionResult>Index()=>View(await _db.SiteSettingRecords.Where(x=>!SeedData.InternalSettingKeys.Contains(x.Key)).OrderBy(x=>x.Key).ToListAsync());
[HttpPost("kaydet"),ValidateAntiForgeryToken]public async Task<IActionResult>Save(List<SiteSettingRecord> items){foreach(var item in items){var row=await _db.SiteSettingRecords.FindAsync(item.Id);if(row is not null){row.Value=item.Value;row.Description=item.Description;}}await _db.SaveChangesAsync();TempData["Notice"]="Ayarlar veritabanına kaydedildi. appsettings kullanan alanlar için ayrıca yapılandırmayı güncellemeniz gerekir.";return RedirectToAction(nameof(Index));}}
