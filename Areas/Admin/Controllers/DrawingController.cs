using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Ölçüye göre şekil çizimleri; teklif ve sözleşmelere eklenir. İmalat bölümleri "Üretim detayları" ayarına bağlıdır.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/cizim")]
public sealed class DrawingController : Controller
{
    private const int MaxBulkRows = 200;
    private static readonly JsonSerializerOptions BulkJson = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly LengthUnits _units;
    private readonly SiteSettingsProvider _settings;
    public DrawingController(AppDbContext db, IAuditService audit, LengthUnits units, SiteSettingsProvider settings) { _db = db; _audit = audit; _units = units; _settings = settings; }

    private async Task<bool> ProductionAsync() => (await _settings.GetAsync()).ProductionDetails;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tur)
    {
        var query = _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (ProductCategories.All.Contains(tur)) query = query.Where(x => x.Category == tur);
        ViewBag.Category = tur;
        return View(await query.OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync());
    }

    // Tür seçilmemişse önce ürün türü sorulur.
    [HttpGet("ekle")]
    public async Task<IActionResult> Create(string? tur)
    {
        if (!ProductCategories.All.Contains(tur)) return View("Chooser");
        await LoadOptions(tur!);
        var isRailing = tur == ProductCategories.Railing;
        var systemId = await ProductionAsync() ? await _db.ProfileSystems.Where(x => x.IsActive && x.Category == tur).OrderBy(x => x.Name).Select(x => (int?)x.Id).FirstOrDefaultAsync() : null;
        return View(DesignerView(tur!), new DesignInputVm { Category = tur!, ProfileSystemId = systemId, LayoutJson = "{\"id\":\"root\"}", ParamsJson = "{}", WidthMm = isRailing ? 4000 : 1500, HeightMm = isRailing ? 1100 : 1400 });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> Create(DesignInputVm input)
    {
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(ViewCategory(input)); return View(DesignerView(ViewCategory(input)), input); }

        var row = new WindowDesign();
        Apply(row, input, await ProductionAsync());
        _db.WindowDesigns.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return RedirectToAction(nameof(Edit), new { id = row.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions(row.Category);
        return View(DesignerView(row.Category), new DesignInputVm
        {
            Id = row.Id, Name = row.Name, CustomerId = row.CustomerId, Category = row.Category, ProfileSystemId = row.ProfileSystemId, ParamsJson = row.ParamsJson, ColorHex = row.ColorHex,
            WidthMm = row.WidthMm, HeightMm = row.HeightMm, Shape = row.Shape, ShapeParamMm = row.ShapeParamMm, Quantity = row.Quantity, LayoutJson = row.LayoutJson, Notes = row.Notes
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> Edit(int id, DesignInputVm input)
    {
        var row = await _db.WindowDesigns.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        input.Category = row.Category; // tür sonradan değişmez
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(input.Category); return View(DesignerView(input.Category), input); }

        Apply(row, input, await ProductionAsync());
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("toplu-ekle")]
    public async Task<IActionResult> Bulk(string? tur)
    {
        ViewBag.Category = ProductCategories.All.Contains(tur) ? tur : ProductCategories.Aluminum;
        await LoadOptions(ProductCategories.Aluminum);
        return View(new BulkDesignInputVm());
    }

    // Satırların tamamı kaydedilir ya da hiçbiri: hatalı satır numarasıyla bildirilir.
    [HttpPost("toplu-ekle"), ValidateAntiForgeryToken, RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> Bulk(BulkDesignInputVm input)
    {
        List<BulkDesignRow>? rows = null;
        try { rows = JsonSerializer.Deserialize<List<BulkDesignRow>>(input.ItemsJson, BulkJson); }
        catch (JsonException) { }
        if (rows is null) ModelState.AddModelError(nameof(input.ItemsJson), "Çizim satırları okunamadı.");
        else if (rows.Count == 0) ModelState.AddModelError(nameof(input.ItemsJson), "En az bir çizim ekleyin.");
        else if (rows.Count > MaxBulkRows) ModelState.AddModelError(nameof(input.ItemsJson), $"Tek seferde en fazla {MaxBulkRows} çizim eklenebilir.");
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");

        var production = await ProductionAsync();
        var created = new List<WindowDesign>();
        if (ModelState.IsValid)
        {
            // Üretim detayları açıksa her türün ilk aktif sistemi atanır; çizimden değiştirilebilir.
            var systems = production
                ? (await _db.ProfileSystems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync()).GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.First().Id)
                : new Dictionary<string, int>();
            for (var i = 0; i < rows!.Count; i++)
            {
                var item = rows[i];
                var name = string.IsNullOrWhiteSpace(item.Name) ? $"Poz {i + 1}" : item.Name.Trim();
                var systemId = systems.TryGetValue(item.Category, out var found) ? found : (int?)null;
                var errors = await ErrorsAsync(item.Category, item.WidthMm, item.HeightMm, WindowShapes.Rect, 0, item.Quantity, item.LayoutJson, item.ParamsJson, systemId, production);
                if (name.Length > 160) errors.Add("Ad en fazla 160 karakter olabilir.");
                foreach (var error in errors) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: {error}");
                if (errors.Count > 0) continue;

                var isWindow = ProductCategories.IsWindow(item.Category);
                created.Add(new WindowDesign
                {
                    Name = name, CustomerId = input.CustomerId, Category = item.Category, ProfileSystemId = systemId, ColorHex = input.ColorHex,
                    WidthMm = item.WidthMm, HeightMm = item.HeightMm, Shape = WindowShapes.Rect, Quantity = item.Quantity,
                    LayoutJson = isWindow ? WindowLayout.Serialize(WindowLayout.Parse(item.LayoutJson)!) : "{}",
                    ParamsJson = isWindow ? "{}" : WindowDrawings.Serialize(WindowDrawings.ParseParams(item.ParamsJson)!)
                });
            }
        }
        if (!ModelState.IsValid)
        {
            await LoadOptions(ProductCategories.Aluminum);
            ViewBag.Category = ProductCategories.Aluminum;
            return View(input);
        }

        _db.WindowDesigns.AddRange(created);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("bulk-create", "WindowDesign", created[0].Id.ToString(), $"{created.Count} çizim");
        TempData["Notice"] = $"{created.Count} çizim eklendi.";
        return RedirectToAction(nameof(Index));
    }

    // Editörün canlı özeti: ölçü ve maliyet her zaman sunucuda hesaplanır.
    [HttpPost("hesapla"), ValidateAntiForgeryToken, RequestSizeLimit(256_000), RequireProductionDetails]
    public async Task<IActionResult> Calculate([FromBody] DesignInputVm input)
    {
        var errors = WindowDrawings.Validate(input.Category, input.WidthMm, input.HeightMm, input.Shape, input.ShapeParamMm, input.Quantity, input.LayoutJson, input.ParamsJson);
        if (errors.Count > 0) return BadRequest(new { errors });
        var system = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.ProfileSystemId && x.Category == input.Category);
        var calculation = system is null ? null : DrawingCalculation.Calculate(input.Category, input.WidthMm, input.HeightMm, input.Shape, input.ShapeParamMm, input.Quantity, input.LayoutJson, input.ParamsJson, system);
        return calculation is null ? BadRequest(new { errors = new[] { "Profil sistemi veya çizim verisi geçersiz." } }) : Json(ToResponse(calculation));
    }

    [HttpGet("imalat/{id:int}"), RequireProductionDetails]
    public async Task<IActionResult> Manufacturing(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).Include(x => x.ProfileSystem).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (row.ProfileSystem is null)
        {
            TempData["Notice"] = "İmalat föyü için önce çizime bir profil sistemi seçin.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        var calculation = DrawingCalculation.Calculate(row, row.ProfileSystem);
        if (calculation is null) return BadRequest();

        ViewBag.Calculation = calculation;
        ViewBag.Plans = calculation.IsValid ? BuildPlans(new[] { (row, calculation) }, row.ProfileSystem) : new List<BarPlan>();
        ViewBag.Quotes = await DraftQuotesAsync();
        ViewBag.GlassLists = new SelectList(await _db.GlassLists.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).Take(100).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        return View(row);
    }

    // Birden çok çizimi aynı boy profillerinden keser; sistemler ayrı ayrı planlanır. Sistemsiz çizim atlanır.
    [HttpGet("optimizasyon"), RequireProductionDetails]
    public async Task<IActionResult> Optimization([FromQuery] int[] ids)
    {
        var rows = await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).Where(x => ids.Contains(x.Id) && x.ProfileSystemId != null).ToListAsync();
        if (rows.Count == 0)
        {
            TempData["Notice"] = "Optimizasyon için profil sistemi seçilmiş en az bir çizim seçin.";
            return RedirectToAction(nameof(Index));
        }

        var groups = new List<(ProfileSystem System, List<WindowDesign> Designs, List<BarPlan> Plans)>();
        foreach (var group in rows.GroupBy(x => x.ProfileSystemId))
        {
            var system = group.First().ProfileSystem!;
            var calculated = new List<(WindowDesign, WindowCalculation)>();
            foreach (var row in group)
            {
                var calculation = DrawingCalculation.Calculate(row, system);
                if (calculation is { IsValid: true }) calculated.Add((row, calculation));
            }
            groups.Add((system, calculated.Select(x => x.Item1).ToList(), BuildPlans(calculated, system)));
        }
        return View(groups);
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).Include(x => x.ProfileSystem).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (await ProductionAsync() && row.ProfileSystem is not null)
        {
            var calculation = DrawingCalculation.Calculate(row, row.ProfileSystem);
            if (calculation is { IsValid: true, UnitPrice: > 0 }) ViewBag.SuggestedPrice = calculation.UnitPrice;
        }
        ViewBag.Quotes = await DraftQuotesAsync();
        return View(row);
    }

    // Teklif satırının birim fiyatını kullanıcı yazar; üretim detayları açıkken hesaplanan fiyat öneri olarak gelir.
    [HttpPost("teklife-ekle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToQuote(int id, int quoteId, decimal unitPrice)
    {
        var row = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var back = $"/yonetim/cizim/detay/{id}";
        var quote = await _db.Quotes.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == quoteId);
        if (quote is null || quote.Status != QuoteStatus.Draft)
        {
            TempData["Notice"] = "Çizim yalnızca taslak durumundaki teklife eklenebilir.";
            return Redirect(back);
        }
        if (unitPrice <= 0)
        {
            TempData["Notice"] = "Teklife eklemek için birim fiyat yazın.";
            return Redirect(back);
        }

        var description = $"{row.Name} - {_units.Format(row.WidthMm)}x{_units.Format(row.HeightMm)} {_units.Code} ({ProductCategories.Label(row.Category)})";
        quote.Items.Add(new QuoteItem { Description = description.Length > 200 ? description[..200] : description, Quantity = row.Quantity, Unit = "Adet", UnitPrice = unitPrice, SortOrder = quote.Items.Count });
        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("add-design", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; design={row.Id}; total={quote.GrandTotal}");
        TempData["Notice"] = "Çizim teklife eklendi.";
        return Redirect($"/yonetim/crm/teklifler/detay/{quote.Id}");
    }

    [HttpPost("kopyala/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Copy(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var name = row.Name + " (kopya)";
        var copy = new WindowDesign
        {
            Name = name.Length > 160 ? name[..160] : name, CustomerId = row.CustomerId, Category = row.Category, ProfileSystemId = row.ProfileSystemId, ParamsJson = row.ParamsJson, ColorHex = row.ColorHex,
            WidthMm = row.WidthMm, HeightMm = row.HeightMm, Shape = row.Shape, ShapeParamMm = row.ShapeParamMm, Quantity = row.Quantity, LayoutJson = row.LayoutJson, Notes = row.Notes
        };
        _db.WindowDesigns.Add(copy);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("copy", "WindowDesign", copy.Id.ToString(), copy.Name);
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.WindowDesigns.FindAsync(id);
        if (row is null) return NotFound();
        _db.WindowDesigns.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "WindowDesign", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    private static string ViewCategory(DesignInputVm input) => ProductCategories.All.Contains(input.Category) ? input.Category : ProductCategories.Aluminum;
    private static string DesignerView(string category) => ProductCategories.IsWindow(category) ? "Designer" : "ProductDesigner";

    private async Task Validate(DesignInputVm input)
    {
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        var errors = await ErrorsAsync(input.Category, input.WidthMm, input.HeightMm, input.Shape, input.ShapeParamMm, input.Quantity, input.LayoutJson, input.ParamsJson, input.ProfileSystemId, await ProductionAsync());
        foreach (var error in errors) ModelState.AddModelError(string.Empty, error);
    }

    // Şekil her zaman denetlenir; imalat kuralları yalnızca üretim detayları açıkken ve sistem seçiliyken.
    private async Task<List<string>> ErrorsAsync(string category, int width, int height, string shape, int shapeParam, int quantity, string? layoutJson, string? paramsJson, int? systemId, bool production)
    {
        var errors = WindowDrawings.Validate(category, width, height, shape, shapeParam, quantity, layoutJson, paramsJson);
        if (errors.Count > 0 || !production || systemId is null) return errors;
        var system = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == systemId && x.Category == category);
        if (system is null) { errors.Add("Bu ürün türüne ait geçerli bir profil sistemi seçin."); return errors; }
        var calculation = DrawingCalculation.Calculate(category, width, height, shape, shapeParam, quantity, layoutJson, paramsJson, system);
        if (calculation is null) errors.Add("Çizim verisi okunamadı.");
        else errors.AddRange(calculation.Errors);
        return errors;
    }

    private static void Apply(WindowDesign row, DesignInputVm input, bool production)
    {
        row.Name = input.Name.Trim();
        row.CustomerId = input.CustomerId;
        row.Category = input.Category;
        if (production) row.ProfileSystemId = input.ProfileSystemId; // kapalıyken mevcut seçim korunur
        row.ColorHex = input.ColorHex;
        row.WidthMm = input.WidthMm;
        row.HeightMm = input.HeightMm;
        row.Shape = input.Shape;
        row.ShapeParamMm = input.Shape == WindowShapes.Rect ? 0 : input.ShapeParamMm;
        row.Quantity = input.Quantity;
        row.Notes = input.Notes?.Trim();
        if (ProductCategories.IsWindow(input.Category)) row.LayoutJson = WindowLayout.Serialize(WindowLayout.Parse(input.LayoutJson)!);
        else row.ParamsJson = WindowDrawings.Serialize(WindowDrawings.ParseParams(input.ParamsJson)!);
    }

    private static List<BarPlan> BuildPlans(IEnumerable<(WindowDesign Design, WindowCalculation Calculation)> items, ProfileSystem system)
    {
        var pieces = new Dictionary<string, List<BarPiece>>();
        foreach (var (design, calculation) in items)
            foreach (var cut in calculation.Cuts)
            {
                if (!pieces.TryGetValue(cut.Profile, out var list)) pieces[cut.Profile] = list = new();
                for (var i = 0; i < cut.Count * design.Quantity; i++) list.Add(new BarPiece($"{design.Name}: {cut.Label}", cut.LengthMm));
            }
        return pieces.OrderBy(x => x.Key).Select(x => BarOptimizer.Optimize(x.Key, x.Value, system.BarLengthMm, system.KerfMm)).ToList();
    }

    private static object ToResponse(WindowCalculation c) => new
    {
        errors = c.Errors, warnings = c.Warnings, cuts = c.Cuts, glassCount = c.Glass.Sum(g => g.Count), hardware = c.Hardware,
        profileCost = c.ProfileCost, panelCost = c.PanelCost, hardwareCost = c.HardwareCost, laborCost = c.LaborCost,
        unitCost = c.UnitCost, unitPrice = c.UnitPrice, totalPrice = c.TotalPrice
    };

    private async Task<SelectList> DraftQuotesAsync()
        => new(await _db.Quotes.AsNoTracking().Where(x => x.Status == QuoteStatus.Draft).OrderByDescending(x => x.CreatedUtc)
            .Select(x => new { x.Id, Text = x.QuoteNumber + " - " + x.Title }).ToListAsync(), "Id", "Text");

    private async Task LoadOptions(string category)
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Systems = await ProductionAsync() ? await _db.ProfileSystems.AsNoTracking().Where(x => x.Category == category).OrderBy(x => x.Name).ToListAsync() : new List<ProfileSystem>();
    }
}
