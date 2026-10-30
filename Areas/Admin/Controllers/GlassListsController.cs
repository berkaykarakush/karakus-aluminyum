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

// Camlar doğramalardan tamamen ayrıdır: kendi listesi, fiyatı, çizimi ve plaka yerleşimi vardır.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/camlar")]
public sealed class GlassListsController : Controller
{
    private const int MaxRows = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public GlassListsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
        => View(await _db.GlassLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(i => i.GlassType)
            .OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync());

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        if (!await _db.GlassTypes.AnyAsync(x => x.IsActive))
        {
            TempData["Notice"] = "Önce en az bir aktif cam tipi tanımlayın.";
            return RedirectToAction(nameof(Types));
        }
        await LoadOptions();
        return View("Editor", new GlassListInputVm());
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Create(GlassListInputVm input)
    {
        var rows = await Validate(input);
        if (rows is null || !ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        var row = new GlassList();
        Apply(row, input, rows);
        _db.GlassLists.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "GlassList", row.Id.ToString(), row.Name);
        return RedirectToAction(nameof(Details), new { id = row.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.GlassLists.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        await LoadOptions();
        return View("Editor", new GlassListInputVm
        {
            Id = row.Id, Name = row.Name, CustomerId = row.CustomerId, Notes = row.Notes,
            ItemsJson = JsonSerializer.Serialize(row.Items.OrderBy(i => i.SortOrder).Select(i => new GlassRowInput
            {
                GlassTypeId = i.GlassTypeId, Position = i.Position, WidthMm = i.WidthMm, HeightMm = i.HeightMm, Quantity = i.Quantity, Shaped = i.Shaped, Note = i.Note
            }), JsonOptions)
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Edit(int id, GlassListInputVm input)
    {
        var row = await _db.GlassLists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        input.Id = id;
        var rows = await Validate(input);
        if (rows is null || !ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        _db.GlassListItems.RemoveRange(row.Items);
        row.Items.Clear();
        Apply(row, input, rows);
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "GlassList", id.ToString(), row.Name);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var row = await _db.GlassLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(i => i.GlassType).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        row.Items = row.Items.OrderBy(i => i.SortOrder).ToList();
        ViewBag.Quotes = new SelectList(await _db.Quotes.AsNoTracking().Where(x => x.Status == QuoteStatus.Draft)
            .OrderByDescending(x => x.CreatedUtc).Select(x => new { x.Id, Text = x.QuoteNumber + " - " + x.Title }).ToListAsync(), "Id", "Text");
        // AsNoTracking her satır için ayrı nesne üretir; gruplama nesneye değil kimliğe göre yapılmalıdır.
        ViewBag.Plans = row.Items.GroupBy(i => i.GlassTypeId).Select(group =>
        {
            var type = group.First().GlassType!;
            var pieces = group.SelectMany(i => Enumerable.Range(0, i.Quantity).Select(_ => (string.IsNullOrWhiteSpace(i.Position) ? "Cam" : i.Position!, i.WidthMm, i.HeightMm)));
            return (Type: type, Plan: GlassPlateOptimizer.Optimize(pieces, type.SheetWidthMm, type.SheetHeightMm, type.CutGapMm));
        }).ToList();
        return View(row);
    }

    public static decimal ListTotal(GlassList list) => list.Items.Sum(RowPrice);
    public static decimal RowArea(GlassListItem i) => i.WidthMm * i.HeightMm / 1_000_000m * i.Quantity;
    public static decimal RowPrice(GlassListItem i) => decimal.Round(RowArea(i) * (i.GlassType?.PricePerM2 ?? 0), 2, MidpointRounding.AwayFromZero);

    [HttpPost("teklife-ekle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToQuote(int id, int quoteId)
    {
        var row = await _db.GlassLists.AsNoTracking().Include(x => x.Items).ThenInclude(i => i.GlassType).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var quote = await _db.Quotes.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == quoteId);
        if (quote is null || quote.Status != QuoteStatus.Draft)
        {
            TempData["Notice"] = "Cam listesi yalnızca taslak durumundaki teklife eklenebilir.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var total = ListTotal(row);
        if (total <= 0)
        {
            TempData["Notice"] = "Teklife eklemek için cam fiyatları girilmiş olmalıdır.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var description = $"Camlar - {row.Name}";
        quote.Items.Add(new QuoteItem { Description = description.Length > 200 ? description[..200] : description, Quantity = 1, Unit = "Adet", UnitPrice = total, SortOrder = quote.Items.Count });
        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("add-glass-list", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; list={id}; total={quote.GrandTotal}");
        TempData["Notice"] = "Cam listesi teklife eklendi.";
        return Redirect($"/yonetim/crm/teklifler/detay/{quote.Id}");
    }

    // Doğrama çiziminden yalnızca cam ölçüleri alınır; sonrasında liste bağımsızdır.
    [HttpPost("cizimden/{designId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FromDesign(int designId, int? listId)
    {
        var design = await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).FirstOrDefaultAsync(x => x.Id == designId);
        if (design is null) return NotFound();
        var calculation = DrawingCalculation.Calculate(design, design.ProfileSystem!);
        var back = $"/yonetim/cizim/imalat/{designId}";
        if (calculation is not { IsValid: true } || calculation.Glass.Count == 0)
        {
            TempData["Notice"] = "Bu çizimde aktarılacak cam ölçüsü yok.";
            return Redirect(back);
        }
        var glassType = await _db.GlassTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).FirstOrDefaultAsync();
        if (glassType is null)
        {
            TempData["Notice"] = "Önce en az bir aktif cam tipi tanımlayın.";
            return RedirectToAction(nameof(Types));
        }

        var list = listId is null ? null : await _db.GlassLists.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == listId);
        if (list is null)
        {
            list = new GlassList { Name = design.Name + " camları", CustomerId = design.CustomerId };
            _db.GlassLists.Add(list);
        }
        var order = list.Items.Count;
        foreach (var glass in calculation.Glass)
            list.Items.Add(new GlassListItem { GlassTypeId = glassType.Id, Position = design.Name.Length > 40 ? design.Name[..40] : design.Name, WidthMm = glass.WidthMm, HeightMm = glass.HeightMm, Quantity = glass.Count * design.Quantity, Shaped = glass.Shaped, SortOrder = order++ });
        list.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("from-design", "GlassList", list.Id.ToString(), $"design={designId}");
        TempData["Notice"] = "Cam ölçüleri listeye aktarıldı. Cam tipini listeden seçebilirsiniz.";
        return RedirectToAction(nameof(Details), new { id = list.Id });
    }

    [HttpPost("kopyala/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Copy(int id)
    {
        var row = await _db.GlassLists.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        var name = row.Name + " (kopya)";
        var copy = new GlassList
        {
            Name = name.Length > 160 ? name[..160] : name, CustomerId = row.CustomerId, Notes = row.Notes,
            Items = row.Items.Select(i => new GlassListItem { GlassTypeId = i.GlassTypeId, Position = i.Position, WidthMm = i.WidthMm, HeightMm = i.HeightMm, Quantity = i.Quantity, Shaped = i.Shaped, Note = i.Note, SortOrder = i.SortOrder }).ToList()
        };
        _db.GlassLists.Add(copy);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("copy", "GlassList", copy.Id.ToString(), copy.Name);
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.GlassLists.FindAsync(id);
        if (row is null) return NotFound();
        _db.GlassLists.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "GlassList", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("tipler")]
    public async Task<IActionResult> Types() => View(await _db.GlassTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync());

    [HttpGet("tipler/ekle")]
    public IActionResult CreateType() => View("EditType", new GlassType());

    [HttpPost("tipler/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateType(GlassType input)
    {
        input.Id = 0;
        if (!ModelState.IsValid) return View("EditType", input);
        _db.GlassTypes.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "GlassType", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Types));
    }

    [HttpGet("tipler/duzenle/{id:int}")]
    public async Task<IActionResult> EditType(int id)
    {
        var row = await _db.GlassTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("tipler/duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditType(int id, GlassType input)
    {
        input.Id = id;
        if (!await _db.GlassTypes.AnyAsync(x => x.Id == id)) return NotFound();
        if (!ModelState.IsValid) return View(input);
        _db.GlassTypes.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "GlassType", id.ToString(), input.Name);
        return RedirectToAction(nameof(Types));
    }

    [HttpPost("tipler/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteType(int id)
    {
        var row = await _db.GlassTypes.FindAsync(id);
        if (row is null) return NotFound();
        if (await _db.GlassListItems.AnyAsync(x => x.GlassTypeId == id))
        {
            TempData["Notice"] = "Bu camı kullanan listeler var; silmek yerine pasif yapın.";
            return RedirectToAction(nameof(Types));
        }
        _db.GlassTypes.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "GlassType", id.ToString(), row.Name);
        return RedirectToAction(nameof(Types));
    }

    private async Task<List<GlassRowInput>?> Validate(GlassListInputVm input)
    {
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");

        List<GlassRowInput>? rows;
        try { rows = JsonSerializer.Deserialize<List<GlassRowInput>>(input.ItemsJson, JsonOptions); }
        catch (JsonException) { rows = null; }
        if (rows is null)
        {
            ModelState.AddModelError(nameof(input.ItemsJson), "Cam ölçüleri okunamadı.");
            return null;
        }

        if (rows.Count == 0) ModelState.AddModelError(string.Empty, "En az bir cam ölçüsü girin.");
        if (rows.Count > MaxRows) ModelState.AddModelError(string.Empty, $"Bir listede en fazla {MaxRows} satır olabilir.");
        var types = await _db.GlassTypes.Select(x => x.Id).ToListAsync();
        for (var i = 0; i < Math.Min(rows.Count, MaxRows); i++)
        {
            var r = rows[i];
            if (r.WidthMm is <= 0 or > 20000 || r.HeightMm is <= 0 or > 20000) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: ölçüler 0-20000 mm arasında olmalıdır.");
            if (r.Quantity is < 1 or > 100000) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: adet en az 1 olmalıdır.");
            if (!types.Contains(r.GlassTypeId)) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: cam tipi seçin.");
            if ((r.Position?.Trim().Length ?? 0) > 40) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: poz en fazla 40 karakter olabilir.");
            if ((r.Note?.Trim().Length ?? 0) > 200) ModelState.AddModelError(string.Empty, $"{i + 1}. satır: not en fazla 200 karakter olabilir.");
        }
        return rows;
    }

    private static void Apply(GlassList row, GlassListInputVm input, List<GlassRowInput> rows)
    {
        row.Name = input.Name.Trim();
        row.CustomerId = input.CustomerId;
        row.Notes = input.Notes?.Trim();
        var order = 0;
        foreach (var r in rows)
            row.Items.Add(new GlassListItem { GlassTypeId = r.GlassTypeId, Position = r.Position?.Trim(), WidthMm = decimal.Round(r.WidthMm, 1), HeightMm = decimal.Round(r.HeightMm, 1), Quantity = r.Quantity, Shaped = r.Shaped, Note = r.Note?.Trim(), SortOrder = order++ });
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.GlassTypes = await _db.GlassTypes.AsNoTracking().OrderBy(x => x.Name).Select(x => new { id = x.Id, name = x.Name, price = x.PricePerM2, active = x.IsActive }).ToListAsync();
    }
}
