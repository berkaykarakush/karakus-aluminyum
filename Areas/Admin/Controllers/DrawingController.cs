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

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/cizim")]
public sealed class DrawingController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly LengthUnits _units;
    public DrawingController(AppDbContext db, IAuditService audit, LengthUnits units) { _db = db; _audit = audit; _units = units; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tur)
    {
        var query = _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).Include(x => x.ProfileSystem).AsQueryable();
        if (ProductCategories.All.Contains(tur)) query = query.Where(x => x.Category == tur);
        ViewBag.Category = tur;
        return View(await query.OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync());
    }

    // Tür seçilmemişse önce ürün türü sorulur.
    [HttpGet("ekle")]
    public async Task<IActionResult> Create(string? tur)
    {
        if (!ProductCategories.All.Contains(tur)) return View("Chooser");
        var system = await _db.ProfileSystems.AsNoTracking().Where(x => x.IsActive && x.Category == tur).OrderBy(x => x.Name).FirstOrDefaultAsync();
        if (system is null)
        {
            TempData["Notice"] = $"Çizim yapmak için önce {ProductCategories.Label(tur)} türünde aktif bir sistem tanımlayın.";
            return Redirect("/yonetim/cizim/sistemler");
        }
        await LoadOptions(tur!);
        return View(DesignerView(tur!), new DesignInputVm { Category = tur!, ProfileSystemId = system.Id, LayoutJson = "{\"id\":\"root\"}", ParamsJson = "{}", HeightMm = tur == ProductCategories.Railing ? 1100 : 1400, WidthMm = tur == ProductCategories.Railing ? 4000 : 1500 });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> Create(DesignInputVm input)
    {
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(input.Category); return View(DesignerView(input.Category), input); }

        var row = new WindowDesign();
        Apply(row, input);
        _db.WindowDesigns.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return Redirect($"/yonetim/cizim/duzenle/{row.Id}");
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions(row.Category);
        return View(DesignerView(row.Category), new DesignInputVm
        {
            Id = row.Id, Name = row.Name, CustomerId = row.CustomerId, Category = row.Category, ProfileSystemId = row.ProfileSystemId, ParamsJson = row.ParamsJson,
            ColorHex = row.ColorHex, WidthMm = row.WidthMm, HeightMm = row.HeightMm, Shape = row.Shape, ShapeParamMm = row.ShapeParamMm,
            Quantity = row.Quantity, LayoutJson = row.LayoutJson, Notes = row.Notes
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

        Apply(row, input);
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return Redirect($"/yonetim/cizim/duzenle/{row.Id}");
    }

    // Editörün canlı önizlemesi: ölçü ve maliyet her zaman sunucuda hesaplanır.
    [HttpPost("hesapla"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> Calculate([FromBody] DesignInputVm input)
    {
        var calculation = await CalculateAsync(input);
        return calculation is null ? BadRequest(new { errors = new[] { "Profil sistemi veya çizim verisi geçersiz." } }) : Json(ToResponse(calculation));
    }

    [HttpGet("imalat/{id:int}")]
    public async Task<IActionResult> Manufacturing(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).Include(x => x.ProfileSystem).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var calculation = DrawingCalculation.Calculate(row, row.ProfileSystem!);
        if (calculation is null) return BadRequest();

        ViewBag.Calculation = calculation;
        ViewBag.Plans = calculation.IsValid ? BuildPlans(new[] { (row, calculation) }, row.ProfileSystem!) : new List<BarPlan>();
        ViewBag.Quotes = new SelectList(await _db.Quotes.AsNoTracking().Where(x => x.Status == QuoteStatus.Draft)
            .OrderByDescending(x => x.CreatedUtc).Select(x => new { x.Id, Text = x.QuoteNumber + " - " + x.Title }).ToListAsync(), "Id", "Text");
        ViewBag.GlassLists = new SelectList(await _db.GlassLists.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).Take(100).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        return View(row);
    }

    // Birden çok çizimi aynı boy profillerinden keser; sistemler ayrı ayrı planlanır.
    [HttpGet("optimizasyon")]
    public async Task<IActionResult> Optimization([FromQuery] int[] ids)
    {
        var rows = await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).Where(x => ids.Contains(x.Id)).ToListAsync();
        if (rows.Count == 0)
        {
            TempData["Notice"] = "Optimizasyon için en az bir çizim seçin.";
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

    [HttpPost("teklife-ekle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToQuote(int id, int quoteId)
    {
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var quote = await _db.Quotes.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == quoteId);
        var back = $"/yonetim/cizim/imalat/{id}";
        if (quote is null || quote.Status != QuoteStatus.Draft)
        {
            TempData["Notice"] = "Çizim yalnızca taslak durumundaki teklife eklenebilir.";
            return Redirect(back);
        }

        var calculation = DrawingCalculation.Calculate(row, row.ProfileSystem!);
        if (calculation is not { IsValid: true } || calculation.UnitPrice <= 0)
        {
            TempData["Notice"] = "Teklife eklemek için çizim hatasız olmalı ve fiyatı sıfırdan büyük çıkmalıdır. Profil sistemi fiyatlarını kontrol edin.";
            return Redirect(back);
        }

        var description = $"{row.Name} - {_units.Format(row.WidthMm)}x{_units.Format(row.HeightMm)} {_units.Code} ({row.ProfileSystem!.Name})";
        quote.Items.Add(new QuoteItem { Description = description.Length > 200 ? description[..200] : description, Quantity = row.Quantity, Unit = "Adet", UnitPrice = calculation.UnitPrice, SortOrder = quote.Items.Count });
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
            Name = name.Length > 160 ? name[..160] : name, CustomerId = row.CustomerId, Category = row.Category, ProfileSystemId = row.ProfileSystemId, ParamsJson = row.ParamsJson,
            ColorHex = row.ColorHex, WidthMm = row.WidthMm, HeightMm = row.HeightMm, Shape = row.Shape, ShapeParamMm = row.ShapeParamMm,
            Quantity = row.Quantity, LayoutJson = row.LayoutJson, Notes = row.Notes
        };
        _db.WindowDesigns.Add(copy);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("copy", "WindowDesign", copy.Id.ToString(), copy.Name);
        return Redirect($"/yonetim/cizim/duzenle/{copy.Id}");
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

    private static string DesignerView(string category) => ProductCategories.IsWindow(category) ? "Designer" : "ProductDesigner";

    private async Task<WindowCalculation?> CalculateAsync(DesignInputVm input)
    {
        var system = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.ProfileSystemId);
        if (system is null || system.Category != input.Category) return null;
        return DrawingCalculation.Calculate(input.Category, input.WidthMm, input.HeightMm, input.Shape, input.ShapeParamMm, input.Quantity, input.LayoutJson, input.ParamsJson, system);
    }

    private async Task Validate(DesignInputVm input)
    {
        if (!ProductCategories.All.Contains(input.Category)) ModelState.AddModelError(nameof(input.Category), "Geçersiz ürün türü.");
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");

        var calculation = await CalculateAsync(input);
        if (calculation is null) ModelState.AddModelError(string.Empty, "Sistem veya çizim verisi geçersiz.");
        else foreach (var error in calculation.Errors) ModelState.AddModelError(string.Empty, error);
    }

    private static void Apply(WindowDesign row, DesignInputVm input)
    {
        row.Name = input.Name.Trim();
        row.CustomerId = input.CustomerId;
        row.Category = input.Category;
        row.ProfileSystemId = input.ProfileSystemId;
        row.ColorHex = input.ColorHex;
        row.WidthMm = input.WidthMm;
        row.HeightMm = input.HeightMm;
        row.Shape = input.Shape;
        row.ShapeParamMm = input.Shape == WindowShapes.Rect ? 0 : input.ShapeParamMm;
        row.Quantity = input.Quantity;
        if (ProductCategories.IsWindow(input.Category)) row.LayoutJson = WindowLayout.Serialize(WindowLayout.Parse(input.LayoutJson)!);
        else row.ParamsJson = DrawingCalculation.Serialize(DrawingCalculation.ParseParams(input.ParamsJson)!);
        row.Notes = input.Notes?.Trim();
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

    private async Task LoadOptions(string category)
    {
        ViewBag.Systems = await _db.ProfileSystems.AsNoTracking().Where(x => x.Category == category).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
    }
}
