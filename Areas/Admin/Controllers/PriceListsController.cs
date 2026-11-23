using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Ercom'daki fiyat listeleri: tedarikçi listesini koda göre stok kartlarına yükleme ve döviz kurları.
[Area("Admin"), Authorize(Roles = "Admin,Editor,Accountant"), Route("yonetim/fiyat-listeleri")]
public sealed class PriceListsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PriceListsController> _logger;
    private readonly ExchangeRateCache _rates;
    public PriceListsController(AppDbContext db, IAuditService audit, IHttpClientFactory http, ILogger<PriceListsController> logger, ExchangeRateCache rates) { _db = db; _audit = audit; _http = http; _logger = logger; _rates = rates; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q)
    {
        ViewBag.Rates = await PriceLists.LatestRatesAsync(_db);
        ViewBag.History = await _db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.Date).ThenBy(x => x.Currency).Take(20).ToListAsync();
        var products = _db.Products.AsNoTracking().Where(x => x.Kind == ProductKind.Stock && x.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(x => EF.Functions.ILike(x.Name, $"%{q}%") || EF.Functions.ILike(x.Code ?? "", $"%{q}%"));
        ViewBag.Query = q;
        return View(await products.OrderBy(x => x.Code).ThenBy(x => x.Name).Take(500).ToListAsync());
    }

    [HttpPost("kur"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRate(string currency, decimal rate, DateOnly? date)
    {
        if (!PriceLists.ForeignCurrencies.Contains(currency) || rate <= 0 || rate > 1_000_000) TempData["Notice"] = "Para birimi ve 0'dan büyük kur girin.";
        else
        {
            await UpsertAsync(currency, rate, date ?? Today, "Elle");
            await _db.SaveChangesAsync();
            await _rates.ReloadAsync(_db); // döviz fiyatlı profil sistemleri yeni kurla hesaplansın
            await _audit.RecordAsync("update", "ExchangeRate", currency, rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            TempData["Notice"] = $"{currency} kuru kaydedildi.";
        }
        return RedirectToAction(nameof(Index));
    }

    // Kullanıcı düğmeye basınca TCMB'nin herkese açık günlük kur dosyası okunur; istekle hiçbir firma bilgisi gönderilmez.
    [HttpPost("kur/tcmb"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FetchTcmb()
    {
        try
        {
            var xml = await _http.CreateClient(PriceLists.TcmbClient).GetStringAsync(PriceLists.TcmbUrl);
            var (date, rates) = PriceLists.ParseTcmb(xml);
            foreach (var (currency, rate) in rates) await UpsertAsync(currency, rate, date, "TCMB");
            await _db.SaveChangesAsync();
            await _rates.ReloadAsync(_db);
            await _audit.RecordAsync("update", "ExchangeRate", "TCMB", date.ToString("yyyy-MM-dd"));
            TempData["Notice"] = rates.Count > 0 ? $"TCMB {date:dd.MM.yyyy} döviz satış kurları alındı: {string.Join(", ", rates.Select(r => $"{r.Currency} {r.Rate:0.0000}"))}." : "TCMB yanıtında EUR/USD kuru bulunamadı.";
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or FormatException or System.Xml.XmlException)
        {
            _logger.LogWarning(error, "TCMB kurları alınamadı");
            TempData["Notice"] = "TCMB kurları alınamadı; kuru elle girin.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task UpsertAsync(string currency, decimal rate, DateOnly date, string source)
    {
        var row = await _db.ExchangeRates.FirstOrDefaultAsync(x => x.Currency == currency && x.Date == date);
        if (row is null) _db.ExchangeRates.Add(row = new ExchangeRate { Currency = currency, Date = date });
        row.Rate = decimal.Round(rate, 6);
        row.Source = source;
        row.CreatedUtc = DateTime.UtcNow;
    }

    // Tedarikçi fiyat listesi: koda göre eşleşen stok kartlarının alış fiyatı ve para birimi güncellenir.
    [HttpPost("yukle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string? text, IFormFile? file, string? currency)
    {
        if (file is { Length: > 0 })
        {
            if (file.Length > 2_000_000) { TempData["Notice"] = "Dosya en fazla 2 MB olabilir."; return RedirectToAction(nameof(Index)); }
            using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync();
        }
        var fallback = AccountingLabels.Currencies.Contains(currency) ? currency! : "TRY";
        var (rows, errors) = PriceLists.ParsePriceList(text);
        var products = await _db.Products.Where(x => x.Code != null && x.Code != "").ToListAsync();
        var byCode = products.GroupBy(x => x.Code!.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.ToList());
        var updated = 0;
        var missing = new List<string>();
        foreach (var row in rows)
        {
            if (!byCode.TryGetValue(row.Code.ToUpperInvariant(), out var matches)) { missing.Add(row.Code); continue; }
            if (matches.Count > 1) { errors.Add($"Satır {row.Line}: {row.Code} kodu birden fazla stok kartında var; güncellenmedi."); continue; }
            matches[0].PurchasePrice = decimal.Round(row.Price, 2);
            matches[0].PriceCurrency = row.Currency ?? fallback;
            updated++;
        }
        await _db.SaveChangesAsync();
        if (updated > 0) await _audit.RecordAsync("update", "PriceList", "import", $"{updated} fiyat");
        TempData["Notice"] = $"{updated} stok kartının fiyatı güncellendi." + (missing.Count > 0 ? $" Bulunamayan kodlar ({missing.Count}): {string.Join(", ", missing.Take(20))}{(missing.Count > 20 ? "…" : "")}." : "")
            + (errors.Count > 0 ? " " + string.Join(" ", errors.Take(10)) : "");
        return RedirectToAction(nameof(Index));
    }
}
