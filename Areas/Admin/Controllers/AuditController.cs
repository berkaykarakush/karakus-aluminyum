using KarakusAluminyum.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin"), Route("yonetim/denetim-kaydi")]
public sealed class AuditController : Controller
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.AuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync());
}
