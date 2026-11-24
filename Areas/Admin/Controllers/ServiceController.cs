using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Satış sonrası servis: bitmiş işlerde servis talebi, ziyaret (servis formu) ve yazdırılabilir formlar.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/servis")]
public sealed class ServiceController : Controller
{
    private readonly AppDbContext _db;
    private readonly ServiceDesk _desk;
    private readonly IAuditService _audit;
    private readonly KarakusAluminyum.Web.Services.Notifications.CustomerNotifier _notifier;
    public ServiceController(AppDbContext db, ServiceDesk desk, IAuditService audit, KarakusAluminyum.Web.Services.Notifications.CustomerNotifier notifier) { _db = db; _desk = desk; _audit = audit; _notifier = notifier; }

    private string? UserName => User.Identity?.Name;

    [HttpGet("")]
    public async Task<IActionResult> Index(ServiceStatus? durum, string? q, bool tumu = false)
    {
        var query = _db.ServiceRequests.AsNoTracking().Include(x => x.Customer).Include(x => x.ProductionOrder).Include(x => x.Installer).AsQueryable();
        if (durum is ServiceStatus status) query = query.Where(x => x.Status == status);
        else if (!tumu) query = query.Where(x => x.Status == ServiceStatus.Open || x.Status == ServiceStatus.Scheduled);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number, $"%{q}%") || EF.Functions.ILike(x.Customer!.Name, $"%{q}%") || EF.Functions.ILike(x.Complaint, $"%{q}%"));
        ViewBag.Counts = await _db.ServiceRequests.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        (ViewBag.Status, ViewBag.Query, ViewBag.All) = (durum, q, tumu);
        return View(await query.OrderByDescending(x => x.Urgent).ThenBy(x => x.PlannedOn == null).ThenBy(x => x.PlannedOn).ThenByDescending(x => x.Id).Take(500).ToListAsync());
    }

    // siparis: bitmiş işten talep açılır; müşteri, adres, telefon ve garanti durumu siparişten gelir.
    [HttpGet("ekle")]
    public async Task<IActionResult> Create(int? siparis, int? musteri)
    {
        var input = new ServiceRequestInputVm { CustomerId = musteri ?? 0 };
        if (siparis is int orderId && await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == orderId) is { } order)
        {
            input.ProductionOrderId = order.Id;
            input.CustomerId = order.CustomerId;
            input.Address = order.InstallAddress ?? OrderService.Location(order.Customer);
            input.ContactPhone = order.ContactPhone ?? order.Customer?.Phone;
            input.ContactName = order.Customer?.ContactPerson ?? order.Customer?.Name;
            input.Warranty = ServiceDesk.SuggestWarranty(order.DeliveredOn, await _desk.WarrantyYearsAsync(order), input.ReceivedOn);
        }
        else if (musteri is int customerId && await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId) is { } customer)
        {
            input.Address = OrderService.Location(customer);
            input.ContactPhone = customer.Phone;
            input.ContactName = customer.ContactPerson ?? customer.Name;
        }
        await LoadOptions(input.ProductionOrderId);
        return View("Editor", input);
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceRequestInputVm input)
    {
        input.Id = 0;
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(input.ProductionOrderId); return View("Editor", input); }
        var request = new ServiceRequest { Number = await _desk.NextRequestNumberAsync(input.ReceivedOn) };
        Apply(request, input);
        _db.ServiceRequests.Add(request);
        await _notifier.ServiceChangedAsync(request, KarakusAluminyum.Web.Services.Notifications.ServiceSnapshot.None);
        await _desk.RecordOnOrderAsync(request.ProductionOrderId, $"Servis talebi açıldı: {request.Number} ({request.Category}).", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ServiceRequest", request.Id.ToString(), request.Number);
        TempData["Notice"] = $"Servis talebi {request.Number} açıldı.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var r = await _db.ServiceRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();
        await LoadOptions(r.ProductionOrderId);
        return View("Editor", new ServiceRequestInputVm
        {
            Id = id, CustomerId = r.CustomerId, ProductionOrderId = r.ProductionOrderId, ReceivedOn = r.ReceivedOn, Channel = r.Channel, Category = r.Category, Urgent = r.Urgent,
            ContactName = r.ContactName, ContactPhone = r.ContactPhone, Address = r.Address, PozIds = ServiceDesk.ParsePozIds(r.PozIds), Complaint = r.Complaint,
            Warranty = r.Warranty, PlannedOn = r.PlannedOn, PlannedAt = r.PlannedAt, InstallerId = r.InstallerId, Notes = r.Notes
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceRequestInputVm input)
    {
        var request = await _db.ServiceRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (request is null) return NotFound();
        input.Id = id;
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(input.ProductionOrderId); return View("Editor", input); }
        var before = KarakusAluminyum.Web.Services.Notifications.ServiceSnapshot.Of(request);
        Apply(request, input);
        await _notifier.ServiceChangedAsync(request, before);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ServiceRequest", id.ToString(), request.Number);
        TempData["Notice"] = "Servis talebi güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var request = await LoadAsync(id);
        if (request is null) return NotFound();
        ViewBag.Pozlar = (await _desk.OrderPozlarAsync(request.ProductionOrderId)).Where(x => ServiceDesk.ParsePozIds(request.PozIds).Contains(x.Id)).ToList();
        ViewBag.WarrantyEnds = ServiceDesk.WarrantyEnds(request.ProductionOrder?.DeliveredOn, await _desk.WarrantyYearsAsync(request.ProductionOrder));
        ViewBag.Notices = await _db.NotificationJobs.AsNoTracking().Where(x => x.ServiceRequestId == id).OrderByDescending(x => x.Id).Take(50).ToListAsync();
        return View(request);
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, ServiceStatus status)
    {
        var request = await _db.ServiceRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (request is null) return NotFound();
        if (!Enum.IsDefined(status) || status == request.Status) return RedirectToAction(nameof(Details), new { id });
        var before = KarakusAluminyum.Web.Services.Notifications.ServiceSnapshot.Of(request);
        request.Status = status;
        request.CompletedOn = status == ServiceStatus.Completed ? request.CompletedOn ?? KarakusAluminyum.Web.Services.Documents.PrintDates.Today : null;
        request.UpdatedUtc = DateTime.UtcNow;
        await _notifier.ServiceChangedAsync(request, before);
        await _desk.RecordOnOrderAsync(request.ProductionOrderId, $"Servis talebi {request.Number}: {ServiceLabels.Label(status)}.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("status", "ServiceRequest", id.ToString(), $"{request.Number}: {status}");
        TempData["Notice"] = $"Durum: {ServiceLabels.Label(status)}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var request = await _db.ServiceRequests.Include(x => x.Visits).FirstOrDefaultAsync(x => x.Id == id);
        if (request is null) return NotFound();
        if (request.Visits.Count > 0) { TempData["Notice"] = "Servis formu olan talep silinemez; iptal edin."; return RedirectToAction(nameof(Details), new { id }); }
        _db.ServiceRequests.Remove(request);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ServiceRequest", id.ToString(), request.Number);
        TempData["Notice"] = "Servis talebi silindi.";
        return RedirectToAction(nameof(Index));
    }

    // ---- Servis formu (ziyaret) ----

    [HttpGet("ziyaret/ekle/{requestId:int}")]
    public async Task<IActionResult> CreateVisit(int requestId)
    {
        var request = await LoadAsync(requestId);
        if (request is null) return NotFound();
        ViewBag.Request = request;
        await LoadParts();
        return View("VisitEditor", new ServiceVisitInputVm { Date = request.PlannedOn ?? KarakusAluminyum.Web.Services.Documents.PrintDates.Today, Technicians = request.Installer?.Name });
    }

    [HttpPost("ziyaret/ekle/{requestId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVisit(int requestId, ServiceVisitInputVm input)
    {
        var request = await _db.ServiceRequests.FirstOrDefaultAsync(x => x.Id == requestId);
        if (request is null) return NotFound();
        if (request.Status == ServiceStatus.Cancelled) ModelState.AddModelError(string.Empty, "İptal edilen talebe servis formu eklenemez.");
        var parts = input.Parts.Where(x => !string.IsNullOrWhiteSpace(x.Description) || x.ProductId is > 0).ToList();
        var productIds = parts.Where(x => x.ProductId is > 0).Select(x => x.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        if (products.Count != productIds.Count) ModelState.AddModelError(string.Empty, "Parçalardaki stok kartlarından biri bulunamadı.");
        foreach (var (part, i) in parts.Select((p, i) => (p, i + 1)))
            if (part.Quantity <= 0 || part.UnitPrice < 0) ModelState.AddModelError(string.Empty, $"Parça satırı {i}: miktar 0'dan büyük, fiyat negatif olmamalı.");
        if (input.ArrivedAt is TimeOnly a && input.LeftAt is TimeOnly l && l < a) ModelState.AddModelError(nameof(input.LeftAt), "Çıkış saati varış saatinden önce olamaz.");
        if (!Enum.IsDefined(input.Result)) ModelState.AddModelError(nameof(input.Result), "Geçersiz sonuç.");
        if (!ModelState.IsValid) { ViewBag.Request = await LoadAsync(requestId); await LoadParts(); return View("VisitEditor", input); }
        Product? Card(ServicePartInput p) => p.ProductId is int id ? products.GetValueOrDefault(id) : null;

        var visit = new ServiceVisit
        {
            Number = await _desk.NextVisitNumberAsync(input.Date), ServiceRequestId = requestId, Date = input.Date, ArrivedAt = input.ArrivedAt, LeftAt = input.LeftAt,
            Technicians = input.Technicians?.Trim(), Findings = input.Findings?.Trim(), WorkDone = input.WorkDone.Trim(), Result = input.Result,
            LaborCharge = input.LaborCharge, CustomerNote = input.CustomerNote?.Trim(),
            Parts = parts.Select((p, i) => new ServicePart
            {
                SortOrder = i, ProductId = Card(p)?.Id, Description = string.IsNullOrWhiteSpace(p.Description) ? Card(p)!.Name : p.Description.Trim(), Quantity = p.Quantity,
                Unit = string.IsNullOrWhiteSpace(p.Unit) ? Card(p)?.Unit ?? "Adet" : p.Unit.Trim(), UnitPrice = p.UnitPrice
            }).ToList()
        };
        _db.ServiceVisits.Add(visit);
        ServiceDesk.ApplyVisit(request, visit);
        await _desk.RecordOnOrderAsync(request.ProductionOrderId, $"Servis formu {visit.Number} ({request.Number}): {ServiceLabels.Label(visit.Result)}.", UserName);
        await _db.SaveChangesAsync();
        // Stok kartlı parçalar ziyaret tarihiyle stoktan düşer (faturaya stok kartı bağlanmaz, yoksa onayda ikinci kez düşerdi).
        var used = visit.Parts.Where(p => p.ProductId is int id && products[id].Kind == ProductKind.Stock).ToList();
        foreach (var part in used)
            _db.StockMovements.Add(new StockMovement { ProductId = part.ProductId!.Value, Date = visit.Date, Kind = StockMovementKind.ServiceUse, Quantity = -part.Quantity, ServiceVisitId = visit.Id, Note = $"Servis: {visit.Number}" });
        if (used.Count > 0) await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ServiceVisit", visit.Id.ToString(), visit.Number);
        TempData["Notice"] = $"Servis formu {visit.Number} kaydedildi. Talep: {ServiceLabels.Label(request.Status)}.";
        return RedirectToAction(nameof(Details), new { id = requestId });
    }

    [HttpPost("ziyaret/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVisit(int id)
    {
        var visit = await _db.ServiceVisits.Include(x => x.ServiceRequest).ThenInclude(x => x!.Visits).FirstOrDefaultAsync(x => x.Id == id);
        if (visit is null) return NotFound();
        var request = visit.ServiceRequest!;
        if (visit.InvoiceId is int invoiceId && await _db.Invoices.AnyAsync(x => x.Id == invoiceId && x.Status != InvoiceStatus.Cancelled))
        {
            TempData["Notice"] = "Faturası açılmış servis formu silinemez; önce faturayı silin veya iptal edin.";
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }
        // Düşülen parçalar stoğa geri yazılır; geçmiş silinmez.
        var net = (await _db.StockMovements.AsNoTracking().Where(x => x.ServiceVisitId == id).Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity))).Where(x => x.Quantity != 0);
        foreach (var (productId, quantity) in net)
            _db.StockMovements.Add(new StockMovement { ProductId = productId, Date = KarakusAluminyum.Web.Services.Documents.PrintDates.Today, Kind = StockMovementKind.ServiceReturn, Quantity = -quantity, Note = $"Servis formu silindi: {visit.Number}" });
        _db.ServiceVisits.Remove(visit);
        // Talebin durumu kalan son ziyarete göre yeniden belirlenir.
        var last = request.Visits.Where(x => x.Id != id).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).FirstOrDefault();
        if (last is not null) ServiceDesk.ApplyVisit(request, last);
        else if (request.Status == ServiceStatus.Completed) { request.Status = ServiceStatus.Open; request.CompletedOn = null; }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ServiceVisit", id.ToString(), visit.Number);
        TempData["Notice"] = "Servis formu silindi.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    // Ücretli servis: parçalar ve işçilik taslak satış faturası olur. Satırlar stok kartına bağlanmaz (parça ziyarette düşüldü).
    [HttpPost("fatura/{visitId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvoice(int visitId)
    {
        var visit = await _db.ServiceVisits.Include(x => x.Parts).Include(x => x.ServiceRequest).FirstOrDefaultAsync(x => x.Id == visitId);
        if (visit is null) return NotFound();
        var request = visit.ServiceRequest!;
        if (visit.InvoiceId is int existing && await _db.Invoices.AnyAsync(x => x.Id == existing && x.Status != InvoiceStatus.Cancelled))
        {
            TempData["Notice"] = "Bu servis formunun faturası zaten açılmış.";
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }
        var productIds = visit.Parts.Where(x => x.ProductId != null).Select(x => x.ProductId!.Value).ToList();
        var vat = await _db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.VatRate);
        var lines = visit.Parts.Where(p => p.UnitPrice > 0).OrderBy(p => p.SortOrder)
            .Select(p => (Description: p.Description, p.Quantity, p.Unit, p.UnitPrice, Vat: p.ProductId is int id ? vat.GetValueOrDefault(id, 20m) : 20m)).ToList();
        if (visit.LaborCharge > 0) lines.Add(("Servis işçiliği", 1, "Hizmet", visit.LaborCharge, 20m));
        if (lines.Count == 0)
        {
            TempData["Notice"] = "Bu servis formunda ücretli kalem yok.";
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }
        var today = KarakusAluminyum.Web.Services.Documents.PrintDates.Today;
        var invoice = new Invoice
        {
            Kind = InvoiceKind.Sale, Status = InvoiceStatus.Draft, CustomerId = request.CustomerId, Date = today, DueDate = today.AddDays(15), Currency = "TRY", ExchangeRate = 1,
            Notes = $"Servis: {request.Number} / {visit.Number} ({visit.Date:dd.MM.yyyy})."
        };
        invoice.Lines.AddRange(lines.Select((l, i) => new InvoiceLine { SortOrder = i, Description = l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, VatRate = l.Vat }));
        KarakusAluminyum.Web.Services.Accounting.InvoiceRules.Calculate(invoice);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        visit.InvoiceId = invoice.Id;
        await _desk.RecordOnOrderAsync(request.ProductionOrderId, $"Servis formu {visit.Number} için satış faturası taslağı açıldı.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Invoice", invoice.Id.ToString(), $"Servis {visit.Number}");
        TempData["Notice"] = $"Taslak satış faturası açıldı ({lines.Count} kalem, {invoice.GrandTotal:#,0.00} TL). Muhasebede onaylanınca kesinleşir."
            + (request.Warranty == ServiceWarranty.InWarranty ? " Not: talep garanti kapsamında işaretli." : "");
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    private async Task LoadParts() => ViewBag.Products = await _db.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ThenBy(x => x.Name).ToListAsync();

    // ---- Çıktılar ----

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> PrintRequest(int id)
    {
        var request = await LoadAsync(id);
        if (request is null) return NotFound();
        await LoadPrint(request);
        return View("RequestPrint", request);
    }

    [HttpGet("form/{visitId:int}")]
    public async Task<IActionResult> PrintVisit(int visitId)
    {
        var visit = await _db.ServiceVisits.AsNoTracking().Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == visitId);
        if (visit is null) return NotFound();
        var request = await LoadAsync(visit.ServiceRequestId);
        await LoadPrint(request!);
        ViewBag.Request = request;
        return View("VisitPrint", visit);
    }

    // Boş servis formu: talepsiz, sahada elle doldurmak için.
    [HttpGet("bos-form")]
    public async Task<IActionResult> BlankForm()
    {
        ViewBag.Template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Contract);
        return View("BlankPrint");
    }

    private async Task LoadPrint(ServiceRequest request)
    {
        ViewBag.Template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Contract);
        ViewBag.Pozlar = (await _desk.OrderPozlarAsync(request.ProductionOrderId)).Where(x => ServiceDesk.ParsePozIds(request.PozIds).Contains(x.Id)).ToList();
        ViewBag.WarrantyEnds = ServiceDesk.WarrantyEnds(request.ProductionOrder?.DeliveredOn, await _desk.WarrantyYearsAsync(request.ProductionOrder));
    }

    private Task<ServiceRequest?> LoadAsync(int id) => _db.ServiceRequests.AsNoTracking()
        .Include(x => x.Customer).Include(x => x.Installer).Include(x => x.ProductionOrder).ThenInclude(x => x!.DrawingList)
        .Include(x => x.Visits).ThenInclude(x => x.Parts).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);

    private async Task Validate(ServiceRequestInputVm input)
    {
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId && x.PartyType != PartyType.Supplier)) ModelState.AddModelError(nameof(input.CustomerId), "Müşteri seçin.");
        if (input.ProductionOrderId is int orderId && !await _db.ProductionOrders.AnyAsync(x => x.Id == orderId && x.CustomerId == input.CustomerId))
            ModelState.AddModelError(nameof(input.ProductionOrderId), "Seçilen sipariş bu müşteriye ait değil.");
        if (input.InstallerId is int installer && !await _db.Installers.AnyAsync(x => x.Id == installer)) ModelState.AddModelError(nameof(input.InstallerId), "Ekip bulunamadı.");
        if (!ServiceLabels.Channels.Contains(input.Channel)) input.Channel = ServiceLabels.Channels[0];
        if (!ServiceLabels.Categories.Contains(input.Category)) input.Category = "Diğer";
        if (!Enum.IsDefined(input.Warranty)) ModelState.AddModelError(nameof(input.Warranty), "Geçersiz garanti durumu.");
        if (input.PlannedOn is DateOnly planned && planned < input.ReceivedOn) ModelState.AddModelError(nameof(input.PlannedOn), "Ziyaret tarihi talep tarihinden önce olamaz.");
        var valid = (await _desk.OrderPozlarAsync(input.ProductionOrderId)).Select(x => x.Id).ToHashSet();
        input.PozIds = input.PozIds.Where(valid.Contains).Distinct().ToList();
    }

    private static void Apply(ServiceRequest request, ServiceRequestInputVm input)
    {
        request.CustomerId = input.CustomerId;
        request.ProductionOrderId = input.ProductionOrderId;
        request.ReceivedOn = input.ReceivedOn;
        request.Channel = input.Channel;
        request.Category = input.Category;
        request.Urgent = input.Urgent;
        request.ContactName = input.ContactName?.Trim();
        request.ContactPhone = input.ContactPhone?.Trim();
        request.Address = input.Address?.Trim();
        request.PozIds = input.PozIds.Count == 0 ? null : string.Join(",", input.PozIds);
        request.Complaint = input.Complaint.Trim();
        request.Warranty = input.Warranty;
        request.PlannedOn = input.PlannedOn;
        request.PlannedAt = input.PlannedOn is null ? null : input.PlannedAt;
        request.InstallerId = input.InstallerId;
        request.Notes = input.Notes?.Trim();
        // Ziyaret tarihi verilen açık talep "planlandı" olur; tarih kaldırılırsa tekrar açılır.
        if (request.Status is ServiceStatus.Open or ServiceStatus.Scheduled) request.Status = input.PlannedOn is null ? ServiceStatus.Open : ServiceStatus.Scheduled;
        request.UpdatedUtc = DateTime.UtcNow;
    }

    private async Task LoadOptions(int? orderId)
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.PartyType != PartyType.Supplier).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        var orders = await _db.ProductionOrders.AsNoTracking().OrderByDescending(x => x.Status == OrderStatus.Delivered).ThenByDescending(x => x.Id).Take(500)
            .Select(x => new { x.Id, x.CustomerId, x.OrderNumber, Customer = x.Customer!.Name, x.Title, x.DeliveredOn }).ToListAsync();
        ViewBag.Orders = orders.Select(x => new OrderOption(x.Id, x.CustomerId, $"{x.OrderNumber} · {x.Customer} · {x.Title}" + (x.DeliveredOn is DateOnly d ? $" (teslim {d:dd.MM.yyyy})" : ""))).ToList();
        ViewBag.Installers = new SelectList(await _db.Installers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.OrderPozlar = await _desk.OrderPozlarAsync(orderId);
    }
}
