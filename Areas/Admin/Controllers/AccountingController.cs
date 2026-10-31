using System.Globalization;
using System.Text;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Accounting;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record CariRow(Customer Customer, decimal Balance);
public sealed record AccountingDashboardVm(decimal CashTotal, decimal Receivable, decimal Payable, decimal Overdue, int OverdueCount, decimal MonthSales, decimal MonthCosts, decimal ChequesIn, decimal ChequesOut, int CriticalStock, IReadOnlyList<AccountBalance> Accounts, IReadOnlyList<CashFlowRow> Upcoming);

[Area("Admin"), Authorize(Roles = "Admin,Accountant"), Route("yonetim/muhasebe")]
public sealed class AccountingController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountingQueries _queries;
    private readonly IAuditService _audit;
    public AccountingController(AppDbContext db, AccountingQueries queries, IAuditService audit) { _db = db; _queries = queries; _audit = audit; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var today = Today;
        var balances = await _queries.BalancesAsync();
        var accounts = await _queries.AccountBalancesAsync();
        var open = await _queries.OpenInvoicesAsync();
        var overdue = open.Where(x => x.DueDate < today && AccountingLabels.CariSign(x.Kind) > 0).ToList();
        var profit = await _queries.ProfitAsync(new DateOnly(today.Year, today.Month, 1), today);
        var cheques = await _db.Cheques.AsNoTracking().Where(x => x.Status == ChequeStatus.Portfolio || x.Status == ChequeStatus.Deposited).Select(x => new { x.Direction, x.Amount }).ToListAsync();
        var stock = await _queries.StockAsync();
        return View(new AccountingDashboardVm(
            accounts.Where(x => x.Account.Currency == "TRY" && x.Account.IsActive).Sum(x => x.Balance), balances.Values.Where(x => x > 0).Sum(), -balances.Values.Where(x => x < 0).Sum(),
            overdue.Sum(x => x.Open), overdue.Count, profit.Revenue, profit.Costs,
            cheques.Where(x => x.Direction == ChequeDirection.Received).Sum(x => x.Amount), cheques.Where(x => x.Direction == ChequeDirection.Given).Sum(x => x.Amount),
            stock.Count(x => x.Product.Kind == ProductKind.Stock && x.Product.IsActive && x.Quantity <= x.Product.CriticalStock),
            accounts.Where(x => x.Account.IsActive).ToList(), (await _queries.CashFlowAsync(today, today.AddDays(30))).Take(12).ToList()));
    }

    [HttpGet("cariler")]
    public async Task<IActionResult> Parties(string? q, string? bakiye, PartyType? tur)
    {
        var balances = await _queries.BalancesAsync();
        var query = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Name, $"%{q}%") || (x.TaxNumber != null && x.TaxNumber.Contains(q)));
        if (tur is not null) query = query.Where(x => x.PartyType == tur || x.PartyType == PartyType.Both);
        var rows = (await query.OrderBy(x => x.Name).Take(1000).ToListAsync()).Select(x => new CariRow(x, balances.GetValueOrDefault(x.Id)));
        rows = bakiye switch { "borclu" => rows.Where(x => x.Balance > 0), "alacakli" => rows.Where(x => x.Balance < 0), _ => rows };
        (ViewBag.Query, ViewBag.Balance, ViewBag.Type) = (q, bakiye, tur);
        return View(rows.ToList());
    }

    [HttpGet("cariler/{id:int}")]
    public async Task<IActionResult> Statement(int id, DateOnly? bas, DateOnly? bit, bool csv = false)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (customer is null) return NotFound();
        var lines = await _queries.StatementAsync(id, bas, bit);
        if (csv)
            return Csv($"ekstre-{id}", new[] { "Tarih", "İşlem", "Belge", "Açıklama", "Borç", "Alacak", "Bakiye" }, lines.Select(x => new[] { D(x.Date), x.Type, x.Document, x.Description ?? "", N(x.Debit), N(x.Credit), N(x.Balance) }));
        (ViewBag.Customer, ViewBag.From, ViewBag.To) = (customer, bas, bit);
        ViewBag.Balance = (await _queries.BalancesAsync()).GetValueOrDefault(id);
        ViewBag.Open = await _queries.OpenInvoicesAsync(id);
        return View(lines);
    }

    [HttpPost("cariler/{id:int}/duzeltme"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(int id, DateOnly date, decimal amount, string? description)
    {
        if (!await _db.Customers.AnyAsync(x => x.Id == id)) return NotFound();
        if (amount == 0 || string.IsNullOrWhiteSpace(description)) TempData["Notice"] = "Tutar ve açıklama yazın.";
        else
        {
            _db.CariAdjustments.Add(new CariAdjustment { CustomerId = id, Date = date, Amount = amount, Description = description.Trim() });
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("adjust", "Customer", id.ToString(), $"{amount} TL; {description}");
            TempData["Notice"] = "Kayıt eklendi.";
        }
        return RedirectToAction(nameof(Statement), new { id });
    }

    [HttpGet("raporlar/kdv")]
    public async Task<IActionResult> Vat(int? yil, bool csv = false)
    {
        var year = yil ?? Today.Year;
        var rows = await _queries.VatAsync(year);
        if (csv)
            return Csv($"kdv-{year}", new[] { "Dönem", "Satış matrahı", "Hesaplanan KDV", "Satış tevkifatı", "Alış matrahı", "Alış KDV", "Alış tevkifatı", "Gider KDV", "İndirilecek KDV", "Ödenecek / devreden" },
                rows.Select(x => new[] { x.Period, N(x.SaleNet), N(x.SaleVat), N(x.SaleWithholding), N(x.PurchaseNet), N(x.PurchaseVat), N(x.PurchaseWithholding), N(x.ExpenseVat), N(x.Deductible), N(x.Payable) }));
        ViewBag.Year = year;
        return View(rows);
    }

    [HttpGet("raporlar/kar-zarar")]
    public async Task<IActionResult> Profit(DateOnly? bas, DateOnly? bit)
    {
        var from = bas ?? new DateOnly(Today.Year, 1, 1);
        var to = bit ?? Today;
        (ViewBag.From, ViewBag.To) = (from, to);
        return View(await _queries.ProfitAsync(from, to));
    }

    [HttpGet("raporlar/yaslandirma")]
    public async Task<IActionResult> Aging(string? tur, bool csv = false)
    {
        var receivable = tur != "borc";
        var rows = await _queries.AgingAsync(receivable, Today);
        if (csv)
            return Csv(receivable ? "alacak-yaslandirma" : "borc-yaslandirma", new[] { "Cari", "Vadesi gelmemiş", "1-30 gün", "31-60 gün", "61-90 gün", "90+ gün", "Toplam" }, rows.Select(x => new[] { x.Name, N(x.Current), N(x.Days30), N(x.Days60), N(x.Days90), N(x.Older), N(x.Total) }));
        ViewBag.Receivable = receivable;
        return View(rows);
    }

    [HttpGet("raporlar/nakit-akisi")]
    public async Task<IActionResult> CashFlow(int gun = 90, bool csv = false)
    {
        gun = Math.Clamp(gun, 1, 730);
        var rows = await _queries.CashFlowAsync(Today, Today.AddDays(gun));
        if (csv) return Csv("nakit-akisi", new[] { "Tarih", "Tür", "Belge", "Cari", "Giriş", "Çıkış" }, rows.Select(x => new[] { D(x.Date), x.Type, x.Document, x.Party, N(x.In), N(x.Out) }));
        ViewBag.Days = gun;
        ViewBag.Cash = (await _queries.AccountBalancesAsync()).Where(x => x.Account.Currency == "TRY" && x.Account.IsActive).Sum(x => x.Balance);
        return View(rows);
    }

    [HttpGet("aktar")]
    public IActionResult Import() => View();

    // Kolaybi veya Excel'den kopyalanan tablo. Hatalı satır varsa hiçbir şey kaydedilmez.
    [HttpPost("aktar"), ValidateAntiForgeryToken, RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Import(string tip, string? text, DateOnly? openingDate)
    {
        var table = ImportParser.Parse(text);
        ImportResult result;
        try
        {
            if (table.Count < 2) throw new AccountingException("Başlık satırıyla birlikte en az bir kayıt yapıştırın.");
            var date = openingDate ?? Today;
            result = tip == "urun" ? await ImportProductsAsync(table, date) : await ImportCustomersAsync(table, date);
        }
        catch (AccountingException error) { result = new ImportResult(0, 0, new[] { error.Message }); }
        if (result.Errors.Count > 0) { foreach (var error in result.Errors.Take(50)) ModelState.AddModelError(string.Empty, error); return View(); }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("import", tip == "urun" ? "Product" : "Customer", "", $"{result.Added} eklendi, {result.Skipped} atlandı");
        TempData["Notice"] = $"{result.Added} kayıt eklendi, {result.Skipped} kayıt zaten vardı.";
        return RedirectToAction(nameof(Import));
    }

    private async Task<ImportResult> ImportCustomersAsync(List<string[]> table, DateOnly date)
    {
        var columns = ImportParser.Columns(table[0], new()
        {
            ["name"] = new[] { "ad", "unvan", "ünvan", "cari", "cari adı", "cari adi", "firma", "müşteri", "ad soyad", "adı" }, ["tax"] = new[] { "vergi no", "vergi numarası", "vkn", "tckn", "vkn/tckn", "tc" },
            ["office"] = new[] { "vergi dairesi" }, ["phone"] = new[] { "telefon", "tel", "gsm", "cep" }, ["email"] = new[] { "e-posta", "eposta", "email", "mail" }, ["address"] = new[] { "adres" },
            ["city"] = new[] { "il", "şehir", "sehir" }, ["district"] = new[] { "ilçe", "ilce" }, ["type"] = new[] { "tür", "tip", "cari türü", "cari tipi" }, ["balance"] = new[] { "bakiye", "açılış bakiyesi", "acilis bakiyesi" }
        });
        if (!columns.ContainsKey("name")) throw new AccountingException("Başlık satırında cari adı sütunu bulunamadı (Ad, Unvan veya Cari adı).");
        var existing = await _db.Customers.AsNoTracking().Select(x => new { x.Name, x.TaxNumber }).ToListAsync();
        var names = existing.Select(x => x.Name.ToLowerInvariant()).ToHashSet();
        var taxes = existing.Where(x => !string.IsNullOrEmpty(x.TaxNumber)).Select(x => x.TaxNumber!).ToHashSet();
        var errors = new List<string>();
        var added = 0; var skipped = 0;
        for (var i = 1; i < table.Count; i++)
        {
            string? Get(string key) => columns.TryGetValue(key, out var index) && index < table[i].Length && !string.IsNullOrWhiteSpace(table[i][index]) ? table[i][index].Trim() : null;
            var name = Get("name");
            if (name is null || name.Length > 160) { errors.Add($"{i + 1}. satır: Cari adı boş veya çok uzun."); continue; }
            var tax = Get("tax");
            if ((tax is not null && !taxes.Add(tax)) || !names.Add(name.ToLowerInvariant())) { skipped++; continue; }
            decimal balance = 0;
            var rawBalance = Get("balance");
            if (rawBalance is not null && !ImportParser.TryNumber(rawBalance, out balance)) { errors.Add($"{i + 1}. satır: Bakiye okunamadı: {rawBalance}"); continue; }
            var typeText = Get("type")?.ToLowerInvariant() ?? "";
            var customer = new Customer
            {
                Name = name, Kind = tax?.Length == 11 ? CustomerKind.Individual : CustomerKind.Company, Status = CustomerStatus.Active, TaxNumber = tax, TaxOffice = Get("office"), Phone = Get("phone"), Email = Get("email"), Address = Get("address"), City = Get("city"), District = Get("district"),
                PartyType = typeText.Contains("tedarik") ? PartyType.Supplier : PartyType.Customer, Source = "Aktarım"
            };
            _db.Customers.Add(customer);
            if (balance != 0) _db.CariAdjustments.Add(new CariAdjustment { Customer = customer, Date = date, Amount = balance, Description = "Açılış bakiyesi (aktarım)" });
            added++;
        }
        return new ImportResult(added, skipped, errors);
    }

    private async Task<ImportResult> ImportProductsAsync(List<string[]> table, DateOnly date)
    {
        var columns = ImportParser.Columns(table[0], new()
        {
            ["name"] = new[] { "ad", "ürün", "urun", "ürün adı", "urun adi", "hizmet", "ürün/hizmet" }, ["code"] = new[] { "kod", "ürün kodu", "stok kodu" }, ["unit"] = new[] { "birim" },
            ["sale"] = new[] { "satış fiyatı", "satis fiyati", "satış" }, ["purchase"] = new[] { "alış fiyatı", "alis fiyati", "alış" }, ["vat"] = new[] { "kdv", "kdv oranı" }, ["stock"] = new[] { "stok", "miktar", "stok miktarı" }, ["kind"] = new[] { "tür", "tip", "ürün türü" }
        });
        if (!columns.ContainsKey("name")) throw new AccountingException("Başlık satırında ürün adı sütunu bulunamadı.");
        var names = (await _db.Products.AsNoTracking().Select(x => x.Name).ToListAsync()).Select(x => x.ToLowerInvariant()).ToHashSet();
        var errors = new List<string>();
        var added = 0; var skipped = 0;
        for (var i = 1; i < table.Count; i++)
        {
            string? Get(string key) => columns.TryGetValue(key, out var index) && index < table[i].Length && !string.IsNullOrWhiteSpace(table[i][index]) ? table[i][index].Trim() : null;
            var name = Get("name");
            if (name is null || name.Length > 200) { errors.Add($"{i + 1}. satır: Ürün adı boş veya çok uzun."); continue; }
            if (!names.Add(name.ToLowerInvariant())) { skipped++; continue; }
            decimal Number(string key, decimal fallback)
            {
                var raw = Get(key);
                if (raw is null) return fallback;
                if (ImportParser.TryNumber(raw.TrimStart('%'), out var value) && value >= 0) return value;
                errors.Add($"{i + 1}. satır: Sayı okunamadı: {raw}");
                return fallback;
            }
            var product = new Product { Name = name, Kind = Get("kind")?.Contains("hizmet", StringComparison.OrdinalIgnoreCase) == true ? ProductKind.Service : ProductKind.Stock, Code = Get("code"), Unit = Get("unit") ?? "Adet", SalePrice = Number("sale", 0), PurchasePrice = Number("purchase", 0), VatRate = Number("vat", 20m) };
            var stock = Number("stock", 0);
            _db.Products.Add(product);
            if (stock > 0 && product.Kind == ProductKind.Stock) _db.StockMovements.Add(new StockMovement { Product = product, Date = date, Kind = StockMovementKind.Opening, Quantity = stock, Note = "Açılış (aktarım)" });
            added++;
        }
        return new ImportResult(added, skipped, errors);
    }

    private static string N(decimal value) => value.ToString("0.00", TurkishNumbers.Culture);
    private static string D(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private FileResult Csv(string name, string[] headers, IEnumerable<string[]> rows)
    {
        var csv = new StringBuilder().AppendLine(string.Join(';', headers.Select(ReportsController.Cell)));
        foreach (var row in rows) csv.AppendLine(string.Join(';', row.Select(ReportsController.Cell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"{name}.csv");
    }
}

public sealed record ImportResult(int Added, int Skipped, IReadOnlyList<string> Errors);

public static class ImportParser
{
    // Excel kopyası sekmeli, CSV dosyası noktalı virgüllü gelir.
    public static List<string[]> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        var lines = text.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var separator = lines[0].Contains('\t') ? '\t' : ';';
        return lines.Select(x => x.Split(separator).Select(c => c.Trim().Trim('"')).ToArray()).ToList();
    }

    public static Dictionary<string, int> Columns(string[] header, Dictionary<string, string[]> names)
    {
        var result = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++)
        {
            var title = header[i].Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            foreach (var (key, options) in names)
                if (!result.ContainsKey(key) && options.Contains(title)) result[key] = i;
        }
        return result;
    }

    // 1.250,50 / 1250,50 / 1250.50: son ayraç ondalıktır.
    public static bool TryNumber(string raw, out decimal value)
    {
        var text = raw.Replace("TL", "").Replace("₺", "").Replace(" ", "").Trim();
        var comma = text.LastIndexOf(',');
        var dot = text.LastIndexOf('.');
        if (comma >= 0 && dot >= 0) text = comma > dot ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
        else if (comma >= 0) text = text.Replace(',', '.');
        else if (dot >= 0 && text.Length - dot == 4 && text.Count(c => c == '.') >= 1) text = text.Replace(".", ""); // 1.250 binlik
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
