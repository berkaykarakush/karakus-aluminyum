using KarakusAluminyum.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace KarakusAluminyum.Web.Areas.Admin.Controllers;
[Area("Admin"), Authorize(Roles="Admin,Editor"), Route("yonetim")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db)=>_db=db;
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(new DashboardStats { Services=await _db.Services.CountAsync(), Projects=await _db.Projects.CountAsync(), Posts=await _db.BlogPosts.CountAsync(), UnreadMessages=await _db.ContactMessages.CountAsync(x=>!x.IsRead) });
}
public sealed class DashboardStats { public int Services {get;set;} public int Projects {get;set;} public int Posts {get;set;} public int UnreadMessages {get;set;} }
