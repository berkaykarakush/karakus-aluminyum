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

public sealed record CashFormVm(string Mode, int? CustomerId, int? InvoiceId);

// Kasa, banka, tahsilat, ödeme, virman.
[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/kasa-banka")]
public sealed class FinanceController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingService _accounting;
    private readonly AccountingQueries _queries;
    private readonly IAuditService _audit;
    public FinanceController(AppDbContext db, AccountingService accounting, AccountingQueries queries, IAuditService audit) { _db = db; _accounting = accounting; _queries = queries; _audit = audit; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _queries.AccountBalancesAsync());

    [HttpGet("hesap/ekle")]
    public IActionResult CreateAccount() => View("EditAccount", new FinanceAccount());

    [HttpPost("hesap/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAccount(FinanceAccount input)
    {
        input.Id = 0;
        if (!AccountingLabels.Currencies.Contains(input.Currency)) ModelState.AddModelError(nameof(input.Currency), "Geçersiz para birimi.");
        if (!ModelState.IsValid) return View("EditAccount", input);
        _db.FinanceAccounts.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "FinanceAccount", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("hesap/duzenle/{id:int}")]
    public async Task<IActionResult> EditAccount(int id)
    {
        var row = await _db.FinanceAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("hesap/duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAccount(int id, FinanceAccount input)
    {
        var row = await _db.FinanceAccounts.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        // Hareket gördükten sonra para birimi değişirse geçmiş tutarlar anlamını yitirir.
        if (input.Currency != row.Currency && await _db.CashTransactions.AnyAsync(x => x.AccountId == id)) ModelState.AddModelError(nameof(input.Currency), "Hareketi olan hesabın para birimi değiştirilemez.");
        if (!AccountingLabels.Currencies.Contains(input.Currency)) ModelState.AddModelError(nameof(input.Currency), "Geçersiz para birimi.");
        if (!ModelState.IsValid) return View(input);
        (row.Name, row.Kind, row.Currency, row.Iban, row.OpeningBalance, row.IsActive) = (input.Name.Trim(), input.Kind, input.Currency, input.Iban?.Trim(), input.OpeningBalance, input.IsActive);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "FinanceAccount", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("hareketler/{id:int}")]
    public async Task<IActionResult> Movements(int id)
    {
        var account = await _db.FinanceAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (account is null) return NotFound();
        ViewBag.Account = account;
        return View(await _db.CashTransactions.AsNoTracking().Include(x => x.Customer).Include(x => x.Employee).Where(x => x.AccountId == id).OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync());
    }

    [HttpGet("yeni")]
    public async Task<IActionResult> New(string tur = "tahsilat", int? cari = null, int? fatura = null)
    {
        if (tur is not ("tahsilat" or "odeme" or "virman" or "giris" or "cikis")) return NotFound();
        await LoadOptions(cari);
        return View(new CashFormVm(tur, cari, fatura));
    }

    [HttpPost("yeni"), ValidateAntiForgeryToken]
    public async Task<IActionResult> New(string tur, int accountId, int? toAccountId, int? customerId, int? invoiceId, DateOnly date, decimal amount, decimal exchangeRate = 1m, string? description = null)
    {
        try
        {
            switch (tur)
            {
                case "tahsilat" or "odeme":
                    if (customerId is null) throw new AccountingException("Cari seçin.");
                    await _accounting.RecordPaymentAsync(tur == "tahsilat", customerId.Value, accountId, date, amount, exchangeRate, invoiceId, description);
                    break;
                case "virman":
                    if (toAccountId is null) throw new AccountingException("Hedef hesabı seçin.");
                    await _accounting.TransferAsync(accountId, toAccountId.Value, date, amount, exchangeRate, description);
                    break;
                case "giris" or "cikis":
                    await _accounting.RecordOtherAsync(tur == "giris", accountId, date, amount, exchangeRate, description);
                    break;
                default: return NotFound();
            }
        }
        catch (AccountingException error)
        {
            ModelState.AddModelError(string.Empty, error.Message);
            await LoadOptions(customerId);
            return View(new CashFormVm(tur, customerId, invoiceId));
        }
        TempData["Notice"] = "Hareket kaydedildi.";
        return invoiceId is not null ? Redirect($"/yonetim/muhasebe/faturalar/detay/{invoiceId}") : customerId is not null ? Redirect($"/yonetim/muhasebe/cariler/{customerId}") : RedirectToAction(nameof(Movements), new { id = accountId });
    }

    [HttpPost("iptal/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var accountId = await _db.CashTransactions.Where(x => x.Id == id).Select(x => (int?)x.AccountId).FirstOrDefaultAsync();
        if (accountId is null) return NotFound();
        try { await _accounting.CancelCashAsync(id, reason); TempData["Notice"] = "Hareket iptal edildi."; }
        catch (AccountingException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Movements), new { id = accountId });
    }

    private async Task LoadOptions(int? customerId)
    {
        ViewBag.Accounts = await _db.FinanceAccounts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Kind).ThenBy(x => x.Name).ToListAsync();
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive || x.Id == customerId).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name", customerId);
        ViewBag.OpenInvoices = customerId is null ? new List<OpenInvoice>() : await _queries.OpenInvoicesAsync(customerId);
    }
}

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/cek-senet")]
public sealed class ChequesController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingService _accounting;
    public ChequesController(AppDbContext db, AccountingService accounting) { _db = db; _accounting = accounting; }

    [HttpGet("")]
    public async Task<IActionResult> Index(ChequeDirection? yon, ChequeStatus? durum)
    {
        var query = _db.Cheques.AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (yon is not null) query = query.Where(x => x.Direction == yon);
        if (durum is not null) query = query.Where(x => x.Status == durum);
        (ViewBag.Direction, ViewBag.Status) = (yon, durum);
        return View(await query.OrderBy(x => x.DueDate).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(ChequeDirection yon = ChequeDirection.Received, int? cari = null)
    {
        await LoadOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        return View(new Cheque { Direction = yon, CustomerId = cari ?? 0, IssueDate = today, DueDate = today });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Cheque input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var row = await _accounting.AddChequeAsync(input);
                return RedirectToAction(nameof(Details), new { id = row.Id });
            }
            catch (AccountingException error) { ModelState.AddModelError(string.Empty, error.Message); }
        }
        await LoadOptions();
        return View(input);
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await _db.Cheques.AsNoTracking().Include(x => x.Customer).Include(x => x.EndorsedToCustomer).Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions();
        return View(row);
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, ChequeStatus status, DateOnly date, int? accountId, int? endorsedTo)
    {
        try { await _accounting.ChangeChequeAsync(id, status, date, accountId, endorsedTo); TempData["Notice"] = "Durum güncellendi."; }
        catch (AccountingException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Accounts = new SelectList(await _db.FinanceAccounts.AsNoTracking().Where(x => x.IsActive && x.Currency == "TRY").OrderBy(x => x.Kind).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
    }
}

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/giderler")]
public sealed class ExpensesController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingService _accounting;
    private readonly IAuditService _audit;
    public ExpensesController(AppDbContext db, AccountingService accounting, IAuditService audit) { _db = db; _accounting = accounting; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? kategori)
    {
        var query = _db.Expenses.AsNoTracking().Include(x => x.Category).Include(x => x.Customer).Include(x => x.Account).AsQueryable();
        if (kategori is not null) query = query.Where(x => x.CategoryId == kategori);
        ViewBag.CategoryId = kategori;
        ViewBag.Categories = await _db.ExpenseCategories.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        return View(await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        await LoadOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        return View(new Expense { Date = today, DueDate = today, VatRate = 20m });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Expense input)
    {
        if (ModelState.IsValid)
        {
            try { await _accounting.AddExpenseAsync(input); TempData["Notice"] = "Gider kaydedildi."; return RedirectToAction(nameof(Index)); }
            catch (AccountingException error) { ModelState.AddModelError(string.Empty, error.Message); }
        }
        await LoadOptions();
        return View(input);
    }

    [HttpPost("iptal/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        try { await _accounting.CancelExpenseAsync(id, reason); TempData["Notice"] = "Gider iptal edildi."; }
        catch (AccountingException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("kategori"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) TempData["Notice"] = "Kategori adı yazın.";
        else
        {
            _db.ExpenseCategories.Add(new ExpenseCategory { Name = name.Trim() });
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("create", "ExpenseCategory", name, name);
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadOptions()
    {
        ViewBag.Categories = new SelectList(await _db.ExpenseCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Accounts = new SelectList(await _db.FinanceAccounts.AsNoTracking().Where(x => x.IsActive && x.Currency == "TRY").OrderBy(x => x.Kind).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
    }
}

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/personel")]
public sealed class EmployeesController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingService _accounting;
    private readonly IAuditService _audit;
    public EmployeesController(AppDbContext db, AccountingService accounting, IAuditService audit) { _db = db; _accounting = accounting; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Accounts = new SelectList(await _db.FinanceAccounts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Kind).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Payments = await _db.CashTransactions.AsNoTracking().Include(x => x.Employee).Include(x => x.Account).Where(x => x.Kind == CashKind.Salary).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(100).ToListAsync();
        return View(await _db.Employees.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync());
    }

    [HttpGet("ekle")]
    public IActionResult Create() => View("Edit", new Employee());

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Employee input)
    {
        input.Id = 0;
        if (!ModelState.IsValid) return View("Edit", input);
        _db.Employees.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Employee", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Employee input)
    {
        if (!await _db.Employees.AnyAsync(x => x.Id == id)) return NotFound();
        input.Id = id;
        if (!ModelState.IsValid) return View(input);
        _db.Employees.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Employee", id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("odeme"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int employeeId, int accountId, DateOnly date, decimal amount, decimal exchangeRate = 1m, string? description = null)
    {
        try { await _accounting.SalaryAsync(employeeId, accountId, date, amount, exchangeRate, description); TempData["Notice"] = "Ödeme kaydedildi."; }
        catch (AccountingException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Index));
    }
}

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe/urunler")]
public sealed class ProductsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingQueries _queries;
    private readonly IAuditService _audit;
    public ProductsController(AppDbContext db, AccountingQueries queries, IAuditService audit) { _db = db; _queries = queries; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, bool kritik = false)
    {
        var rows = await _queries.StockAsync();
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(x => x.Product.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || (x.Product.Code?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        if (kritik) rows = rows.Where(x => x.Product.Kind == ProductKind.Stock && x.Product.IsActive && x.Quantity <= x.Product.CriticalStock).ToList();
        (ViewBag.Query, ViewBag.Critical) = (q, kritik);
        return View(rows);
    }

    [HttpGet("ekle")]
    public IActionResult Create() => View("Edit", new Product());

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product input)
    {
        input.Id = 0;
        if (!ModelState.IsValid) return View("Edit", input);
        _db.Products.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Product", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Movements = await _db.StockMovements.AsNoTracking().Where(x => x.ProductId == id).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(100).ToListAsync();
        return View(row);
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product input)
    {
        var row = await _db.Products.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        if (!ModelState.IsValid) { ViewBag.Movements = new List<StockMovement>(); return View(input); }
        (row.Code, row.Name, row.Kind, row.Unit, row.SalePrice, row.PurchasePrice, row.VatRate, row.CriticalStock, row.IsActive) = (input.Code?.Trim(), input.Name.Trim(), input.Kind, input.Unit.Trim(), input.SalePrice, input.PurchasePrice, input.VatRate, input.CriticalStock, input.IsActive);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Product", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    // Sayım farkı ve açılış: stok hiçbir zaman doğrudan yazılmaz, hareket eklenir.
    [HttpPost("stok/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(int id, decimal quantity, DateOnly date, string? note)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null) return NotFound();
        if (product.Kind != ProductKind.Stock || quantity == 0) TempData["Notice"] = "Miktar yazın; hizmette stok tutulmaz.";
        else
        {
            _db.StockMovements.Add(new StockMovement { ProductId = id, Date = date, Kind = StockMovementKind.Adjustment, Quantity = quantity, Note = note?.Trim() });
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("stock", "Product", id.ToString(), $"{quantity}");
            TempData["Notice"] = "Stok güncellendi.";
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.Products.FindAsync(id);
        if (row is null) return NotFound();
        if (await _db.InvoiceLines.AnyAsync(x => x.ProductId == id) || await _db.StockMovements.AnyAsync(x => x.ProductId == id))
            TempData["Notice"] = "Hareketi olan ürün silinemez; pasif yapın.";
        else
        {
            _db.Products.Remove(row);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("delete", "Product", id.ToString(), row.Name);
        }
        return RedirectToAction(nameof(Index));
    }
}
