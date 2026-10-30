using KarakusAluminyum.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record SearchHit(string Group, string Title, string Detail, string Url);

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/ara")]
public sealed class SearchController : Controller
{
    private const int PerGroup = 10;
    private readonly AppDbContext _db;
    public SearchController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q)
    {
        var term = q?.Trim() ?? "";
        ViewBag.Query = term;
        var hits = new List<SearchHit>();
        if (term.Length < 2) return View(hits);

        // LIKE joker karakterleri arama metninde düz karakter olarak kalır.
        var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        hits.AddRange((await _db.Customers.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like) || EF.Functions.ILike(x.Phone ?? "", like) || EF.Functions.ILike(x.Email ?? "", like)).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Müşteriler", x.Name, x.Phone ?? x.Email ?? "", $"/yonetim/crm/musteriler/duzenle/{x.Id}")));
        hits.AddRange((await _db.Quotes.AsNoTracking().Include(x => x.Customer).Where(x => EF.Functions.ILike(x.QuoteNumber, like) || EF.Functions.ILike(x.Title, like) || EF.Functions.ILike(x.Customer!.Name, like)).OrderByDescending(x => x.CreatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Teklifler", $"{x.QuoteNumber} · {x.Title}", x.Customer?.Name ?? "", $"/yonetim/crm/teklifler/detay/{x.Id}")));
        hits.AddRange((await _db.Contracts.AsNoTracking().Where(x => EF.Functions.ILike(x.ContractNumber, like) || EF.Functions.ILike(x.Title, like) || EF.Functions.ILike(x.BuyerName, like)).OrderByDescending(x => x.CreatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Sözleşmeler", $"{x.ContractNumber} · {x.Title}", x.BuyerName, $"/yonetim/sozlesmeler/detay/{x.Id}")));
        hits.AddRange((await _db.DrawingLists.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like)).OrderByDescending(x => x.UpdatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Çizim listeleri", x.Name, "", $"/yonetim/cizim/detay/{x.Id}")));
        hits.AddRange((await _db.WindowDesigns.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like)).OrderByDescending(x => x.UpdatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Çizimler", x.Name, KarakusAluminyum.Web.Models.Drawing.ProductCategories.Label(x.Category), $"/yonetim/cizim/poz/{x.Id}")));
        hits.AddRange((await _db.GlassLists.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like)).OrderByDescending(x => x.UpdatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Cam listeleri", x.Name, "", $"/yonetim/camlar/detay/{x.Id}")));
        hits.AddRange((await _db.ContactMessages.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like) || EF.Functions.ILike(x.ContactInfo, like) || EF.Functions.ILike(x.Message, like)).OrderByDescending(x => x.CreatedUtc).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("İletişim mesajları", x.Name, x.ContactInfo, $"/yonetim/mesajlar/{x.Id}")));
        hits.AddRange((await _db.Services.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, like)).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Hizmetler", x.Name, "", $"/yonetim/hizmetler/duzenle/{x.Id}")));
        hits.AddRange((await _db.Projects.AsNoTracking().Where(x => EF.Functions.ILike(x.Title, like)).Take(PerGroup).ToListAsync())
            .Select(x => new SearchHit("Projeler", x.Title, "", $"/yonetim/projeler/duzenle/{x.Id}")));
        return View(hits);
    }
}
