using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/crm/teklifler")]
public sealed class QuotesController : Controller
{
    private const int MaxDocumentBytes = 1_000_000;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly LengthUnits _units;
    private readonly PresentationBuilder _presentation;
    public QuotesController(AppDbContext db, IAuditService audit, LengthUnits units, PresentationBuilder presentation) { _db = db; _audit = audit; _units = units; _presentation = presentation; }

    [HttpGet("")]
    public async Task<IActionResult> Index(QuoteStatus? status)
    {
        ViewBag.Status = status;
        var query = _db.Quotes.AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return View(await query.OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync());
    }

    // Teklif çoğunlukla çizim listesinden hazırlanır: pozlar maliyetleriyle kalem olur, çizimler eke girer.
    [HttpGet("ekle")]
    public async Task<IActionResult> Create([FromQuery(Name = "liste")] int? listId)
    {
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Quote);
        var block = new DocumentBlock();
        var content = new DocumentContent { Intro = template.Intro, Closing = template.Closing, Note = template.DefaultNote, Blocks = { block } };
        var input = new QuoteInputViewModel { ValidUntilUtc = DateTime.UtcNow.Date.AddDays(14) };
        var list = listId is null ? null : await _db.DrawingLists.AsNoTracking().FirstOrDefaultAsync(x => x.Id == listId);
        if (list is not null)
        {
            var glass = await _db.GlassTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync();
            input.DrawingListId = list.Id;
            input.CustomerId = list.CustomerId ?? 0;
            input.Title = list.Name.Length > 180 ? list.Name[..180] : list.Name;
            input.GlassTypeId = glass?.Id;
            input.MarginPercent = await QuotePricing.DefaultMarginAsync(_db, list.Id);
            foreach (var (design, cost) in await QuotePricing.ForListAsync(_db, list.Id))
            {
                var unitCost = cost.UnitCost(glass?.PricePerM2 ?? 0);
                block.Items.Add(new BlockItem { Description = Describe(design), Quantity = design.Quantity, Unit = "Adet", DesignId = design.Id, UnitCost = unitCost, UnitPrice = QuoteRules.PriceFromCost(unitCost, input.MarginPercent) });
                content.DesignIds.Add(design.Id);
            }
        }
        if (block.Items.Count == 0) block.Items.Add(new BlockItem());
        input.DocumentJson = DocumentJson.Serialize(content);
        await LoadOptions(input.DrawingListId);
        return View("Editor", input);
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(MaxDocumentBytes)]
    public async Task<IActionResult> Create(QuoteInputViewModel input)
    {
        var content = await Validate(input);
        if (content is null || !ModelState.IsValid) { await LoadOptions(input.DrawingListId); return View("Editor", input); }

        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Quote);
        var quote = new Quote
        {
            QuoteNumber = $"KA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Status = QuoteStatus.Draft, CreatedUtc = DateTime.UtcNow, SnapshotJson = DocumentJson.Serialize(template)
        };
        await ApplyAsync(quote, input, content);
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; total={quote.GrandTotal}");
        TempData["Notice"] = $"{quote.QuoteNumber} numaralı teklif oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = quote.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        if (quote.Status != QuoteStatus.Draft) return NotDraft(id);

        var content = DocumentJson.ParseContent(quote.DocumentJson);
        // Çizimden gelen kalemlerin maliyeti çizimin güncel haliyle yeniden hesaplanır; elle yazılmamış fiyat da buna uyar.
        var costs = await CostsAsync(quote.DrawingListId);
        var glassPrice = await GlassPriceAsync(quote.GlassTypeId);
        // Bloklarda henüz olmayan kalemler (ör. eski teklifler) editörde görünsün.
        content.Blocks = DocumentViews.BuildBlocks(quote, content).Select(view =>
        {
            view.Block.Items = view.Items.Select(i =>
            {
                var manual = i.UnitPrice != QuoteRules.PriceFromCost(i.UnitCost, quote.MarginPercent);
                var cost = i.WindowDesignId is int d && costs.TryGetValue(d, out var c) ? c.LineCost(glassPrice, i.ManualBaseCost, i.ManualGlassCost) : i.UnitCost;
                return new BlockItem { Description = i.Description, Quantity = i.Quantity, Unit = i.Unit, DesignId = i.WindowDesignId, UnitCost = cost, ManualPrice = manual, ManualBaseCost = i.ManualBaseCost, ManualGlassCost = i.ManualGlassCost, UnitPrice = manual ? i.UnitPrice : QuoteRules.PriceFromCost(cost, quote.MarginPercent) };
            }).ToList();
            return view.Block;
        }).ToList();
        await LoadOptions(quote.DrawingListId);
        return View("Editor", new QuoteInputViewModel
        {
            Id = quote.Id, CustomerId = quote.CustomerId, Title = quote.Title, DocumentDate = quote.DocumentDate, ValidUntilUtc = quote.ValidUntilUtc,
            VatMode = quote.VatMode, TaxRate = quote.TaxRate, DiscountRate = quote.DiscountRate, DocumentJson = DocumentJson.Serialize(content),
            DrawingListId = quote.DrawingListId, GlassTypeId = quote.GlassTypeId, MarginPercent = quote.MarginPercent
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(MaxDocumentBytes)]
    public async Task<IActionResult> Edit(int id, QuoteInputViewModel input)
    {
        var quote = await _db.Quotes.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        if (quote.Status != QuoteStatus.Draft) return NotDraft(id);

        input.Id = id;
        var content = await Validate(input);
        if (content is null || !ModelState.IsValid) { await LoadOptions(input.DrawingListId); return View("Editor", input); }

        _db.QuoteItems.RemoveRange(quote.Items);
        quote.Items.Clear();
        await ApplyAsync(quote, input, content);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; total={quote.GrandTotal}");
        TempData["Notice"] = "Teklif güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).Include(x => x.DrawingList).Include(x => x.GlassType).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        ViewBag.ContractId = await _db.Contracts.Where(x => x.QuoteId == id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        return View(quote);
    }

    // sunum=1: kurumsal sunum teklifin önüne eklenir, kapağa müşteri ve teklif bilgileri yazılır.
    // Sunum dikey basılır: Chrome aynı belgede yatay ve dikey sayfayı karıştırınca tüm sayfaları küçültür.
    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id, [FromQuery(Name = "sunum")] int withPresentation = 0)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        var content = DocumentJson.ParseContent(quote.DocumentJson);
        var template = await DocumentViews.TemplateForAsync(_db, DocumentKinds.Quote, quote.SnapshotJson);
        var attachments = await DocumentViews.LoadAttachmentsAsync(_db, content.DesignIds);
        var customer = quote.Customer?.Name ?? "";
        var presentation = withPresentation == 1 ? await _presentation.BuildAsync(new PresentationCover(customer, quote.Title, quote.QuoteNumber, quote.DocumentDate), portrait: true) : null;
        return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", new QuotePrintVm(quote, customer, template, content, DocumentViews.BuildBlocks(quote, content), attachments, presentation));
    }

    // Taslak teklif, şablon ayarlarındaki güncel logo ve firma bilgilerini alır.
    [HttpPost("sablonu-yenile/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshTemplate(int id)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        if (quote.Status != QuoteStatus.Draft) return NotDraft(id);
        quote.SnapshotJson = DocumentJson.Serialize(await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Quote));
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Teklif, güncel şablon ayarlarını kullanacak şekilde yenilendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, QuoteStatus status)
    {
        var quote = await _db.Quotes.FindAsync(id);
        if (quote is null) return NotFound();
        if (!Enum.IsDefined(status) || status == QuoteStatus.Converted)
        {
            TempData["Notice"] = "Geçersiz durum. Sözleşmeye dönüştürme, kabul edilen teklifte sözleşme oluşturularak yapılır.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Form mevcut durumu seçili getirir; değiştirmeden kaydetmek hata değildir.
        if (status == quote.Status)
        {
            TempData["Notice"] = "Teklif durumu değişmedi.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!QuoteRules.CanTransition(quote.Status, status))
        {
            TempData["Notice"] = "Bu teklif için seçilen durum geçişine izin verilmiyor.";
            return RedirectToAction(nameof(Details), new { id });
        }

        quote.Status = status;
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Teklif durumu güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private RedirectToActionResult NotDraft(int id)
    {
        TempData["Notice"] = "Yalnızca taslak durumundaki teklif düzenlenebilir.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<DocumentContent?> Validate(QuoteInputViewModel input)
    {
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        if (!Enum.IsDefined(input.VatMode)) ModelState.AddModelError(nameof(input.VatMode), "Geçersiz KDV seçeneği.");
        if (input.DrawingListId is int listId && !await _db.DrawingLists.AnyAsync(x => x.Id == listId)) input.DrawingListId = null;
        if (input.GlassTypeId is int glassId && !await _db.GlassTypes.AnyAsync(x => x.Id == glassId)) ModelState.AddModelError(nameof(input.GlassTypeId), "Geçerli bir cam tipi seçin.");

        var content = DocumentJson.TryParseContent(input.DocumentJson);
        if (content is null)
        {
            ModelState.AddModelError(nameof(input.DocumentJson), "Teklif içeriği okunamadı.");
            return null;
        }

        var names = content.Blocks.Select(b => b.Name?.Trim() ?? "").ToList();
        if (names.Distinct().Count() != names.Count) ModelState.AddModelError(string.Empty, "Blok adları birbirinden farklı olmalıdır.");
        if (names.Any(n => n.Length > 120)) ModelState.AddModelError(string.Empty, "Blok adı en fazla 120 karakter olabilir.");

        var items = content.Blocks.SelectMany(b => b.Items).Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        if (items.Count == 0) ModelState.AddModelError(string.Empty, "En az bir teklif kalemi girin (açıklama zorunludur).");
        if (items.Any(i => i.UnitCost < 0 || i.ManualBaseCost < 0 || i.ManualGlassCost < 0)) ModelState.AddModelError(string.Empty, "Kalem maliyeti sıfır veya daha büyük olmalıdır.");
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) ModelState.AddModelError(string.Empty, "Kalem miktarı sıfırdan büyük, fiyatı sıfır veya daha büyük olmalıdır.");
        if (items.Any(i => i.Description!.Trim().Length > 200)) ModelState.AddModelError(string.Empty, "Kalem açıklaması en fazla 200 karakter olabilir.");
        if (items.Any(i => (i.Unit?.Trim().Length ?? 0) > 20)) ModelState.AddModelError(string.Empty, "Birim en fazla 20 karakter olabilir.");
        return content;
    }

    private async Task ApplyAsync(Quote quote, QuoteInputViewModel input, DocumentContent content)
    {
        quote.CustomerId = input.CustomerId;
        quote.Title = input.Title.Trim();
        quote.DocumentDate = DateTime.SpecifyKind(input.DocumentDate.Date, DateTimeKind.Utc);
        quote.ValidUntilUtc = input.ValidUntilUtc.HasValue ? DateTime.SpecifyKind(input.ValidUntilUtc.Value.Date, DateTimeKind.Utc) : null;
        quote.VatMode = input.VatMode;
        quote.TaxRate = input.TaxRate;
        quote.DiscountRate = input.DiscountRate;
        quote.DrawingListId = input.DrawingListId;
        quote.GlassTypeId = input.GlassTypeId;
        quote.MarginPercent = input.MarginPercent;

        // Çizim kalemlerinin maliyeti tarayıcıdan değil çizimden gelir; listede olmayan poz elle girilmiş kalem sayılır.
        var costs = await CostsAsync(quote.DrawingListId);
        var glassPrice = await GlassPriceAsync(quote.GlassTypeId);
        var order = 0;
        foreach (var block in content.Blocks)
        {
            var name = string.IsNullOrWhiteSpace(block.Name) ? null : block.Name.Trim();
            foreach (var item in block.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)))
            {
                var linked = item.DesignId is int d && costs.TryGetValue(d, out var cost) ? cost : null;
                // Taşerona verilen imalat veya cam elle girilen tutarla, kalanı çizimden hesaplanır.
                decimal? Money(decimal? v) => v is decimal x ? decimal.Round(x, 2, MidpointRounding.AwayFromZero) : null;
                var manualBase = linked is null ? null : Money(item.ManualBaseCost);
                var manualGlass = linked is null ? null : Money(item.ManualGlassCost);
                var unitCost = linked?.LineCost(glassPrice, manualBase, manualGlass) ?? decimal.Round(item.UnitCost, 2, MidpointRounding.AwayFromZero);
                // Marj fiyatı yalnızca çizime bağlı, elle yazılmamış kalemde sunucuda yeniden hesaplanır.
                var unitPrice = linked is not null && !item.ManualPrice ? QuoteRules.PriceFromCost(unitCost, quote.MarginPercent) : item.UnitPrice;
                quote.Items.Add(new QuoteItem { BlockName = name, SortOrder = order++, Description = item.Description!.Trim(), Quantity = item.Quantity, Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Adet" : item.Unit.Trim(), UnitPrice = unitPrice, UnitCost = unitCost, WindowDesignId = linked?.DesignId, ManualBaseCost = manualBase, ManualGlassCost = manualGlass });
            }
            block.Items.Clear(); // kalemlerin tek kaynağı QuoteItems tablosudur
        }
        quote.DocumentJson = DocumentJson.Serialize(content);

        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
        quote.CostTotal = decimal.Round(quote.Items.Sum(i => i.UnitCost * i.Quantity), 2, MidpointRounding.AwayFromZero);
    }

    private async Task<Dictionary<int, DesignCost>> CostsAsync(int? listId)
        => listId is int id ? (await QuotePricing.ForListAsync(_db, id)).ToDictionary(x => x.Design.Id, x => x.Cost) : new();

    private async Task<decimal> GlassPriceAsync(int? glassTypeId)
        => glassTypeId is int id ? await _db.GlassTypes.Where(x => x.Id == id).Select(x => x.PricePerM2).FirstOrDefaultAsync() : 0m;

    private string Describe(WindowDesign row)
    {
        var text = $"{row.Name} - {_units.Format(row.WidthMm)}x{_units.Format(row.HeightMm)} {_units.Code} ({ProductCategories.Label(row.Category)})";
        return text.Length > 200 ? text[..200] : text;
    }

    private async Task LoadOptions(int? listId)
    {
        ViewBag.ListName = listId is int lid ? await _db.DrawingLists.Where(x => x.Id == lid).Select(x => x.Name).FirstOrDefaultAsync() : null;
        ViewBag.GlassTypes = await _db.GlassTypes.AsNoTracking().OrderBy(x => x.Id).Select(x => new GlassOption(x.Id, x.Name, x.PricePerM2, x.IsActive)).ToListAsync();
        ViewBag.DesignCosts = (await CostsAsync(listId)).Values.Select(c => new DesignCostOption(c.DesignId, c.BaseCost, c.GlassAreaM2, c.Problem)).ToList();
        ViewBag.Customers = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToListAsync();
        ViewBag.Clauses = await _db.TemplateClauses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync();
        ViewBag.Designs = await _db.WindowDesigns.AsNoTracking().OrderByDescending(x => x.DrawingList!.UpdatedUtc).ThenBy(x => x.DrawingListId).ThenBy(x => x.SortOrder).Take(200).Select(x => new { id = x.Id, listId = x.DrawingListId, listName = x.DrawingList!.Name, name = x.Name, widthMm = x.WidthMm, heightMm = x.HeightMm, quantity = x.Quantity }).ToListAsync();
    }
}
