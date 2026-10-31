using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

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
    public IActionResult Create() => View("Edit", new Customer());

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer model)
    {
        if (!ModelState.IsValid) return View("Edit", model);
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
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer input)
    {
        if (id != input.Id) return BadRequest();
        if (!ModelState.IsValid) return View(input);
        var model = await _db.Customers.FindAsync(id);
        if (model is null) return NotFound();
        model.Name = input.Name; model.Kind = input.Kind; model.Status = input.Status; model.PartyType = input.PartyType;
        model.ContactPerson = input.ContactPerson; model.Email = input.Email; model.Phone = input.Phone;
        model.TaxNumber = input.TaxNumber; model.TaxOffice = input.TaxOffice; model.Address = input.Address;
        model.City = input.City; model.District = input.District; model.Source = input.Source; model.Notes = input.Notes;
        model.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Customer", model.Id.ToString(), model.Name);
        TempData["Notice"] = "Müşteri güncellendi.";
        return RedirectToAction(nameof(Index));
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
