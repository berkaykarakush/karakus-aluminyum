using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/crm/teklifler")]
public sealed class QuotesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public QuotesController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(QuoteStatus? status)
    {
        ViewBag.Status = status;
        var query = _db.Quotes.AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return View(await query.OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        await LoadCustomers();
        return View(new QuoteInputViewModel { ValidUntilUtc = DateTime.UtcNow.Date.AddDays(14) });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(QuoteInputViewModel input)
    {
        var customerExists = await _db.Customers.AnyAsync(x => x.Id == input.CustomerId);
        if (!customerExists) ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        var items = input.Items.Where(x => !string.IsNullOrWhiteSpace(x.Description) && x.Quantity > 0).ToList();
        if (items.Count == 0) ModelState.AddModelError(nameof(input.Items), "En az bir teklif kalemi girin (açıklama ve miktar zorunludur).");
        if (!ModelState.IsValid) { await LoadCustomers(); return View(input); }

        var quote = new Quote
        {
            QuoteNumber = $"KA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            CustomerId = input.CustomerId, Title = input.Title.Trim(), Status = QuoteStatus.Draft,
            CreatedUtc = DateTime.UtcNow,
            ValidUntilUtc = input.ValidUntilUtc.HasValue ? DateTime.SpecifyKind(input.ValidUntilUtc.Value.Date, DateTimeKind.Utc) : null,
            Notes = input.Notes, TaxRate = input.TaxRate, DiscountRate = input.DiscountRate
        };
        foreach (var item in items)
        {
            var line = new QuoteItem { Description = item.Description.Trim(), Quantity = item.Quantity, Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Adet" : item.Unit.Trim(), UnitPrice = item.UnitPrice };
            quote.Items.Add(line);
        }
        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
        _db.Quotes.Add(quote); await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; total={quote.GrandTotal}");
        TempData["Notice"] = $"{quote.QuoteNumber} numaralı teklif oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = quote.Id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        return quote is null ? NotFound() : View(quote);
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, QuoteStatus status)
    {
        var quote = await _db.Quotes.FindAsync(id);
        if (quote is null) return NotFound();
        if (!Enum.IsDefined(status) || status == QuoteStatus.Converted)
        {
            TempData["Notice"] = "Geçersiz durum. Siparişe dönüşüm, sipariş modülü eklenene kadar kullanılamaz.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var allowed = quote.Status switch
        {
            QuoteStatus.Draft => status is QuoteStatus.Draft or QuoteStatus.Sent,
            QuoteStatus.Sent => status is QuoteStatus.Sent or QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired,
            QuoteStatus.Accepted => status == QuoteStatus.Accepted,
            QuoteStatus.Rejected => status == QuoteStatus.Rejected,
            QuoteStatus.Expired => status == QuoteStatus.Expired,
            QuoteStatus.Converted => false,
            _ => false
        };
        if (!allowed)
        {
            TempData["Notice"] = "Bu teklif için seçilen durum geçişine izin verilmiyor.";
            return RedirectToAction(nameof(Details), new { id });
        }

        quote.Status = status;
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Teklif durumu güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadCustomers() => ViewBag.Customers = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToListAsync();
}
