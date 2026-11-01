using System.Globalization;
using System.Text;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Accounting;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/projeler")]
public sealed class CostProjectsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingQueries _queries;
    private readonly IAuditService _audit;
    public CostProjectsController(AppDbContext db, AccountingQueries queries, IAuditService audit) { _db = db; _queries = queries; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(CostProjectStatus? durum)
    {
        ViewBag.Status = durum;
        return View(await _queries.ProjectSummariesAsync(durum));
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        await LoadOptions();
        return View("Edit", new CostProject());
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CostProject input)
    {
        input.Id = 0;
        await ValidateLinksAsync(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View("Edit", input); }
        var row = new CostProject();
        Apply(row, input);
        _db.CostProjects.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "CostProject", row.Id.ToString(), row.Name);
        return RedirectToAction(nameof(Details), new { id = row.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.CostProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions();
        return View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CostProject input)
    {
        var row = await _db.CostProjects.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        input.Id = id;
        await ValidateLinksAsync(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View(input); }
        Apply(row, input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "CostProject", id.ToString(), row.Name);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id, bool csv = false)
    {
        var analysis = await _queries.ProjectAnalysisAsync(id);
        if (analysis is null) return NotFound();
        if (!csv) return View(analysis);
        var text = new StringBuilder().AppendLine(string.Join(';', new[] { "Tür", "Tarih", "Kaynak", "Grup", "Belge", "Cari", "Açıklama", "Tutar (KDV hariç)" }.Select(ReportsController.Cell)));
        void Write(string type, IEnumerable<ProjectEntry> rows)
        {
            foreach (var x in rows)
                text.AppendLine(string.Join(';', new[] { type, x.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), x.Source, x.Group, x.Document, x.Party ?? "", x.Description, x.Amount.ToString("0.00", TurkishNumbers.Culture) }.Select(ReportsController.Cell)));
        }
        Write("Gelir", analysis.Revenues);
        Write("Maliyet", analysis.Costs);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(), "text/csv; charset=utf-8", $"proje-analizi-{id}.csv");
    }

    [HttpPost("kalem/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int id, DateOnly date, ProjectCostKind kind, string? description, decimal quantity, string? unit, decimal unitCost)
    {
        if (!await _db.CostProjects.AnyAsync(x => x.Id == id)) return NotFound();
        var name = description?.Trim() ?? "";
        var unitName = string.IsNullOrWhiteSpace(unit) ? "Adet" : unit.Trim();
        var error = !ModelState.IsValid || !Enum.IsDefined(kind) || date == default ? "Kalem bilgilerini kontrol edin."
            : name.Length is 0 or > 300 ? "Kalem açıklaması yazın (en fazla 300 karakter)."
            : unitName.Length > 20 ? "Birim en fazla 20 karakter olabilir."
            : quantity is <= 0 or > 100_000_000 ? "Miktar sıfırdan büyük olmalıdır."
            : unitCost is <= 0 or > 1_000_000_000 ? "Birim maliyet sıfırdan büyük olmalıdır."
            : null;
        if (error is not null) TempData["Notice"] = error;
        else
        {
            var item = new ProjectCostItem { ProjectId = id, Date = date, Kind = kind, Description = name, Quantity = quantity, Unit = unitName, UnitCost = unitCost, Total = InvoiceRules.Money(quantity * unitCost) };
            _db.ProjectCostItems.Add(item);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("create", "ProjectCostItem", item.Id.ToString(), $"Proje {id}; {name}; {item.Total} TL");
            TempData["Notice"] = "Kalem eklendi.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("kalem-sil/{id:int}/{itemId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var item = await _db.ProjectCostItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ProjectId == id);
        if (item is null) return NotFound();
        _db.ProjectCostItems.Remove(item);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ProjectCostItem", itemId.ToString(), $"Proje {id}; {item.Description}; {item.Total} TL");
        TempData["Notice"] = "Kalem silindi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.CostProjects.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (await _db.Invoices.AnyAsync(x => x.ProjectId == id) || await _db.Expenses.AnyAsync(x => x.ProjectId == id) || await _db.ProjectCostItems.AnyAsync(x => x.ProjectId == id))
        {
            TempData["Notice"] = "Bağlı fatura, gider veya kalemi olan proje silinemez; durumunu İptal yapabilirsiniz.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.CostProjects.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "CostProject", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    private static void Apply(CostProject row, CostProject input)
    {
        row.Name = input.Name.Trim();
        row.Status = input.Status;
        row.CustomerId = input.CustomerId;
        row.QuoteId = input.QuoteId;
        row.ContractId = input.ContractId;
        row.TargetMargin = input.TargetMargin;
        row.VatRate = input.VatRate;
        row.Notes = input.Notes?.Trim();
    }

    // Cari boşsa seçilen belgeden alınır; farklı bir carinin belgesi bağlanamaz.
    private async Task ValidateLinksAsync(CostProject input)
    {
        if (!Enum.IsDefined(input.Status)) ModelState.AddModelError(nameof(input.Status), "Geçersiz durum.");
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) ModelState.AddModelError(nameof(input.CustomerId), "Cari bulunamadı.");
        async Task Check(int? documentId, Func<int, Task<int?>> ownerOf, string field, string name)
        {
            if (documentId is null) return;
            var owner = await ownerOf(documentId.Value);
            if (owner is null) ModelState.AddModelError(field, $"{name} bulunamadı.");
            else if (input.CustomerId is null) input.CustomerId = owner;
            else if (input.CustomerId != owner) ModelState.AddModelError(field, $"Seçilen {name.ToLower(TurkishNumbers.Culture)} bu cariye ait değil.");
        }
        await Check(input.QuoteId, id => _db.Quotes.Where(x => x.Id == id).Select(x => (int?)x.CustomerId).FirstOrDefaultAsync(), nameof(input.QuoteId), "Teklif");
        await Check(input.ContractId, id => _db.Contracts.Where(x => x.Id == id).Select(x => (int?)x.CustomerId).FirstOrDefaultAsync(), nameof(input.ContractId), "Sözleşme");
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Quotes = new SelectList(await _db.Quotes.AsNoTracking().OrderByDescending(x => x.Id).Take(500).Select(x => new { x.Id, Name = x.QuoteNumber + " - " + x.Title }).ToListAsync(), "Id", "Name");
        ViewBag.Contracts = new SelectList(await _db.Contracts.AsNoTracking().OrderByDescending(x => x.Id).Take(500).Select(x => new { x.Id, Name = x.ContractNumber + " - " + x.Title }).ToListAsync(), "Id", "Name");
    }
}
