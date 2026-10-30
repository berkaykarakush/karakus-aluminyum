using System.Text.Json;
using System.Text.RegularExpressions;
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

// Çizim listeleri: her çizim bir listenin satırıdır. Ölçüye göre şekildir; teklif ve sözleşmelere eklenir. İmalat bölümleri "Üretim detayları" ayarına bağlıdır.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/cizim")]
public sealed class DrawingController : Controller
{
    private const int MaxRows = 200;
    private static readonly JsonSerializerOptions RowJson = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly LengthUnits _units;
    private readonly SiteSettingsProvider _settings;
    public DrawingController(AppDbContext db, IAuditService audit, LengthUnits units, SiteSettingsProvider settings) { _db = db; _audit = audit; _units = units; _settings = settings; }

    private async Task<bool> ProductionAsync() => (await _settings.GetAsync()).ProductionDetails;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tur)
    {
        var query = _db.DrawingLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).AsQueryable();
        if (ProductCategories.All.Contains(tur)) query = query.Where(x => x.Items.Any(i => i.Category == tur));
        ViewBag.Category = tur;
        return View(await query.OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(string? tur)
    {
        ViewBag.Category = ProductCategories.All.Contains(tur) ? tur : ProductCategories.Aluminum;
        await LoadListOptions();
        return View("ListEditor", new DrawingListInputVm());
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> Create(DrawingListInputVm input)
    {
        input.Id = 0;
        var list = new DrawingList();
        if (!await ApplyListAsync(list, input))
        {
            await LoadListOptions();
            return View("ListEditor", input);
        }
        _db.DrawingLists.Add(list);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "DrawingList", list.Id.ToString(), $"{list.Name}; {list.Items.Count} çizim");
        TempData["Notice"] = $"Liste kaydedildi: {list.Items.Count} çizim.";
        return RedirectToAction(nameof(Details), new { id = list.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (list is null) return NotFound();
        await LoadListOptions();
        return View("ListEditor", new DrawingListInputVm
        {
            Id = list.Id, Name = list.Name, CustomerId = list.CustomerId, Notes = list.Notes,
            ItemsJson = JsonSerializer.Serialize(list.Items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new DrawingRowInput
            {
                Id = x.Id, Category = x.Category, BrandId = x.BrandId, ProfileSystemId = x.ProfileSystemId, Name = x.Name, WidthMm = x.WidthMm, HeightMm = x.HeightMm, Quantity = x.Quantity,
                ColorHex = x.ColorHex, Shape = x.Shape, ShapeParamMm = x.ShapeParamMm, LayoutJson = x.LayoutJson, ParamsJson = x.ParamsJson
            }), RowJson)
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> Edit(int id, DrawingListInputVm input)
    {
        var list = await _db.DrawingLists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (list is null) return NotFound();
        input.Id = id;
        if (!await ApplyListAsync(list, input))
        {
            await LoadListOptions();
            return View("ListEditor", input);
        }
        list.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "DrawingList", id.ToString(), $"{list.Name}; {list.Items.Count} çizim");
        TempData["Notice"] = $"Liste kaydedildi: {list.Items.Count} çizim.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Brand)
            .Include(x => x.Items).ThenInclude(x => x.ProfileSystem).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);
        if (list is null) return NotFound();
        var suggested = new Dictionary<int, decimal>();
        if (await ProductionAsync())
            foreach (var item in list.Items.Where(x => x.ProfileSystem is not null))
                if (DrawingCalculation.Calculate(item, item.ProfileSystem!) is { IsValid: true, UnitPrice: > 0 } calculation) suggested[item.Id] = calculation.UnitPrice;
        ViewBag.SuggestedPrices = suggested;
        ViewBag.Quotes = await DraftQuotesAsync();
        return View(list);
    }

    // Listedeki tek çizimin ayrıntılı editörü: açılım, bölme, şekil ve ürün seçimleri.
    [HttpGet("poz/{id:int}")]
    public async Task<IActionResult> EditDrawing(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions(row.Category);
        return View(DesignerView(row.Category), new DesignInputVm
        {
            Id = row.Id, DrawingListId = row.DrawingListId, Name = row.Name, Category = row.Category, ProfileSystemId = row.ProfileSystemId, BrandId = row.BrandId, ParamsJson = row.ParamsJson, ColorHex = row.ColorHex,
            WidthMm = row.WidthMm, HeightMm = row.HeightMm, Shape = row.Shape, ShapeParamMm = row.ShapeParamMm, Quantity = row.Quantity, LayoutJson = row.LayoutJson, Notes = row.Notes
        });
    }

    [HttpPost("poz/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> EditDrawing(int id, DesignInputVm input)
    {
        var row = await _db.WindowDesigns.FindAsync(id);
        if (row is null) return NotFound();
        input.Id = id;
        input.DrawingListId = row.DrawingListId;
        input.Category = row.Category; // tür sonradan değişmez
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(input.Category); return View(DesignerView(input.Category), input); }

        Apply(row, input, await ProductionAsync());
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.DrawingLists.Where(x => x.Id == row.DrawingListId).ExecuteUpdateAsync(x => x.SetProperty(l => l.UpdatedUtc, DateTime.UtcNow));
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return RedirectToAction(nameof(EditDrawing), new { id });
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
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).Include(x => x.ProfileSystem).Include(x => x.Brand).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        if (row.ProfileSystem is null)
        {
            TempData["Notice"] = "İmalat föyü için önce çizime bir profil sistemi seçin.";
            return RedirectToAction(nameof(EditDrawing), new { id });
        }
        var calculation = DrawingCalculation.Calculate(row, row.ProfileSystem);
        if (calculation is null) return BadRequest();

        ViewBag.Calculation = calculation;
        ViewBag.Plans = calculation.IsValid ? BuildPlans(new[] { (row, calculation) }, row.ProfileSystem) : new List<BarPlan>();
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

    // Birim fiyat yazılan çizimler teklife eklenir; üretim detayları açıkken hesaplanan fiyat öneri olarak gelir.
    [HttpPost("teklife-ekle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToQuote(int id, int quoteId, Dictionary<int, decimal>? prices)
    {
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (list is null) return NotFound();
        var back = $"/yonetim/cizim/detay/{id}";
        var quote = await _db.Quotes.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == quoteId);
        if (quote is null || quote.Status != QuoteStatus.Draft)
        {
            TempData["Notice"] = "Çizim yalnızca taslak durumundaki teklife eklenebilir.";
            return Redirect(back);
        }
        var priced = list.Items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => (Row: x, Price: prices is not null && prices.TryGetValue(x.Id, out var price) ? price : 0m)).Where(x => x.Price > 0).ToList();
        if (priced.Count == 0)
        {
            TempData["Notice"] = "Teklife eklemek için en az bir çizime birim fiyat yazın.";
            return Redirect(back);
        }

        foreach (var (row, price) in priced)
        {
            var description = $"{row.Name} - {_units.Format(row.WidthMm)}x{_units.Format(row.HeightMm)} {_units.Code} ({ProductCategories.Label(row.Category)})";
            quote.Items.Add(new QuoteItem { Description = description.Length > 200 ? description[..200] : description, Quantity = row.Quantity, Unit = "Adet", UnitPrice = price, SortOrder = quote.Items.Count });
        }
        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("add-drawings", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; list={id}; {priced.Count} çizim; total={quote.GrandTotal}");
        TempData["Notice"] = $"{priced.Count} çizim teklife eklendi.";
        return Redirect($"/yonetim/crm/teklifler/detay/{quote.Id}");
    }

    [HttpPost("kopyala/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Copy(int id)
    {
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (list is null) return NotFound();
        var name = list.Name + " (kopya)";
        var copy = new DrawingList
        {
            Name = name.Length > 160 ? name[..160] : name, CustomerId = list.CustomerId, Notes = list.Notes,
            Items = list.Items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new WindowDesign
            {
                Name = x.Name, CustomerId = x.CustomerId, SortOrder = x.SortOrder, BrandId = x.BrandId, Category = x.Category, ProfileSystemId = x.ProfileSystemId, ParamsJson = x.ParamsJson, ColorHex = x.ColorHex,
                WidthMm = x.WidthMm, HeightMm = x.HeightMm, Shape = x.Shape, ShapeParamMm = x.ShapeParamMm, Quantity = x.Quantity, LayoutJson = x.LayoutJson, Notes = x.Notes
            }).ToList()
        };
        _db.DrawingLists.Add(copy);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("copy", "DrawingList", copy.Id.ToString(), copy.Name);
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var list = await _db.DrawingLists.FindAsync(id);
        if (list is null) return NotFound();
        _db.DrawingLists.Remove(list);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "DrawingList", id.ToString(), list.Name);
        return RedirectToAction(nameof(Index));
    }

    private static string DesignerView(string category) => ProductCategories.IsWindow(category) ? "Designer" : "ProductDesigner";

    // Satırların tamamı geçerli olmalıdır: hatalar satır numarasıyla bildirilir ve hiçbiri kaydedilmez.
    private async Task<bool> ApplyListAsync(DrawingList list, DrawingListInputVm input)
    {
        List<DrawingRowInput>? rows = null;
        try { rows = JsonSerializer.Deserialize<List<DrawingRowInput>>(input.ItemsJson, RowJson); }
        catch (JsonException) { }
        if (rows is null) ModelState.AddModelError(nameof(input.ItemsJson), "Çizim satırları okunamadı.");
        else if (rows.Count == 0) ModelState.AddModelError(nameof(input.ItemsJson), "Listede en az bir çizim olmalıdır.");
        else if (rows.Count > MaxRows) ModelState.AddModelError(nameof(input.ItemsJson), $"Bir listede en fazla {MaxRows} çizim olabilir.");
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        if (!ModelState.IsValid) return false;

        var production = await ProductionAsync();
        // Üretim detayları açıksa yeni satıra türünün ilk aktif sistemi atanır; çizimden değiştirilebilir.
        var systems = production
            ? (await _db.ProfileSystems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync()).GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.First().Id)
            : new Dictionary<string, int>();
        var brands = await _db.Brands.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Category);
        var existing = list.Items.ToDictionary(x => x.Id);
        var used = rows!.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name!.Trim()).ToHashSet();
        var nextName = 1;
        var kept = new List<WindowDesign>();
        for (var i = 0; i < rows.Count; i++)
        {
            var item = rows[i];
            WindowDesign? current = null;
            if (item.Id is not null && !existing.TryGetValue(item.Id.Value, out current))
            {
                ModelState.AddModelError(string.Empty, $"{i + 1}. satır: Bu çizim listeye ait değil.");
                continue;
            }
            // Kayıtlı çizimin türü ve şekli listeden değişmez; ayrıntılar çizim editöründedir.
            var category = current?.Category ?? item.Category;
            var shape = current?.Shape ?? WindowShapes.Rect;
            var shapeParam = current?.ShapeParamMm ?? 0;
            var systemId = production
                ? item.ProfileSystemId ?? current?.ProfileSystemId ?? (systems.TryGetValue(category, out var found) ? found : null)
                : current?.ProfileSystemId;

            var errors = await ErrorsAsync(category, item.WidthMm, item.HeightMm, shape, shapeParam, item.Quantity, item.LayoutJson, item.ParamsJson, systemId, production);
            if (item.BrandId is not null && (!brands.TryGetValue(item.BrandId.Value, out var brandCategory) || brandCategory != category)) errors.Add("Bu ürün türüne ait geçerli bir marka seçin.");
            if (!string.IsNullOrEmpty(item.ColorHex) && !Regex.IsMatch(item.ColorHex, "^#[0-9a-fA-F]{6}$")) errors.Add("Geçersiz renk.");
            if (item.Name?.Trim().Length > 160) errors.Add("Ad en fazla 160 karakter olabilir.");
            foreach (var error in errors) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: {error}");
            if (errors.Count > 0) continue;

            var name = item.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                do name = $"Poz {nextName++}"; while (!used.Add(name));
            }
            var row = current ?? new WindowDesign { Category = category, Shape = shape };
            row.Name = name;
            row.SortOrder = i;
            row.CustomerId = input.CustomerId;
            row.BrandId = item.BrandId;
            row.ProfileSystemId = systemId;
            row.ColorHex = string.IsNullOrEmpty(item.ColorHex) ? input.ColorHex : item.ColorHex;
            row.WidthMm = item.WidthMm;
            row.HeightMm = item.HeightMm;
            row.Quantity = item.Quantity;
            row.UpdatedUtc = DateTime.UtcNow;
            if (ProductCategories.IsWindow(category)) row.LayoutJson = WindowLayout.Serialize(WindowLayout.Parse(item.LayoutJson)!);
            else row.ParamsJson = WindowDrawings.Serialize(WindowDrawings.ParseParams(item.ParamsJson)!);
            kept.Add(row);
        }
        if (!ModelState.IsValid) return false;

        list.Name = input.Name.Trim();
        list.CustomerId = input.CustomerId;
        list.Notes = input.Notes?.Trim();
        foreach (var removed in list.Items.Except(kept).ToList()) _db.WindowDesigns.Remove(removed);
        list.Items = kept;
        return true;
    }

    private async Task Validate(DesignInputVm input)
    {
        if (input.BrandId is not null && !await _db.Brands.AnyAsync(x => x.Id == input.BrandId && x.Category == input.Category))
            ModelState.AddModelError(nameof(input.BrandId), "Bu ürün türüne ait geçerli bir marka seçin.");
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
        row.BrandId = input.BrandId;
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

    private async Task LoadListOptions()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Brands = await _db.Brands.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
    }

    private async Task LoadOptions(string category)
    {
        ViewBag.Brands = await _db.Brands.AsNoTracking().Where(x => x.Category == category).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
        ViewBag.Systems = await ProductionAsync() ? await _db.ProfileSystems.AsNoTracking().Where(x => x.Category == category).OrderBy(x => x.Name).ToListAsync() : new List<ProfileSystem>();
    }
}
