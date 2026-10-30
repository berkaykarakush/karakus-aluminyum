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

// Ölçüye göre şekil çizimleri. İmalat hesabı yapılmaz; çizimler teklif ve sözleşmelere eklenir.
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
        await LoadOptions();
        var isRailing = tur == ProductCategories.Railing;
        return View(DesignerView(tur!), new DesignInputVm { Category = tur!, LayoutJson = "{\"id\":\"root\"}", ParamsJson = "{}", WidthMm = isRailing ? 4000 : 1500, HeightMm = isRailing ? 1100 : 1400 });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(256_000)]
    public async Task<IActionResult> Create(DesignInputVm input)
    {
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View(DesignerView(ViewCategory(input)), input); }

        var row = new WindowDesign();
        Apply(row, input);
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
        await LoadOptions();
        return View(DesignerView(row.Category), new DesignInputVm
        {
            Id = row.Id, Name = row.Name, CustomerId = row.CustomerId, Category = row.Category, ParamsJson = row.ParamsJson, ColorHex = row.ColorHex,
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
        if (!ModelState.IsValid) { await LoadOptions(); return View(DesignerView(input.Category), input); }

        Apply(row, input);
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "WindowDesign", row.Id.ToString(), row.Name);
        TempData["Notice"] = "Çizim kaydedildi.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await _db.WindowDesigns.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Quotes = new SelectList(await _db.Quotes.AsNoTracking().Where(x => x.Status == QuoteStatus.Draft)
            .OrderByDescending(x => x.CreatedUtc).Select(x => new { x.Id, Text = x.QuoteNumber + " - " + x.Title }).ToListAsync(), "Id", "Text");
        return View(row);
    }

    // Fiyat hesaplanmaz: teklif satırının birim fiyatını kullanıcı yazar.
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
            Name = name.Length > 160 ? name[..160] : name, CustomerId = row.CustomerId, Category = row.Category, ParamsJson = row.ParamsJson, ColorHex = row.ColorHex,
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
        foreach (var error in WindowDrawings.Validate(input.Category, input.WidthMm, input.HeightMm, input.Shape, input.ShapeParamMm, input.Quantity, input.LayoutJson, input.ParamsJson))
            ModelState.AddModelError(string.Empty, error);
    }

    private static void Apply(WindowDesign row, DesignInputVm input)
    {
        row.Name = input.Name.Trim();
        row.CustomerId = input.CustomerId;
        row.Category = input.Category;
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

    private async Task LoadOptions()
        => ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
}
