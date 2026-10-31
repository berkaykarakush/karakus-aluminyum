using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/faturalar")]
public sealed class InvoicesController : Controller
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly AccountingService _accounting;
    private readonly IAuditService _audit;
    public InvoicesController(AppDbContext db, AccountingService accounting, IAuditService audit) { _db = db; _accounting = accounting; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(InvoiceKind? tur, InvoiceStatus? durum, string? q, bool acik = false)
    {
        var query = _db.Invoices.AsNoTracking().Include(x => x.Customer).Include(x => x.Allocations).AsQueryable();
        if (tur is not null) query = query.Where(x => x.Kind == tur);
        if (durum is not null) query = query.Where(x => x.Status == durum);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Customer!.Name, $"%{q}%") || (x.Number != null && EF.Functions.ILike(x.Number, $"%{q}%")));
        var rows = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(500).ToListAsync();
        if (acik) rows = rows.Where(x => x.Status == InvoiceStatus.Approved && x.PayableTry > x.Allocations.Sum(a => a.Amount)).ToList();
        (ViewBag.Kind, ViewBag.Status, ViewBag.Query, ViewBag.Open) = (tur, durum, q, acik);
        return View(rows);
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(InvoiceKind tur = InvoiceKind.Sale, int? cari = null, int? teklif = null, int? sozlesme = null)
    {
        var input = new InvoiceInputVm { Kind = tur, CustomerId = cari ?? 0 };
        if (teklif is not null)
        {
            var quote = await _db.Quotes.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == teklif);
            if (quote is null) return NotFound();
            input.Kind = InvoiceKind.Sale;
            input.CustomerId = quote.CustomerId;
            input.QuoteId = quote.Id;
            input.Notes = $"Teklif: {quote.QuoteNumber}";
            input.LinesJson = JsonSerializer.Serialize(quote.Items.OrderBy(x => x.SortOrder).Select(x => FromDocument(x.Description, x.Quantity, x.Unit, x.UnitPrice, quote.DiscountRate, quote.TaxRate, quote.VatMode)), Json);
        }
        else if (sozlesme is not null)
        {
            var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sozlesme);
            if (contract is null) return NotFound();
            input.Kind = InvoiceKind.Sale;
            input.CustomerId = contract.CustomerId;
            input.ContractId = contract.Id;
            input.QuoteId = contract.QuoteId;
            input.Notes = $"Sözleşme: {contract.ContractNumber}";
            input.LinesJson = JsonSerializer.Serialize(new[] { FromDocument(contract.Title, 1, "Adet", contract.TotalAmount, 0, 20m, contract.VatMode) }, Json);
        }
        await LoadOptions(input.Kind);
        return View("Editor", input);
    }

    // Teklif KDV dahil yazıldıysa birim fiyat KDV hariçe çevrilir; fatura hep KDV hariç fiyatla hesaplanır.
    private static InvoiceLineInput FromDocument(string description, decimal quantity, string unit, decimal price, decimal discount, decimal taxRate, VatMode mode) => new()
    {
        Description = description.Length > 300 ? description[..300] : description, Quantity = quantity, Unit = unit, DiscountRate = discount,
        UnitPrice = mode == VatMode.Included ? decimal.Round(price / (1 + taxRate / 100m), 4, MidpointRounding.AwayFromZero) : price,
        VatRate = mode == VatMode.None ? 0 : taxRate
    };

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Create(InvoiceInputVm input)
    {
        input.Id = 0;
        var invoice = new Invoice { Kind = input.Kind };
        if (!await ApplyAsync(invoice, input)) { await LoadOptions(input.Kind); return View("Editor", input); }
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Invoice", invoice.Id.ToString(), $"{AccountingLabels.Label(invoice.Kind)}; {invoice.PayableTry} TL");
        return RedirectToAction(nameof(Details), new { id = invoice.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.Invoices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (row.Status != InvoiceStatus.Draft) { TempData["Notice"] = "Yalnızca taslak fatura düzenlenebilir."; return RedirectToAction(nameof(Details), new { id }); }
        await LoadOptions(row.Kind);
        return View("Editor", new InvoiceInputVm
        {
            Id = row.Id, Kind = row.Kind, CustomerId = row.CustomerId, Date = row.Date, DueDate = row.DueDate, Currency = row.Currency, ExchangeRate = row.ExchangeRate, Number = row.Number,
            EDocumentNumber = row.EDocumentNumber, Notes = row.Notes, QuoteId = row.QuoteId, ContractId = row.ContractId,
            LinesJson = JsonSerializer.Serialize(row.Lines.OrderBy(x => x.SortOrder).Select(x => new InvoiceLineInput { ProductId = x.ProductId, Description = x.Description, Quantity = x.Quantity, Unit = x.Unit, UnitPrice = x.UnitPrice, DiscountRate = x.DiscountRate, VatRate = x.VatRate, WithholdingRate = x.WithholdingRate }), Json)
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Edit(int id, InvoiceInputVm input)
    {
        var invoice = await _db.Invoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status != InvoiceStatus.Draft) { TempData["Notice"] = "Yalnızca taslak fatura düzenlenebilir."; return RedirectToAction(nameof(Details), new { id }); }
        input.Id = id;
        input.Kind = invoice.Kind; // tür sonradan değişmez
        if (!await ApplyAsync(invoice, input)) { await LoadOptions(input.Kind); return View("Editor", input); }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Invoice", id.ToString(), $"{invoice.PayableTry} TL");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await Load(id);
        return row is null ? NotFound() : View(row);
    }

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var row = await Load(id);
        return row is null ? NotFound() : View(row);
    }

    private Task<Invoice?> Load(int id) => _db.Invoices.AsNoTracking().Include(x => x.Customer).Include(x => x.Lines)
        .Include(x => x.Allocations).ThenInclude(x => x.CashTransaction).ThenInclude(x => x!.Account).Include(x => x.Allocations).ThenInclude(x => x.Cheque)
        .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);

    [HttpPost("onayla/{id:int}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(int id) => RunAsync(id, () => _accounting.ApproveAsync(id), "Fatura onaylandı.");

    [HttpPost("iptal/{id:int}"), ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, string? reason) => RunAsync(id, () => _accounting.CancelInvoiceAsync(id, reason), "Fatura iptal edildi.");

    // e-Belge numarası resmi fatura kesildikten sonra girilebilir; tutarlara dokunmaz.
    [HttpPost("e-belge/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EDocument(int id, string? number)
    {
        var row = await _db.Invoices.FindAsync(id);
        if (row is null) return NotFound();
        row.EDocumentNumber = string.IsNullOrWhiteSpace(number) ? null : number.Trim()[..Math.Min(number.Trim().Length, 60)];
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("e-document", "Invoice", id.ToString(), row.EDocumentNumber ?? "");
        TempData["Notice"] = "e-Belge numarası kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.Invoices.FindAsync(id);
        if (row is null) return NotFound();
        if (row.Status != InvoiceStatus.Draft) { TempData["Notice"] = "Onaylı fatura silinemez, iptal edilir."; return RedirectToAction(nameof(Details), new { id }); }
        _db.Invoices.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "Invoice", id.ToString(), "taslak");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> RunAsync(int id, Func<Task> action, string message)
    {
        try { await action(); TempData["Notice"] = message; }
        catch (AccountingException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> ApplyAsync(Invoice invoice, InvoiceInputVm input)
    {
        List<InvoiceLineInput>? lines = null;
        try { lines = JsonSerializer.Deserialize<List<InvoiceLineInput>>(input.LinesJson, Json); }
        catch (JsonException) { }
        if (lines is null) { ModelState.AddModelError(nameof(input.LinesJson), "Fatura satırları okunamadı."); return false; }
        if (!Enum.IsDefined(input.Kind)) ModelState.AddModelError(nameof(input.Kind), "Geçersiz fatura türü.");
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) ModelState.AddModelError(nameof(input.CustomerId), "Cari seçin.");
        foreach (var error in InvoiceRules.Validate(lines, input.ExchangeRate, input.Currency, input.Date, input.DueDate)) ModelState.AddModelError(string.Empty, error);
        var productIds = lines.Where(x => x.ProductId is not null).Select(x => x.ProductId!.Value).Distinct().ToList();
        if (await _db.Products.CountAsync(x => productIds.Contains(x.Id)) != productIds.Count) ModelState.AddModelError(string.Empty, "Satırlardaki ürünlerden biri bulunamadı.");
        if (!ModelState.IsValid) return false;

        invoice.CustomerId = input.CustomerId;
        invoice.Date = input.Date;
        invoice.DueDate = input.DueDate;
        invoice.Currency = input.Currency;
        invoice.ExchangeRate = input.ExchangeRate;
        invoice.Number = AccountingLabels.IsSaleSide(invoice.Kind) ? null : input.Number?.Trim(); // satış numarası onayda verilir
        invoice.EDocumentNumber = input.EDocumentNumber?.Trim();
        invoice.Notes = input.Notes?.Trim();
        invoice.QuoteId = input.QuoteId;
        invoice.ContractId = input.ContractId;
        invoice.UpdatedUtc = DateTime.UtcNow;
        invoice.Lines.Clear();
        invoice.Lines.AddRange(lines.Select((x, i) => new InvoiceLine
        {
            SortOrder = i, ProductId = x.ProductId, Description = x.Description.Trim(), Quantity = x.Quantity, Unit = string.IsNullOrWhiteSpace(x.Unit) ? "Adet" : x.Unit.Trim(),
            UnitPrice = x.UnitPrice, DiscountRate = x.DiscountRate, VatRate = x.VatRate, WithholdingRate = x.WithholdingRate
        }));
        InvoiceRules.Calculate(invoice);
        return true;
    }

    private async Task LoadOptions(InvoiceKind kind)
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Products = await _db.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { id = x.Id, name = x.Name, unit = x.Unit, price = AccountingLabels.IsSaleSide(kind) ? x.SalePrice : x.PurchasePrice, vat = x.VatRate }).ToListAsync();
    }
}
