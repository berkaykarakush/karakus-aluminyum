using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/sozlesmeler")]
public sealed class ContractsController : Controller
{
    private const int MaxDocumentBytes = 1_000_000;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public ContractsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
        => View(await _db.Contracts.AsNoTracking().Include(x => x.Customer).OrderByDescending(x => x.CreatedUtc).Take(500).ToListAsync());

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract);
        var content = new DocumentContent { Closing = template.Closing, Note = template.DefaultNote, Blocks = { new DocumentBlock() }, Payments = { new PaymentLine() } };
        await LoadOptions();
        return View("Editor", new ContractInputVm { DocumentJson = DocumentJson.Serialize(content) });
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken, RequestSizeLimit(MaxDocumentBytes)]
    public async Task<IActionResult> Create(ContractInputVm input)
    {
        var content = await Validate(input);
        if (content is null || !ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        var contract = new Contract { ContractNumber = NewNumber(), SnapshotJson = DocumentJson.Serialize(await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract)) };
        Apply(contract, input, content);
        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Contract", contract.Id.ToString(), $"{contract.ContractNumber}; total={contract.TotalAmount}");
        TempData["Notice"] = $"{contract.ContractNumber} numaralı sözleşme oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = contract.Id });
    }

    // Kabul edilen tekliften sözleşme: müşteri, bloklar, değişkenler, ekler ve tutar aktarılır.
    [HttpPost("tekliften/{quoteId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FromQuote(int quoteId)
    {
        var quote = await _db.Quotes.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == quoteId);
        if (quote is null) return NotFound();
        if (!QuoteRules.CanTransition(quote.Status, QuoteStatus.Converted))
        {
            TempData["Notice"] = "Sözleşme yalnızca kabul edilen tekliften oluşturulabilir.";
            return Redirect($"/yonetim/crm/teklifler/detay/{quoteId}");
        }

        var source = DocumentJson.ParseContent(quote.DocumentJson);
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract);
        var clauses = await _db.TemplateClauses.AsNoTracking().ToListAsync();
        var content = new DocumentContent
        {
            Closing = template.Closing, Note = source.Note, Variables = source.Variables, DesignIds = source.DesignIds,
            Blocks = source.Blocks.Select(block => new DocumentBlock
            {
                Name = block.Name, Variables = block.Variables,
                // Aynı adlı hazır maddenin sözleşme metni kullanılır; yoksa teklifteki metin korunur.
                Clauses = block.Clauses.Select(c => new BlockClause { Title = c.Title, Text = clauses.FirstOrDefault(x => x.Name == c.Title)?.ContractText ?? c.Text }).ToList()
            }).ToList()
        };
        var customer = quote.Customer!;
        var taxInfo = string.Join(" ", new[] { customer.TaxOffice, customer.TaxNumber }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var contract = new Contract
        {
            ContractNumber = NewNumber(), QuoteId = quote.Id, CustomerId = quote.CustomerId, Title = quote.Title, DocumentDate = DateTime.UtcNow.Date,
            BuyerName = customer.Name, BuyerAddress = customer.Address, BuyerTaxInfo = taxInfo.Length == 0 ? null : taxInfo,
            TotalAmount = quote.GrandTotal, VatMode = quote.VatMode == VatMode.Added ? VatMode.Included : quote.VatMode,
            DocumentJson = DocumentJson.Serialize(content), SnapshotJson = DocumentJson.Serialize(template)
        };
        _db.Contracts.Add(contract);
        quote.Status = QuoteStatus.Converted;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create-from-quote", "Contract", contract.Id.ToString(), $"{contract.ContractNumber}; quote={quote.QuoteNumber}");
        TempData["Notice"] = "Sözleşme taslağı oluşturuldu. Ödeme planını ve bilgileri tamamlayın.";
        return RedirectToAction(nameof(Edit), new { id = contract.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status != ContractStatus.Draft) return NotDraft(id);
        await LoadOptions();
        ViewBag.ContractId = id;
        return View("Editor", new ContractInputVm
        {
            CustomerId = contract.CustomerId, Title = contract.Title, DocumentDate = contract.DocumentDate, BuyerName = contract.BuyerName, BuyerAddress = contract.BuyerAddress,
            BuyerTaxInfo = contract.BuyerTaxInfo, TotalAmount = contract.TotalAmount, VatMode = contract.VatMode, PageCount = contract.PageCount, DocumentJson = contract.DocumentJson
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(MaxDocumentBytes)]
    public async Task<IActionResult> Edit(int id, ContractInputVm input)
    {
        var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status != ContractStatus.Draft) return NotDraft(id);

        var content = await Validate(input);
        if (content is null || !ModelState.IsValid) { await LoadOptions(); ViewBag.ContractId = id; return View("Editor", input); }

        Apply(contract, input, content);
        contract.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Contract", id.ToString(), $"{contract.ContractNumber}; total={contract.TotalAmount}");
        TempData["Notice"] = "Sözleşme güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var contract = await _db.Contracts.AsNoTracking().Include(x => x.Customer).Include(x => x.Quote).FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        var content = DocumentJson.ParseContent(contract.DocumentJson);
        ViewBag.PlanErrors = PaymentPlan.Validate(content.Payments, contract.TotalAmount).ToList();
        return View(contract);
    }

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return contract is null ? NotFound() : View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", await BuildPrintVm(contract));
    }

    // Taslak sözleşme, şablon ayarlarındaki güncel madde metinlerini ve firma bilgilerini alır.
    [HttpPost("sablonu-yenile/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshTemplate(int id)
    {
        var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status != ContractStatus.Draft) return NotDraft(id);
        contract.SnapshotJson = DocumentJson.Serialize(await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract));
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Sözleşme, güncel şablon ayarlarını kullanacak şekilde yenilendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // İmzalanan sözleşme değişmez; imzadan önce ödeme planı ve zorunlu alanlar denetlenir.
    [HttpPost("imzala/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(int id)
    {
        var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status != ContractStatus.Draft) return NotDraft(id);

        var content = DocumentJson.ParseContent(contract.DocumentJson);
        var template = await DocumentViews.TemplateForAsync(_db, DocumentKinds.Contract, contract.SnapshotJson);
        var problems = PaymentPlan.Validate(content.Payments, contract.TotalAmount).ToList();
        if (content.Payments.Count == 0) problems.Add("Ödeme planı girilmemiş.");
        if (contract.TotalAmount <= 0) problems.Add("İşin toplam bedeli girilmemiş.");
        if (string.IsNullOrWhiteSpace(template.Signatory)) problems.Add("Şablon ayarlarında imza yetkilisi girilmemiş.");
        if (problems.Count > 0)
        {
            TempData["Notice"] = "İmzalandı olarak işaretlenemedi: " + string.Join(" ", problems);
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = ContractStatus.Signed;
        contract.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("sign", "Contract", id.ToString(), contract.ContractNumber);
        TempData["Notice"] = "Sözleşme imzalandı olarak işaretlendi. Artık değiştirilemez.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("iptal/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status == ContractStatus.Cancelled) return RedirectToAction(nameof(Details), new { id });
        contract.Status = ContractStatus.Cancelled;
        contract.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("cancel", "Contract", id.ToString(), contract.ContractNumber);
        TempData["Notice"] = "Sözleşme iptal edildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        if (contract.Status != ContractStatus.Draft) return NotDraft(id);
        _db.Contracts.Remove(contract);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "Contract", id.ToString(), contract.ContractNumber);
        return RedirectToAction(nameof(Index));
    }

    private async Task<ContractPrintVm> BuildPrintVm(Contract contract)
    {
        var content = DocumentJson.ParseContent(contract.DocumentJson);
        var template = await DocumentViews.TemplateForAsync(_db, DocumentKinds.Contract, contract.SnapshotJson);
        return new ContractPrintVm(contract, template, content, DocumentJson.ParseArticles(template.ArticlesJson), await DocumentViews.LoadAttachmentsAsync(_db, content.DesignIds));
    }

    private static string NewNumber() => $"SZ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private RedirectToActionResult NotDraft(int id)
    {
        TempData["Notice"] = "Yalnızca taslak durumundaki sözleşme değiştirilebilir.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<DocumentContent?> Validate(ContractInputVm input)
    {
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        if (!Enum.IsDefined(input.VatMode)) ModelState.AddModelError(nameof(input.VatMode), "Geçersiz KDV seçeneği.");
        var content = DocumentJson.TryParseContent(input.DocumentJson);
        if (content is null)
        {
            ModelState.AddModelError(nameof(input.DocumentJson), "Sözleşme içeriği okunamadı.");
            return null;
        }
        // Ödeme toplamı taslakta bilgi olarak gösterilir; imzalanırken zorunludur. Tip ve negatif tutar her zaman denetlenir.
        foreach (var problem in PaymentPlan.Validate(content.Payments, content.Payments.Sum(p => p.Amount)))
            ModelState.AddModelError(string.Empty, problem);
        return content;
    }

    private static void Apply(Contract contract, ContractInputVm input, DocumentContent content)
    {
        contract.CustomerId = input.CustomerId;
        contract.Title = input.Title.Trim();
        contract.DocumentDate = DateTime.SpecifyKind(input.DocumentDate.Date, DateTimeKind.Utc);
        contract.BuyerName = input.BuyerName.Trim();
        contract.BuyerAddress = input.BuyerAddress?.Trim();
        contract.BuyerTaxInfo = input.BuyerTaxInfo?.Trim();
        contract.TotalAmount = input.TotalAmount;
        contract.VatMode = input.VatMode;
        contract.PageCount = input.PageCount;
        contract.DocumentJson = DocumentJson.Serialize(content);
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToListAsync();
        ViewBag.CustomerDetails = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive)
            .Select(x => new { id = x.Id, name = x.Name, address = x.Address, taxInfo = ((x.TaxOffice ?? "") + " " + (x.TaxNumber ?? "")).Trim() }).ToListAsync();
        ViewBag.Clauses = await _db.TemplateClauses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync();
        ViewBag.Designs = await _db.WindowDesigns.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).Take(200).Select(x => new { id = x.Id, name = x.Name, widthMm = x.WidthMm, heightMm = x.HeightMm, quantity = x.Quantity }).ToListAsync();
    }
}
