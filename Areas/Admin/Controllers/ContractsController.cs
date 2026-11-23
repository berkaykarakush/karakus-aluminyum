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
        var content = new DocumentContent { Closing = template.Closing, Note = template.DefaultNote, Blocks = { new DocumentBlock() }, Payments = { new PaymentLine() }, Terms = DocumentJson.ParseTerms(template.TermsJson), Variables = DocumentJson.ParseVariables(template.VariablesJson) };
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

    // Kabul edilen tekliften sözleşme: müşteri, bloklar, kalemler (birim fiyat ve iskontolarıyla), şartlar, değişkenler, ekler ve tutar aktarılır.
    [HttpPost("tekliften/{quoteId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FromQuote(int quoteId)
    {
        var quote = await _db.Quotes.Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == quoteId);
        if (quote is null) return NotFound();
        if (!QuoteRules.CanTransition(quote.Status, QuoteStatus.Converted))
        {
            TempData["Notice"] = "Sözleşme yalnızca kabul edilen tekliften oluşturulabilir.";
            return Redirect($"/yonetim/crm/teklifler/detay/{quoteId}");
        }

        var source = DocumentJson.ParseContent(quote.DocumentJson);
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract);
        var clauses = await _db.TemplateClauses.AsNoTracking().ToListAsync();
        // Teklifte olmayan değişkenler (ör. gecikme cezası oranı) sözleşme şablonunun değerini alır.
        var variables = DocumentJson.ParseVariables(template.VariablesJson);
        foreach (var (key, value) in source.Variables) variables[key] = value;
        var content = new DocumentContent
        {
            Closing = template.Closing, Note = source.Note, Variables = variables, DesignIds = source.DesignIds, Terms = source.Terms, Subject = quote.Title,
            Blocks = DocumentViews.BuildBlocks(quote, source).Select(view => new DocumentBlock
            {
                Name = view.Block.Name, Variables = view.Block.Variables,
                // Aynı adlı hazır maddenin sözleşme metni kullanılır; yoksa teklifteki metin korunur.
                Clauses = view.Block.Clauses.Select(c => new BlockClause { Title = c.Title, Text = clauses.FirstOrDefault(x => x.Name == c.Title)?.ContractText ?? c.Text }).ToList(),
                // Maliyet ve marj sözleşmeye geçmez; yalnızca müşteriye verilen fiyat.
                Items = view.Items.Select(i => new BlockItem { Description = i.Description, Quantity = i.Quantity, Unit = i.Unit, UnitPrice = i.UnitPrice, DiscountRate = i.DiscountRate }).ToList()
            }).ToList()
        };
        // Ödeme planı teklifteki avans oranından başlar: avans sözleşme imzasında, kalanı montaj bitiminde. Sözleşmede değiştirilebilir.
        content.Payments = DefaultPayments(quote.GrandTotal, variables.TryGetValue("avans_orani", out var advance) ? advance : null);
        var customer = quote.Customer!;
        var taxInfo = string.Join(" ", new[] { customer.TaxOffice, customer.TaxNumber }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var contract = new Contract
        {
            ContractNumber = NewNumber(), QuoteId = quote.Id, CustomerId = quote.CustomerId, Title = quote.Title, DocumentDate = DateTime.UtcNow.Date,
            BuyerName = customer.Name, BuyerAddress = customer.Address, BuyerTaxInfo = taxInfo.Length == 0 ? null : taxInfo,
            TotalAmount = quote.GrandTotal, VatMode = quote.VatMode, TaxRate = quote.TaxRate, DiscountRate = quote.DiscountRate,
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
            BuyerTaxInfo = contract.BuyerTaxInfo, TotalAmount = contract.TotalAmount, VatMode = contract.VatMode, TaxRate = contract.TaxRate, DiscountRate = contract.DiscountRate, PageCount = contract.PageCount, DocumentJson = contract.DocumentJson
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
        ViewBag.OrderId = await _db.ProductionOrders.Where(x => x.ContractId == id || (contract.QuoteId != null && x.QuoteId == contract.QuoteId)).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        ViewBag.PublicUrl = DocumentLinks.Url(Request, DocumentLinks.Contract, await DocumentLinks.EnsureContractAsync(_db, contract.Id, contract.PublicToken));
        return View(contract);
    }

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        ViewData["Qr"] = DocumentLinks.Qr(Request, DocumentLinks.Contract, await DocumentLinks.EnsureContractAsync(_db, contract.Id, contract.PublicToken));
        return View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", await DocumentViews.ContractPrintAsync(_db, contract, PrintDates.From(Request)));
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

    // QR bağlantısını yeniler: eski QR kodlu çıktılar artık açılmaz.
    [HttpPost("baglantiyi-yenile/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RenewLink(int id)
    {
        if (await _db.Contracts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublicToken, DocumentLinks.NewToken())) == 0) return NotFound();
        await _audit.RecordAsync("renew-link", "Contract", id.ToString(), "QR bağlantısı yenilendi");
        TempData["Notice"] = "QR bağlantısı yenilendi. Önceki çıktılardaki QR kodu artık açılmaz; belgeyi yeniden yazdırın.";
        return RedirectToAction(nameof(Details), new { id });
    }


    private static List<PaymentLine> DefaultPayments(decimal total, string? advanceText)
    {
        if (total <= 0) return new() { new PaymentLine() };
        var rate = decimal.TryParse(advanceText, System.Globalization.NumberStyles.Number, TurkishNumbers.Culture, out var r) && r is > 0 and < 100 ? r : 0m;
        if (rate == 0) return new() { new PaymentLine { Kind = PaymentKinds.Cash, Amount = total } };
        var first = decimal.Round(total * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new()
        {
            new PaymentLine { Kind = PaymentKinds.Cash, Amount = first, Text = "sözleşmenin imzalanmasıyla peşin olarak ödenecektir." },
            new PaymentLine { Kind = PaymentKinds.Cash, Amount = total - first, Text = "montajın tamamlanmasıyla ödenecektir." }
        };
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
        var items = content.Blocks.SelectMany(b => b.Items).Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        if (items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) ModelState.AddModelError(string.Empty, "Kalem miktarı sıfırdan büyük, fiyatı sıfır veya daha büyük olmalıdır.");
        if (items.Any(i => i.DiscountRate is < 0 or > 100)) ModelState.AddModelError(string.Empty, "Satır iskontosu %0 ile %100 arasında olmalıdır.");
        if (items.Any(i => i.Description!.Trim().Length > 200)) ModelState.AddModelError(string.Empty, "Kalem açıklaması en fazla 200 karakter olabilir.");
        if (items.Any(i => (i.Unit?.Trim().Length ?? 0) > 20)) ModelState.AddModelError(string.Empty, "Birim en fazla 20 karakter olabilir.");
        // Sözleşmede maliyet tutulmaz; tarayıcıdan gelse de silinir.
        foreach (var item in content.Blocks.SelectMany(b => b.Items)) { item.UnitCost = 0; item.DesignId = null; item.ManualBaseCost = null; item.ManualGlassCost = null; item.ManualPrice = false; }
        foreach (var block in content.Blocks) block.Items = block.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        DocumentTerms.Validate(content, ModelState);
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
        contract.VatMode = input.VatMode;
        contract.TaxRate = input.TaxRate;
        contract.DiscountRate = input.DiscountRate;
        // Kalemli sözleşmede bedel kalemlerden hesaplanır (genel toplam, KDV dahil); kalemsizde elle yazılır.
        var prices = PriceSheet.FromContent(content, input.VatMode, input.TaxRate, input.DiscountRate);
        contract.TotalAmount = prices.HasLines ? prices.GrandTotal : input.TotalAmount;
        contract.PageCount = input.PageCount;
        contract.DocumentJson = DocumentJson.Serialize(content);
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive).OrderBy(x => x.Name).Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToListAsync();
        ViewBag.CustomerDetails = await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive)
            .Select(x => new { id = x.Id, name = x.Name, address = x.Address, taxInfo = ((x.TaxOffice ?? "") + " " + (x.TaxNumber ?? "")).Trim() }).ToListAsync();
        ViewBag.Clauses = await _db.TemplateClauses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync();
        // Şablon maddelerindeki değişkenler (ör. {{teslim_suresi}}) editörde doldurulabilsin.
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract);
        ViewBag.TemplateTexts = DocumentJson.ParseArticles(template.ArticlesJson).Select(a => a.Body ?? "").Append(template.SignOff ?? "").ToList();
        ViewBag.Designs = await _db.WindowDesigns.AsNoTracking().OrderByDescending(x => x.DrawingList!.UpdatedUtc).ThenBy(x => x.DrawingListId).ThenBy(x => x.SortOrder).Take(200).Select(x => new { id = x.Id, listId = x.DrawingListId, listName = x.DrawingList!.Name, name = x.Name, widthMm = x.WidthMm, heightMm = x.HeightMm, quantity = x.Quantity }).ToListAsync();
    }
}
