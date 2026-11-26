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
    private readonly DocumentBrandingProvider _branding;
    public DocumentTemplatesController(AppDbContext db, IWebHostEnvironment env, IAuditService audit, DocumentBrandingProvider branding) { _db = db; _env = env; _audit = audit; _branding = branding; }

    // Tüm belgelerin ortak anteti: logo, iletişim, alt bilgi, sayfa numarası ve filigran.
    [HttpGet("antet")]
    public async Task<IActionResult> Branding() => View(await _branding.GetAsync());

    [HttpPost("antet"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Branding(DocumentBranding input, IFormFile? logoFile, bool removeLogo = false)
    {
        var current = await _branding.GetAsync();
        if (!WatermarkModes.All.Contains(input.WatermarkMode)) ModelState.AddModelError(nameof(input.WatermarkMode), "Geçersiz filigran seçeneği.");
        string? uploaded = null;
        try { uploaded = await AdminImageUpload.SaveAsync(logoFile, _env); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        input.LogoPath = uploaded ?? (removeLogo ? null : current.LogoPath);
        if (input.WatermarkMode == WatermarkModes.Logo && string.IsNullOrWhiteSpace(input.LogoPath)) ModelState.AddModelError(nameof(input.WatermarkMode), "Logo filigranı için önce logo yükleyin.");
        foreach (var property in typeof(DocumentBranding).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite))
            if (property.GetValue(input) is string text) property.SetValue(input, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        input.AccentColor ??= "#b0202a";
        input.WatermarkMode ??= WatermarkModes.None;
        if (ModelState.IsValid)
        {
            try { await _branding.SaveAsync(input); }
            catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        }
        if (!ModelState.IsValid) return View(input);
        await _audit.RecordAsync("update", "DocumentBranding", "1", "Antet ve filigran");
        TempData["Notice"] = "Antet ve filigran kaydedildi. Tüm belgeler yeni ayarla basılır.";
        return RedirectToAction(nameof(Branding));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.TemplateClauses.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync());

    [HttpGet("{kind:regex(^(quote|contract)$)}")]
    public async Task<IActionResult> Edit(string kind)
    {
        var template = await DocumentViews.CurrentTemplateAsync(_db, kind);
        ViewBag.Articles = DocumentJson.ParseArticles(template.ArticlesJson);
        ViewBag.Terms = DocumentJson.ParseTerms(template.TermsJson);
        ViewBag.Variables = DocumentJson.ParseVariables(template.VariablesJson);
        return View(template);
    }

    [HttpPost("{kind:regex(^(quote|contract)$)}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string kind, DocumentTemplate input, List<ContractArticle>? articles, List<BlockClause>? terms, string? variablesText)
    {
        var row = await _db.DocumentTemplates.FirstAsync(x => x.Kind == kind);
        var cleanArticles = (articles ?? new()).Where(a => !string.IsNullOrWhiteSpace(a.Title)).ToList();
        var cleanTerms = new DocumentContent { Terms = terms ?? new() };
        DocumentTerms.Validate(cleanTerms, ModelState);
        var variables = ParseVariables(variablesText);
        if (variables is null) ModelState.AddModelError(string.Empty, "Varsayılan değerlerde her satır \"alan = değer\" biçiminde olmalı; alan adı harf, rakam ve _ içerebilir.");
        ModelState.Remove(nameof(input.Kind));
        if (cleanArticles.Any(a => !ContractArticleKinds.All.Contains(a.Kind))) ModelState.AddModelError(string.Empty, "Geçersiz madde tipi.");
        if (kind == DocumentKinds.Contract)
            foreach (var required in new[] { ContractArticleKinds.Subject, ContractArticleKinds.Price, ContractArticleKinds.Payment })
                if (cleanArticles.Count(a => a.Kind == required) != 1)
                    ModelState.AddModelError(string.Empty, "Sözleşmede konu, bedel ve ödeme maddelerinden tam birer tane olmalıdır.");


        if (!ModelState.IsValid)
        {
            ViewBag.Articles = articles ?? new();
            ViewBag.Terms = terms ?? new();
            ViewBag.Variables = variables ?? DocumentJson.ParseVariables(row.VariablesJson);
            input.Kind = kind;
            input.LogoPath = row.LogoPath;
            return View(input);
        }

        row.Title = input.Title.Trim();
        row.SellerName = input.SellerName?.Trim();
        row.SellerLegalName = input.SellerLegalName?.Trim();
        row.Signatory = input.Signatory?.Trim();
        row.SignatoryTitle = input.SignatoryTitle?.Trim();
        row.SellerAddress = input.SellerAddress?.Trim();
        row.SellerTaxInfo = input.SellerTaxInfo?.Trim();
        row.Intro = input.Intro;
        row.Closing = input.Closing;
        row.SignOff = input.SignOff;
        row.DefaultNote = input.DefaultNote;
        row.BuyerLabel = input.BuyerLabel?.Trim();
        row.SellerLabel = input.SellerLabel?.Trim();
        row.SellerEmail = input.SellerEmail?.Trim();
        row.BankInfo = input.BankInfo?.Trim();
        row.AcceptanceText = input.AcceptanceText?.Trim();
        row.TermsJson = DocumentJson.Serialize(cleanTerms.Terms);
        row.VariablesJson = DocumentJson.Serialize(variables!);
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
        var none = new AttachmentList(Array.Empty<KarakusAluminyum.Web.Services.Drawing.PozView>());
        // Örnek teklif ve sözleşme: kalemler, iskonto, KDV ve şartlarla nasıl basıldığı görünsün.
        var content = new DocumentContent
        {
            Intro = template.Intro, Closing = template.Closing, Note = template.DefaultNote, Subject = "örnek işin tanımı burada yer alır.",
            Variables = DocumentJson.ParseVariables(template.VariablesJson), Terms = DocumentJson.ParseTerms(template.TermsJson),
            Blocks = { new DocumentBlock { Name = kind == DocumentKinds.Quote ? "PVC Doğrama" : "Örnek Blok", Clauses = { new BlockClause { Title = "Örnek madde", Text = "Hazır madde metinleri burada listelenir." } } } },
            Payments = { new PaymentLine { Kind = PaymentKinds.Cash, Amount = 100000 } }
        };
        content.Variables.TryAdd("proje", "örnek proje");
        content.Variables.TryAdd("is_tanimi", "doğrama ve cam");
        if (kind == DocumentKinds.Quote)
        {
            var quote = new Quote { QuoteNumber = "ÖRNEK", Title = "Örnek proje doğramaları", DocumentDate = DateTime.UtcNow, ValidUntilUtc = DateTime.UtcNow.AddDays(14), VatMode = VatMode.Added, TaxRate = 20, Items =
            {
                // Kurumsal teklif örneği: çok kalemli grup toplamıyla, tek kalemli grup kendi tutarıyla basılır.
                new QuoteItem { BlockName = "PVC Doğrama", Description = "4100x2200 4'lü Sürme", Quantity = 6, UnitPrice = 42000, SortOrder = 0 },
                new QuoteItem { BlockName = "PVC Doğrama", Description = "900x2200 Açılır Kapı", Quantity = 3, UnitPrice = 18000, SortOrder = 1 },
                new QuoteItem { BlockName = "PVC Doğrama", Description = "450x900 Açılır Banyo Penceresi", Quantity = 15, UnitPrice = 5200, SortOrder = 2 },
                new QuoteItem { BlockName = "Isıcam", Description = "3'lü Isıcam", Quantity = 186, Unit = "M2", UnitPrice = 3500, SortOrder = 3 },
                new QuoteItem { BlockName = "Montaj", Description = "Montaj ve nakliye", Quantity = 1, Unit = "Götürü", UnitPrice = 12000, DiscountRate = 10, SortOrder = 4 }
            } };
            var totals = QuoteRules.Calculate(quote.Items, quote.DiscountRate, quote.TaxRate, quote.VatMode);
            (quote.Subtotal, quote.DiscountAmount, quote.TaxAmount, quote.GrandTotal) = totals;
            return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", new QuotePrintVm(quote, "Örnek Müşteri", template, content, DocumentViews.BuildBlocks(quote, content), none));
        }
        content.Blocks[0].Items.Add(new BlockItem { Description = "Poz 1 - Plastik Doğrama, Açılır, 1200 × 2200 mm", Quantity = 8, Unit = "Adet", UnitPrice = 9500 });
        content.Terms.Add(new BlockClause { Title = "Örnek şart", Text = "Tekliften gelen özel şartlar burada listelenir." });
        var sample = PriceSheet.FromContent(content, VatMode.Included, 20, 0);
        var contract = new Contract { BuyerName = "Örnek Müşteri", BuyerAddress = "Örnek adres", TotalAmount = sample.GrandTotal, TaxRate = 20, DocumentDate = DateTime.UtcNow, VatMode = VatMode.Included };
        return View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", new ContractPrintVm(contract, template, content, DocumentViews.VisibleArticles(DocumentJson.ParseArticles(template.ArticlesJson), content), none));
    }

    // "alan = değer" satırları. Boş satır atlanır; geçersiz satır varsa null döner.
    private static Dictionary<string, string>? ParseVariables(string? text)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var at = line.IndexOf('=');
            if (at <= 0) return null;
            var key = line[..at].Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"^[\p{L}\p{N}_]{1,60}$")) return null;
            result[key] = line[(at + 1)..].Trim();
        }
        return result;
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
