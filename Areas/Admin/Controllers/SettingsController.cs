using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin"), Route("yonetim/ayarlar")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public SettingsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await VisibleRecordsAsync());

    [HttpPost("kaydet"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Dictionary<string, string?> values)
    {
        var rows = await VisibleRecordsAsync();
        foreach (var row in rows)
        {
            var definition = SiteSettingCatalog.Find(row.Key);
            // İşaretlenmeyen onay kutusu forma gelmez: kapalı demektir.
            var value = definition?.Kind == SettingKind.Toggle
                ? (values.TryGetValue(row.Key, out var toggle) && toggle == "true" ? "true" : "false")
                : values.TryGetValue(row.Key, out var posted) ? posted?.Trim() ?? "" : row.Value;
            if (definition is not null && SiteSettingCatalog.Validate(definition, value) is { } error) ModelState.AddModelError(string.Empty, error);
            if (value.Length > 2000) ModelState.AddModelError(string.Empty, $"{definition?.Label ?? row.Key}: en fazla 2000 karakter olabilir.");
            row.Value = value;
        }
        if (!ModelState.IsValid) return View("Index", rows); // yazılanlar kaybolmasın; kayıt yapılmaz

        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "SiteSettings", null, "Site ayarları güncellendi");
        TempData["Notice"] = "Ayarlar kaydedildi ve sitede hemen geçerli oldu.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<SiteSettingRecord>> VisibleRecordsAsync()
        => _db.SiteSettingRecords.Where(x => !SeedData.InternalSettingKeys.Contains(x.Key)).OrderBy(x => x.Key).ToListAsync();
}
