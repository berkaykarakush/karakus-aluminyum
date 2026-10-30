using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin"), Route("yonetim/sablonlar")]
public sealed class DocumentTemplatesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _audit;
    public DocumentTemplatesController(AppDbContext db, IWebHostEnvironment env, IAuditService audit) { _db = db; _env = env; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.TemplateClauses.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync());

    [HttpGet("{kind:regex(^(quote|contract)$)}")]
    public async Task<IActionResult> Edit(string kind)
    {
        ViewBag.Articles = DocumentJson.ParseArticles((await DocumentViews.CurrentTemplateAsync(_db, kind)).ArticlesJson);
        return View(await DocumentViews.CurrentTemplateAsync(_db, kind));
    }

    [HttpPost("{kind:regex(^(quote|contract)$)}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string kind, DocumentTemplate input, List<ContractArticle>? articles, IFormFile? logoFile, bool removeLogo = false)
    {
        var row = await _db.DocumentTemplates.FirstAsync(x => x.Kind == kind);
        var cleanArticles = (articles ?? new()).Where(a => !string.IsNullOrWhiteSpace(a.Title)).ToList();
        ModelState.Remove(nameof(input.Kind));
        if (cleanArticles.Any(a => !ContractArticleKinds.All.Contains(a.Kind))) ModelState.AddModelError(string.Empty, "Geçersiz madde tipi.");
        if (kind == DocumentKinds.Contract)
            foreach (var required in new[] { ContractArticleKinds.Subject, ContractArticleKinds.Price, ContractArticleKinds.Payment })
                if (cleanArticles.Count(a => a.Kind == required) != 1)
                    ModelState.AddModelError(string.Empty, "Sözleşmede konu, bedel ve ödeme maddelerinden tam birer tane olmalıdır.");

        string? uploaded = null;
        try { uploaded = await AdminImageUpload.SaveAsync(logoFile, _env); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }

        if (!ModelState.IsValid)
        {
            ViewBag.Articles = articles ?? new();
            input.Kind = kind;
            input.LogoPath = row.LogoPath;
            return View(input);
        }

        row.Title = input.Title.Trim();
        row.LogoPath = uploaded ?? (removeLogo ? null : row.LogoPath);
        row.SellerName = input.SellerName?.Trim();
        row.SellerLegalName = input.SellerLegalName?.Trim();
        row.Signatory = input.Signatory?.Trim();
        row.SignatoryTitle = input.SignatoryTitle?.Trim();
        row.SellerAddress = input.SellerAddress?.Trim();
        row.SellerTaxInfo = input.SellerTaxInfo?.Trim();
        row.Phone = input.Phone?.Trim();
        row.FooterAddress = input.FooterAddress?.Trim();
        row.Intro = input.Intro;
        row.Closing = input.Closing;
        row.SignOff = input.SignOff;
        row.DefaultNote = input.DefaultNote;
        row.BuyerLabel = input.BuyerLabel?.Trim();
        row.SellerLabel = input.SellerLabel?.Trim();
        if (kind == DocumentKinds.Contract) row.ArticlesJson = DocumentJson.Serialize(cleanArticles);
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "DocumentTemplate", row.Id.ToString(), kind);
        TempData["Notice"] = "Şablon kaydedildi. Yeni belgeler bu ayarları kullanır; mevcut taslaklar için belge sayfasından \"Şablonu yenile\" seçin.";
        return RedirectToAction(nameof(Edit), new { kind });
    }

    // Kayıt oluşturmadan, şablonun örnek verilerle nasıl basılacağını gösterir.
    [HttpGet("{kind:regex(^(quote|contract)$)}/onizleme")]
    public async Task<IActionResult> Preview(string kind)
    {
        var template = await DocumentViews.CurrentTemplateAsync(_db, kind);
        var none = new AttachmentList(Array.Empty<AttachmentItem>());
        var content = new DocumentContent
        {
            Intro = template.Intro, Closing = template.Closing, Note = template.DefaultNote, Subject = "örnek işin tanımı burada yer alır.",
            Variables = new() { ["proje"] = "örnek proje", ["is_tanimi"] = "doğrama ve cam" },
            Blocks = { new DocumentBlock { Name = "Örnek Blok", Clauses = { new BlockClause { Title = "Örnek madde", Text = "Hazır madde metinleri burada listelenir." } } } },
            Payments = { new PaymentLine { Kind = PaymentKinds.Cash, Amount = 100000 } }
        };
        if (kind == DocumentKinds.Quote)
        {
            var quote = new Quote { DocumentDate = DateTime.UtcNow, VatMode = VatMode.Included, GrandTotal = 100000, Subtotal = 100000, Items = { new QuoteItem { BlockName = "Örnek Blok", Description = "Örnek kalem", Quantity = 1, UnitPrice = 100000, LineTotal = 100000 } } };
            return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", new QuotePrintVm(quote, "Örnek Müşteri", template, content, DocumentViews.BuildBlocks(quote, content), none));
        }
        var contract = new Contract { BuyerName = "Örnek Müşteri", BuyerAddress = "Örnek adres", TotalAmount = 100000, DocumentDate = DateTime.UtcNow, VatMode = VatMode.Included };
        return View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", new ContractPrintVm(contract, template, content, DocumentJson.ParseArticles(template.ArticlesJson), none));
    }

    [HttpGet("maddeler/ekle")]
    public IActionResult CreateClause() => View("EditClause", new TemplateClause());

    [HttpPost("maddeler/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClause(TemplateClause input)
    {
        input.Id = 0;
        if (!ModelState.IsValid) return View("EditClause", input);
        _db.TemplateClauses.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "TemplateClause", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("maddeler/duzenle/{id:int}")]
    public async Task<IActionResult> EditClause(int id)
    {
        var row = await _db.TemplateClauses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost("maddeler/duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditClause(int id, TemplateClause input)
    {
        input.Id = id;
        if (!await _db.TemplateClauses.AnyAsync(x => x.Id == id)) return NotFound();
        if (!ModelState.IsValid) return View(input);
        _db.TemplateClauses.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "TemplateClause", id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("maddeler/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClause(int id)
    {
        var row = await _db.TemplateClauses.FindAsync(id);
        if (row is null) return NotFound();
        _db.TemplateClauses.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "TemplateClause", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }
}
