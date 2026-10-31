using KarakusAluminyum.Web.Data;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Areas.Admin.Controllers;
[Area("Admin"),Authorize(Roles="Admin,Editor"),Route("yonetim/mesajlar")]
public class MessagesController:Controller{private readonly AppDbContext _db;public MessagesController(AppDbContext db)=>_db=db;
[HttpGet("")]public async Task<IActionResult>Index()=>View(await _db.ContactMessages.OrderByDescending(x=>x.CreatedUtc).ToListAsync());
[HttpGet("{id:int}")]public async Task<IActionResult>Detail(int id){var x=await _db.ContactMessages.FindAsync(id);if(x is null)return NotFound();x.IsRead=true;await _db.SaveChangesAsync();ViewBag.Jobs=await _db.NotificationJobs.AsNoTracking().Where(j=>j.ContactMessageId==id).OrderBy(j=>j.Id).ToListAsync();return View(x);}
[HttpPost("sil/{id:int}"),ValidateAntiForgeryToken]public async Task<IActionResult>Delete(int id){var x=await _db.ContactMessages.FindAsync(id);if(x is not null){_db.Remove(x);await _db.SaveChangesAsync();}return RedirectToAction(nameof(Index));}}
