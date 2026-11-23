using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Malzeme ihtiyacı: kritik stok ve açık siparişlerin metrajına göre sipariş önerisi; seçilenler tedarikçiye taslak alış faturası olur.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/uretim/malzeme-ihtiyaci")]
public sealed class MaterialNeedsController : Controller
{
    private readonly AppDbContext _db;
    private readonly MaterialPlanning _planning;
    private readonly IAuditService _audit;
    public MaterialNeedsController(AppDbContext db, MaterialPlanning planning, IAuditService audit) { _db = db; _planning = planning; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Suppliers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.PartyType != PartyType.Customer).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Rates = await PriceLists.LatestRatesAsync(_db);
        return View(await _planning.ReorderAsync());
    }

    // Formda seçilen her satır: miktar_{ürün} alanı; işaretsiz veya 0 olanlar atlanır.
    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Order(int? supplierId, List<int>? productIds)
    {
        if (supplierId is not int supplier || !await _db.Customers.AnyAsync(x => x.Id == supplier && x.PartyType != PartyType.Customer))
        {
            TempData["Notice"] = "Tedarikçi cari seçin.";
            return RedirectToAction(nameof(Index));
        }
        var ids = (productIds ?? new()).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(x => ids.Contains(x.Id) && x.Kind == ProductKind.Stock).ToDictionaryAsync(x => x.Id);
        var lines = ids.Where(products.ContainsKey)
            .Select(id => (Product: products[id], Quantity: decimal.TryParse(Request.Form[$"miktar_{id}"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : 0))
            .Where(x => x.Quantity > 0).ToList();
        if (lines.Count == 0)
        {
            TempData["Notice"] = "Sipariş verilecek satır seçin.";
            return RedirectToAction(nameof(Index));
        }
        var (invoice, missingRate) = await _planning.PurchaseDraftAsync(supplier, lines, "Malzeme ihtiyacı ekranından (kritik stok ve açık siparişler).");
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Invoice", invoice.Id.ToString(), $"Malzeme ihtiyacı: {lines.Count} kalem");
        TempData["Notice"] = $"{lines.Count} kalemlik taslak alış faturası açıldı (taslak no {invoice.Id}). Tedarikçinin fatura numarasını girip onaylayınca stok artar."
            + (missingRate.Count > 0 ? $" {string.Join(", ", missingRate)} kuru girilmediği için bu kalemlerin fiyatı 0 yazıldı." : "");
        return RedirectToAction(nameof(Index));
    }
}
