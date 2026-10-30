using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/crm/teklifler")]
public sealed class QuotesController : Controller
{
    private const int MaxDocumentBytes = 1_000_000;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public QuotesController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(QuoteStatus? status)
    {
        ViewBag.Status = status;
        var query = _db.Quotes.AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return View(await query.OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync());
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Quote);
        var content = new DocumentContent { Intro = template.Intro, Closing = template.Closing, Note = template.DefaultNote, Blocks = { new DocumentBlock { Items = { new BlockItem() } } } };
        await LoadOptions();
        return View("Editor", new QuoteInputViewModel { ValidUntilUtc = DateTime.UtcNow.Date.AddDays(14), DocumentJson = DocumentJson.Serialize(content) });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(MaxDocumentBytes)]
    public async Task<IActionResult> Create(QuoteInputViewModel input)
    {
        var content = await Validate(input);
        if (content is null || !ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Quote);
        var quote = new Quote
        {
            QuoteNumber = $"KA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Status = QuoteStatus.Draft, CreatedUtc = DateTime.UtcNow, SnapshotJson = DocumentJson.Serialize(template)
        };
        Apply(quote, input, content);
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
        // Bloklarda henüz olmayan kalemler (ör. çizimden eklenenler) editörde görünsün.
        content.Blocks = DocumentViews.BuildBlocks(quote, content).Select(view =>
        {
            view.Block.Items = view.Items.Select(i => new BlockItem { Description = i.Description, Quantity = i.Quantity, Unit = i.Unit, UnitPrice = i.UnitPrice }).ToList();
            return view.Block;
        }).ToList();
        await LoadOptions();
        return View("Editor", new QuoteInputViewModel
        {
            Id = quote.Id, CustomerId = quote.CustomerId, Title = quote.Title, DocumentDate = quote.DocumentDate, ValidUntilUtc = quote.ValidUntilUtc,
            VatMode = quote.VatMode, TaxRate = quote.TaxRate, DiscountRate = quote.DiscountRate, DocumentJson = DocumentJson.Serialize(content)
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
        if (content is null || !ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        _db.QuoteItems.RemoveRange(quote.Items);
        quote.Items.Clear();
        Apply(quote, input, content);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Quote", quote.Id.ToString(), $"{quote.QuoteNumber}; total={quote.GrandTotal}");
        TempData["Notice"] = "Teklif güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        ViewBag.ContractId = await _db.Contracts.Where(x => x.QuoteId == id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        return View(quote);
    }

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        var content = DocumentJson.ParseContent(quote.DocumentJson);
        var template = await DocumentViews.TemplateForAsync(_db, DocumentKinds.Quote, quote.SnapshotJson);
        var attachments = await DocumentViews.LoadAttachmentsAsync(_db, content.DesignIds);
        return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", new QuotePrintVm(quote, quote.Customer?.Name ?? "", template, content, DocumentViews.BuildBlocks(quote, content), attachments));
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
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) ModelState.AddModelError(string.Empty, "Kalem miktarı sıfırdan büyük, fiyatı sıfır veya daha büyük olmalıdır.");
        if (items.Any(i => i.Description!.Trim().Length > 200)) ModelState.AddModelError(string.Empty, "Kalem açıklaması en fazla 200 karakter olabilir.");
        if (items.Any(i => (i.Unit?.Trim().Length ?? 0) > 20)) ModelState.AddModelError(string.Empty, "Birim en fazla 20 karakter olabilir.");
        return content;
    }

    private static void Apply(Quote quote, QuoteInputViewModel input, DocumentContent content)
    {
        quote.CustomerId = input.CustomerId;
        quote.Title = input.Title.Trim();
        quote.DocumentDate = DateTime.SpecifyKind(input.DocumentDate.Date, DateTimeKind.Utc);
        quote.ValidUntilUtc = input.ValidUntilUtc.HasValue ? DateTime.SpecifyKind(input.ValidUntilUtc.Value.Date, DateTimeKind.Utc) : null;
        quote.VatMode = input.VatMode;
        quote.TaxRate = input.TaxRate;
        quote.DiscountRate = input.DiscountRate;

        var order = 0;
        foreach (var block in content.Blocks)
        {
            var name = string.IsNullOrWhiteSpace(block.Name) ? null : block.Name.Trim();
            foreach (var item in block.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)))
                quote.Items.Add(new QuoteItem { BlockName = name, SortOrder = order++, Description = item.Description!.Trim(), Quantity = item.Quantity, Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Adet" : item.Unit.Trim(), UnitPrice = item.UnitPrice });
            block.Items.Clear(); // kalemlerin tek kaynağı QuoteItems tablosudur
        }
        quote.DocumentJson = DocumentJson.Serialize(content);

        var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
        quote.Subtotal = totals.Subtotal; quote.DiscountAmount = totals.DiscountAmount;
        quote.TaxAmount = totals.TaxAmount; quote.GrandTotal = totals.GrandTotal;
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToListAsync();
        ViewBag.Clauses = await _db.TemplateClauses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync();
        ViewBag.Designs = await _db.WindowDesigns.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).Take(200).Select(x => new { id = x.Id, name = x.Name, widthMm = x.WidthMm, heightMm = x.HeightMm, quantity = x.Quantity }).ToListAsync();
    }
}
