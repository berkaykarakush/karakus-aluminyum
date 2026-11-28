using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record ReferralRow(int Id, string Name, string? Phone, int Count, int WonCount, decimal Revenue, IReadOnlyList<(int Id, string Name, decimal Won)> Customers);

[Area("Admin"), Authorize(Roles = "Admin,Editor,Accountant"), Route("yonetim/crm/musteriler")]
public sealed class CustomersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public CustomersController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CustomerStatus? status)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.Contains(q) || (x.ContactPerson != null && x.ContactPerson.Contains(q)) || (x.Phone != null && x.Phone.Contains(q)) || (x.Email != null && x.Email.Contains(q)));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        ViewBag.Query = q; ViewBag.Status = status;
        return View(await query.OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create() { await LoadOptionsAsync(null); return View("Edit", new Customer()); }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer model)
    {
        ModelState.Remove(nameof(model.ReferredBy));
        await CheckReferrerAsync(model);
        if (!ModelState.IsValid) { await LoadOptionsAsync(null); return View("Edit", model); }
        model.CreatedUtc = model.UpdatedUtc = DateTime.UtcNow;
        _db.Customers.Add(model); await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Customer", model.Id.ToString(), model.Name);
        TempData["Notice"] = "Müşteri kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.Customers.FindAsync(id);
        if (model is null) return NotFound();
        await LoadOptionsAsync(model);
        return View(model);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer input)
    {
        if (id != input.Id) return BadRequest();
        ModelState.Remove(nameof(input.ReferredBy));
        await CheckReferrerAsync(input);
        if (!ModelState.IsValid) { await LoadOptionsAsync(input); return View(input); }
        var model = await _db.Customers.FindAsync(id);
        if (model is null) return NotFound();
        model.Name = input.Name; model.Kind = input.Kind; model.Status = input.Status; model.PartyType = input.PartyType;
        model.ContactPerson = input.ContactPerson; model.Email = input.Email; model.Phone = input.Phone;
        model.TaxNumber = input.TaxNumber; model.TaxOffice = input.TaxOffice; model.Address = input.Address;
        model.City = input.City; model.District = input.District; model.Source = input.Source; model.ReferredByCustomerId = input.ReferredByCustomerId; model.Notes = input.Notes; model.NotificationsOptOut = input.NotificationsOptOut;
        model.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Customer", model.Id.ToString(), model.Name);
        TempData["Notice"] = "Müşteri güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Tavsiye eden müşteri kendisi olamaz ve kayıtlı bir müşteri olmalıdır. Tavsiye seçildiyse kaynak boşsa "Tavsiye" yazılır.
    private async Task CheckReferrerAsync(Customer input)
    {
        if (input.ReferredByCustomerId is not int referrer) return;
        if (referrer == input.Id || !await _db.Customers.AnyAsync(x => x.Id == referrer)) ModelState.AddModelError(nameof(input.ReferredByCustomerId), "Tavsiye eden olarak başka bir kayıtlı müşteri seçin.");
        else if (string.IsNullOrWhiteSpace(input.Source)) input.Source = "Tavsiye";
    }

    private async Task LoadOptionsAsync(Customer? model)
    {
        var id = model?.Id ?? 0;
        ViewBag.Referrers = await _db.Customers.AsNoTracking().Where(x => x.Id != id && x.PartyType != KarakusAluminyum.Web.Models.Accounting.PartyType.Supplier)
            .OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(x.Name + (x.Phone == null ? "" : " · " + x.Phone), x.Id.ToString())).ToListAsync();
        if (model is null || id == 0) return;
        ViewBag.Referred = (await _db.Customers.AsNoTracking().Where(x => x.ReferredByCustomerId == id).OrderByDescending(x => x.CreatedUtc).Select(x => new { x.Id, x.Name }).ToListAsync())
            .Select(x => (x.Id, x.Name)).ToList();
        ViewBag.Neighbors = await NeighborWork.FindAsync(_db, model.Address, model.District, excludeCustomerId: id);
    }

    // Tavsiye raporu: kim kaç müşteri getirdi, getirdikleri müşterilerden ne kadar iş çıktı.
    [HttpGet("tavsiyeler")]
    public async Task<IActionResult> Referrals()
    {
        var won = new[] { QuoteStatus.Accepted, QuoteStatus.Converted };
        var referred = await _db.Customers.AsNoTracking().Where(x => x.ReferredByCustomerId != null)
            .Select(x => new { x.Id, x.Name, Referrer = x.ReferredByCustomerId!.Value, x.CreatedUtc, Won = x.Quotes.Where(q => won.Contains(q.Status)).Sum(q => (decimal?)q.GrandTotal) ?? 0m, Quotes = x.Quotes.Count })
            .ToListAsync();
        var referrerIds = referred.Select(x => x.Referrer).Distinct().ToList();
        var referrers = await _db.Customers.AsNoTracking().Where(x => referrerIds.Contains(x.Id)).Select(x => new { x.Id, x.Name, x.Phone }).ToDictionaryAsync(x => x.Id);
        var rows = referred.GroupBy(x => x.Referrer).Select(g => new ReferralRow(g.Key, referrers.TryGetValue(g.Key, out var r) ? r.Name : "?", referrers.TryGetValue(g.Key, out var rp) ? rp.Phone : null,
                g.Count(), g.Count(x => x.Won > 0), g.Sum(x => x.Won), g.OrderByDescending(x => x.CreatedUtc).Select(x => (x.Id, x.Name, x.Won)).ToList()))
            .OrderByDescending(x => x.Revenue).ThenByDescending(x => x.Count).ToList();
        ViewBag.TaggedWithoutReferrer = await _db.Customers.CountAsync(x => x.Source == "Tavsiye" && x.ReferredByCustomerId == null);
        return View(rows);
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await _db.Customers.Include(x => x.Quotes).FirstOrDefaultAsync(x => x.Id == id);
        if (model is not null)
        {
            var hasAccounting = await _db.Invoices.AnyAsync(x => x.CustomerId == id) || await _db.CashTransactions.AnyAsync(x => x.CustomerId == id) || await _db.Cheques.AnyAsync(x => x.CustomerId == id || x.EndorsedToCustomerId == id)
                || await _db.Expenses.AnyAsync(x => x.CustomerId == id) || await _db.CariAdjustments.AnyAsync(x => x.CustomerId == id);
            if (hasAccounting) TempData["Notice"] = "Bu cariye bağlı muhasebe kayıtları var. Silmek yerine pasif duruma alın.";
            else if (model.Quotes.Count > 0) TempData["Notice"] = "Bu müşteriye bağlı teklifler var. Silmek yerine pasif duruma alın.";
            else { var deletedName = model.Name; _db.Customers.Remove(model); await _db.SaveChangesAsync(); await _audit.RecordAsync("delete", "Customer", id.ToString(), deletedName); TempData["Notice"] = "Müşteri silindi."; }
        }
        return RedirectToAction(nameof(Index));
    }
}
