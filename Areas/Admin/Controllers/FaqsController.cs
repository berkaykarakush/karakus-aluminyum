using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/sss")]
public sealed class FaqsController : Controller
{
    private const string HomePage = "genel";

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public FaqsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // sayfa: boş = tümü, "genel" = ana sayfa, sayı = hizmet Id'si
    [HttpGet("")]
    public async Task<IActionResult> Index(string? sayfa)
    {
        var query = _db.FaqItems.AsNoTracking().Include(x => x.Service).AsQueryable();
        if (sayfa == HomePage) query = query.Where(x => x.ServiceId == null);
        else if (int.TryParse(sayfa, out var serviceId)) query = query.Where(x => x.ServiceId == serviceId);

        ViewBag.Page = sayfa ?? "";
        await LoadServices();
        return View(await query.OrderBy(x => x.ServiceId != null).ThenBy(x => x.Service!.SortOrder)
            .ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(int? hizmet)
    {
        await LoadServices();
        return View("Edit", new FaqInputVm { ServiceId = hizmet });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FaqInputVm input)
    {
        await ValidateService(input);
        if (!ModelState.IsValid) { await LoadServices(); return View("Edit", input); }

        var row = new FaqItem { ServiceId = input.ServiceId, Question = input.Question.Trim(), Answer = input.Answer.Trim(), SortOrder = input.SortOrder, IsPublished = input.IsPublished };
        _db.FaqItems.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Faq", row.Id.ToString(), row.Question);
        return BackToList(row.ServiceId);
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.FaqItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadServices();
        return View(new FaqInputVm { Id = row.Id, ServiceId = row.ServiceId, Question = row.Question, Answer = row.Answer, SortOrder = row.SortOrder, IsPublished = row.IsPublished });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FaqInputVm input)
    {
        var row = await _db.FaqItems.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        await ValidateService(input);
        if (!ModelState.IsValid) { await LoadServices(); return View(input); }

        row.ServiceId = input.ServiceId;
        row.Question = input.Question.Trim();
        row.Answer = input.Answer.Trim();
        row.SortOrder = input.SortOrder;
        row.IsPublished = input.IsPublished;
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Faq", row.Id.ToString(), row.Question);
        return BackToList(row.ServiceId);
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.FaqItems.FindAsync(id);
        if (row is null) return NotFound();
        _db.FaqItems.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "Faq", id.ToString(), row.Question);
        return BackToList(row.ServiceId);
    }

    private RedirectToActionResult BackToList(int? serviceId)
        => RedirectToAction(nameof(Index), new { sayfa = serviceId?.ToString() ?? HomePage });

    private async Task ValidateService(FaqInputVm input)
    {
        if (input.ServiceId is not null && !await _db.Services.AnyAsync(x => x.Id == input.ServiceId))
            ModelState.AddModelError(nameof(input.ServiceId), "Geçerli bir hizmet seçin.");
    }

    private async Task LoadServices()
        => ViewBag.Services = new SelectList(await _db.Services.AsNoTracking().OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
}
