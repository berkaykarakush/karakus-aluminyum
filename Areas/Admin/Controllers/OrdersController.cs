using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
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

// Üretim siparişleri (Ercom sipariş formu): durum akışı, sipariş işaretleri, cam ve montaj takibi, irsaliye ve çıktılar.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/siparisler")]
public sealed class OrdersController : Controller
{
    public const string CapacityKey = "ProductionWeeklyCapacityM2";
    public const string UnitCapacityKey = "ProductionWeeklyCapacityUnits";
    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    private readonly IAuditService _audit;
    private readonly MaterialPlanning _planning;
    public OrdersController(AppDbContext db, OrderService orders, IAuditService audit, MaterialPlanning planning) { _db = db; _orders = orders; _audit = audit; _planning = planning; }

    private string? UserName => User.Identity?.Name;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [HttpGet("")]
    public async Task<IActionResult> Index(OrderStatus? durum, string? q, bool geciken = false)
    {
        var query = _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.Installer).AsQueryable();
        if (durum is OrderStatus status) query = query.Where(x => x.Status == status);
        else query = query.Where(x => x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.Delivered); // varsayılan: açık siparişler
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.OrderNumber, $"%{q}%") || EF.Functions.ILike(x.Title, $"%{q}%") || EF.Functions.ILike(x.Customer!.Name, $"%{q}%"));
        var rows = await query.OrderBy(x => x.PlannedShipDate == null).ThenBy(x => x.PlannedShipDate).ThenByDescending(x => x.Id).Take(500).ToListAsync();
        if (geciken) rows = rows.Where(x => OrderService.IsLate(x, Today)).ToList();
        ViewBag.Counts = await _db.ProductionOrders.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        ViewBag.Loads = await _orders.LoadsAsync(rows.Select(x => x.DrawingListId));
        (ViewBag.Status, ViewBag.Query, ViewBag.Late) = (durum, q, geciken);
        return View(rows);
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create()
    {
        await LoadOptions();
        return View("Editor", new OrderInputVm());
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderInputVm input)
    {
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }
        var order = new ProductionOrder();
        Apply(order, input);
        await _orders.CreateAsync(order, "Sipariş elle açıldı.", UserName);
        await _audit.RecordAsync("create", "ProductionOrder", order.Id.ToString(), order.OrderNumber);
        TempData["Notice"] = $"{order.OrderNumber} numaralı sipariş oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpPost("tekliften/{quoteId:int}"), ValidateAntiForgeryToken]
    public Task<IActionResult> FromQuote(int quoteId) => FromSource(() => _orders.FromQuoteAsync(quoteId, UserName), $"/yonetim/crm/teklifler/detay/{quoteId}");

    [HttpPost("sozlesmeden/{contractId:int}"), ValidateAntiForgeryToken]
    public Task<IActionResult> FromContract(int contractId) => FromSource(() => _orders.FromContractAsync(contractId, UserName), $"/yonetim/sozlesmeler/detay/{contractId}");

    private async Task<IActionResult> FromSource(Func<Task<ProductionOrder>> create, string back)
    {
        try
        {
            var order = await create();
            await _audit.RecordAsync("create", "ProductionOrder", order.Id.ToString(), order.OrderNumber);
            TempData["Notice"] = $"{order.OrderNumber} numaralı sipariş hazır. Sevk tarihini ve montaj bilgilerini girin.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        catch (OrderException error)
        {
            TempData["Notice"] = error.Message;
            return Redirect(back);
        }
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.GlassSupplier).Include(x => x.Installer)
            .Include(x => x.Quote).Include(x => x.Contract).Include(x => x.DrawingList)
            .Include(x => x.Events).Include(x => x.DeliveryNotes).ThenInclude(x => x.Lines).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        ViewBag.Progress = await _orders.ProgressAsync(order);
        ViewBag.Scans = await ProductionController.ProgressAsync(_db, id);
        var workLogs = await _db.WorkLogs.AsNoTracking().Include(x => x.Employee).Include(x => x.Installer).Include(x => x.ProductionOrder).Where(x => x.ProductionOrderId == id).ToListAsync();
        var workRows = WorkStats.ByProject(workLogs, KarakusAluminyum.Web.Services.Documents.PrintDates.Today);
        if (User.IsInRole("Admin") || User.IsInRole("Accountant")) workRows = WorkStats.WithCost(workRows, workLogs, KarakusAluminyum.Web.Services.Documents.PrintDates.Today); // maliyet maaştan gelir
        ViewBag.Work = workRows.FirstOrDefault();
        ViewBag.Services = await _db.ServiceRequests.AsNoTracking().Where(x => x.ProductionOrderId == id).OrderByDescending(x => x.Id).ToListAsync();
        ViewBag.Punch = await _db.PunchItems.AsNoTracking().Include(x => x.WindowDesign).Where(x => x.ProductionOrderId == id).OrderBy(x => x.Status == PunchStatus.Resolved || x.Status == PunchStatus.Cancelled).ThenBy(x => x.ReportedOn).ThenBy(x => x.Id).ToListAsync();
        ViewBag.PunchPositions = await PunchController.PositionsAsync(_db, new[] { order.DrawingListId });
        ViewBag.Load = (await _orders.LoadsAsync(new[] { order.DrawingListId })).GetValueOrDefault(order.DrawingListId ?? 0, OrderLoad.Empty);
        ViewBag.SystemIds = order.DrawingListId is int listId ? await _db.WindowDesigns.Where(x => x.DrawingListId == listId && x.ProfileSystemId != null).Select(x => x.Id).ToListAsync() : new List<int>();
        await LoadOptions();
        return View(order);
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        await LoadOptions();
        return View("Editor", new OrderInputVm
        {
            Id = id, CustomerId = order.CustomerId, Title = order.Title, DrawingListId = order.DrawingListId, OrderDate = order.OrderDate, PlannedShipDate = order.PlannedShipDate,
            InstallAddress = order.InstallAddress, ContactPhone = order.ContactPhone, GlassExcluded = order.GlassExcluded, InstallationExcluded = order.InstallationExcluded, Reserved = order.Reserved, Notes = order.Notes
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OrderInputVm input)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        input.Id = id;
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }
        var oldShip = order.PlannedShipDate;
        Apply(order, input);
        OrderService.Record(order, oldShip != order.PlannedShipDate ? $"Sipariş bilgileri güncellendi. Sevk tarihi: {Date(oldShip)} → {Date(order.PlannedShipDate)}." : "Sipariş bilgileri güncellendi.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ProductionOrder", id.ToString(), order.OrderNumber);
        TempData["Notice"] = "Sipariş güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, OrderStatus status, string? note)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).Include(x => x.DeliveryNotes).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        try
        {
            _orders.ChangeStatus(order, status, note, UserName);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("status", "ProductionOrder", id.ToString(), $"{order.OrderNumber}: {status}");
            TempData["Notice"] = $"Sipariş durumu: {OrderStatuses.Label(status)}.";
        }
        catch (OrderException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    // Ercom'daki sipariş işaretleri: değişen her işaret geçmişe yazılır.
    [HttpPost("isaretler/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFlags(int id, bool sentToCutting, bool glassExcluded, bool installationExcluded, bool reserved)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var changes = new List<string>();
        void Set(bool current, bool next, string label, Action<bool> apply)
        {
            if (current == next) return;
            apply(next);
            changes.Add($"{label}: {(next ? "evet" : "hayır")}");
        }
        Set(order.SentToCutting, sentToCutting, "Kesime gönderildi", v => order.SentToCutting = v);
        Set(order.GlassExcluded, glassExcluded, "Cam hariç", v => order.GlassExcluded = v);
        Set(order.InstallationExcluded, installationExcluded, "Montaj hariç", v => order.InstallationExcluded = v);
        Set(order.Reserved, reserved, "Rezerve", v => order.Reserved = v);
        if (changes.Count > 0)
        {
            OrderService.Record(order, string.Join(", ", changes) + ".", UserName);
            await _db.SaveChangesAsync();
        }
        TempData["Notice"] = changes.Count > 0 ? "İşaretler kaydedildi." : "İşaretler değişmedi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Kesime gönder: işaret konur ve pozların birlikte kesim planı (optimizasyon) açılır.
    [HttpPost("kesime-gonder/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToCutting(int id)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var ids = order.DrawingListId is int listId ? await _db.WindowDesigns.Where(x => x.DrawingListId == listId && x.ProfileSystemId != null).Select(x => x.Id).ToListAsync() : new List<int>();
        if (ids.Count == 0)
        {
            TempData["Notice"] = "Kesim planı için siparişe profil sistemi seçilmiş pozları olan bir çizim listesi bağlayın.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!order.SentToCutting)
        {
            order.SentToCutting = true;
            OrderService.Record(order, $"Kesime gönderildi ({ids.Count} poz).", UserName);
            await _db.SaveChangesAsync();
        }
        return Redirect("/yonetim/cizim/optimizasyon?" + string.Join("&", ids.Select(x => $"ids={x}")));
    }

    [HttpPost("cam/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetGlass(int id, int? glassSupplierId, DateOnly? glassOrderedOn, DateOnly? glassExpectedOn, DateOnly? glassReceivedOn)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (glassSupplierId is int supplier && !await _db.Customers.AnyAsync(x => x.Id == supplier && x.PartyType != PartyType.Customer))
        {
            TempData["Notice"] = "Camcı olarak tedarikçi carilerden birini seçin.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var changed = order.GlassSupplierId != glassSupplierId || order.GlassOrderedOn != glassOrderedOn || order.GlassExpectedOn != glassExpectedOn || order.GlassReceivedOn != glassReceivedOn;
        (order.GlassSupplierId, order.GlassOrderedOn, order.GlassExpectedOn, order.GlassReceivedOn) = (glassSupplierId, glassOrderedOn, glassExpectedOn, glassReceivedOn);
        if (changed)
        {
            var supplierName = glassSupplierId is int s ? await _db.Customers.Where(x => x.Id == s).Select(x => x.Name).FirstAsync() : "-";
            OrderService.Record(order, $"Cam: camcı {supplierName}, sipariş {Date(glassOrderedOn)}, beklenen {Date(glassExpectedOn)}, teslim alındı {Date(glassReceivedOn)}.", UserName);
            await _db.SaveChangesAsync();
        }
        TempData["Notice"] = "Cam bilgileri kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("montaj/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetInstallation(int id, int? installerId, DateOnly? installationDate)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (installerId is int installer && !await _db.Installers.AnyAsync(x => x.Id == installer))
        {
            TempData["Notice"] = "Geçerli bir montaj ekibi seçin.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (order.InstallerId != installerId || order.InstallationDate != installationDate)
        {
            (order.InstallerId, order.InstallationDate) = (installerId, installationDate);
            var name = installerId is int i ? await _db.Installers.Where(x => x.Id == i).Select(x => x.Name).FirstAsync() : "-";
            OrderService.Record(order, $"Montaj: ekip {name}, tarih {Date(installationDate)}.", UserName);
            await _db.SaveChangesAsync();
        }
        TempData["Notice"] = "Montaj bilgileri kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("not/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(int id, string? text)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (string.IsNullOrWhiteSpace(text)) { TempData["Notice"] = "Not yazın."; return RedirectToAction(nameof(Details), new { id }); }
        OrderService.Record(order, text.Trim(), UserName);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // Yalnızca plan aşamasındaki veya iptal edilen, irsaliyesi olmayan sipariş silinir.
    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _db.ProductionOrders.Include(x => x.DeliveryNotes).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (order.Status is not (OrderStatus.Plan or OrderStatus.Cancelled) || order.DeliveryNotes.Count > 0)
        {
            TempData["Notice"] = "Yalnızca plan aşamasındaki veya iptal edilen, irsaliyesi olmayan sipariş silinebilir.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.ProductionOrders.Remove(order);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ProductionOrder", id.ToString(), order.OrderNumber);
        TempData["Notice"] = "Sipariş silindi.";
        return RedirectToAction(nameof(Index));
    }

    // ---- İrsaliye ----

    [HttpGet("irsaliye/ekle/{orderId:int}")]
    public async Task<IActionResult> CreateDelivery(int orderId)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == orderId);
        if (order is null) return NotFound();
        var progress = await _orders.ProgressAsync(order);
        ViewBag.Order = order;
        ViewBag.Progress = progress;
        return View("DeliveryEditor", new DeliveryNoteInputVm
        {
            Address = order.InstallAddress,
            // Varsayılan: kalan adetlerin tamamı. Kısmi sevkte adet azaltılır.
            Lines = progress.Where(p => p.Remaining > 0).Select(p => new DeliveryLineInput { WindowDesignId = p.Design.Id, Description = Line(p.Design), Quantity = p.Remaining, Unit = "Adet" }).ToList()
        });
    }

    [HttpPost("irsaliye/ekle/{orderId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDelivery(int orderId, DeliveryNoteInputVm input)
    {
        var order = await _db.ProductionOrders.Include(x => x.Customer).Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == orderId);
        if (order is null) return NotFound();
        var progress = (await _orders.ProgressAsync(order)).ToDictionary(p => p.Design.Id);
        var lines = input.Lines.Where(l => l.Quantity > 0 && (l.WindowDesignId is not null || !string.IsNullOrWhiteSpace(l.Description))).ToList();
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "İrsaliyede en az bir satır olmalıdır.");
        foreach (var line in lines)
        {
            if (line.WindowDesignId is int designId)
            {
                if (!progress.TryGetValue(designId, out var p)) { ModelState.AddModelError(string.Empty, "Satırdaki poz bu siparişe ait değil."); continue; }
                if (line.Quantity > p.Remaining) ModelState.AddModelError(string.Empty, $"{p.Design.Name}: en fazla {p.Remaining:0.###} adet sevk edilebilir.");
                line.Description = Line(p.Design);
            }
            if ((line.Description?.Trim().Length ?? 0) > 200) ModelState.AddModelError(string.Empty, "Satır açıklaması en fazla 200 karakter olabilir.");
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Order = order;
            ViewBag.Progress = progress.Values.ToList();
            return View("DeliveryEditor", input);
        }

        var note = new DeliveryNote
        {
            ProductionOrderId = order.Id, Number = await _orders.NextNumberAsync("IRS", input.Date.Year), Date = input.Date, Address = input.Address?.Trim(),
            Vehicle = input.Vehicle?.Trim(), Driver = input.Driver?.Trim(), Notes = input.Notes?.Trim(),
            Lines = lines.Select((l, i) => new DeliveryNoteLine { SortOrder = i, WindowDesignId = l.WindowDesignId, Description = l.Description!.Trim(), Quantity = l.Quantity, Unit = string.IsNullOrWhiteSpace(l.Unit) ? "Adet" : l.Unit.Trim() }).ToList()
        };
        _db.DeliveryNotes.Add(note);
        OrderService.Record(order, $"{note.Number} numaralı irsaliye kesildi ({note.Lines.Sum(l => l.Quantity):0.###} adet).", UserName);
        // Pozların tamamı sevk edildiyse sipariş "Sevk edildi" olur.
        var allShipped = progress.Count > 0 && progress.Values.All(p => p.Remaining - lines.Where(l => l.WindowDesignId == p.Design.Id).Sum(l => l.Quantity) <= 0);
        if (allShipped && order.Status is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled))
        {
            order.DeliveryNotes.Add(note);
            _orders.ChangeStatus(order, OrderStatus.Shipped, "Tüm pozlar sevk edildi.", UserName);
        }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "DeliveryNote", note.Id.ToString(), note.Number);
        TempData["Notice"] = $"{note.Number} numaralı irsaliye kesildi.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpGet("irsaliye/{id:int}")]
    public async Task<IActionResult> DeliveryPrint(int id)
    {
        var note = await _db.DeliveryNotes.AsNoTracking().Include(x => x.Lines).Include(x => x.ProductionOrder).ThenInclude(x => x!.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (note is null) return NotFound();
        ViewBag.Template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Quote);
        return View(note);
    }

    [HttpPost("irsaliye/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDelivery(int id)
    {
        var note = await _db.DeliveryNotes.Include(x => x.ProductionOrder).ThenInclude(x => x!.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (note is null) return NotFound();
        var order = note.ProductionOrder!;
        if (order.Status == OrderStatus.Delivered)
        {
            TempData["Notice"] = "Teslim edilen siparişin irsaliyesi silinemez.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
        _db.DeliveryNotes.Remove(note);
        OrderService.Record(order, $"{note.Number} numaralı irsaliye silindi.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "DeliveryNote", id.ToString(), note.Number);
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    // ---- Metraj ve stok ----

    // Seri kataloğu kurallarıyla siparişin malzeme ihtiyacı, mevcut stok ve eksikler.
    [HttpGet("metraj/{id:int}")]
    public async Task<IActionResult> Materials(int id)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.DrawingList).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        await LoadTakeoff(order);
        return View(order);
    }

    // Malzeme ihtiyaç listesi: tedarikçiye verilecek sipariş formu (yalnızca eksikler veya tümü).
    [HttpGet("malzeme-listesi/{id:int}")]
    public async Task<IActionResult> MaterialPrint(int id, bool tumu = false, int? tedarikci = null)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        await LoadTakeoff(order);
        ViewBag.All = tumu;
        ViewBag.Supplier = tedarikci is int supplierId ? await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == supplierId && x.PartyType != PartyType.Customer) : null;
        ViewBag.Template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Contract);
        return View(order);
    }

    [HttpPost("stok-dus/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeductStock(int id)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (order.StockDeducted) { TempData["Notice"] = "Bu siparişin malzemesi stoktan zaten düşüldü."; return RedirectToAction(nameof(Materials), new { id }); }
        var takeoff = await TakeoffAsync(order);
        var lines = takeoff.Lines.Where(x => x.Product.Kind == ProductKind.Stock).ToList();
        if (lines.Count == 0) { TempData["Notice"] = "Stoktan düşülecek malzeme yok. Profil sistemlerine malzeme kuralı tanımlayın."; return RedirectToAction(nameof(Materials), new { id }); }
        foreach (var line in lines)
            _db.StockMovements.Add(new StockMovement { ProductId = line.Product.Id, Date = Today, Kind = StockMovementKind.Production, Quantity = -line.Quantity, ProductionOrderId = id, Note = "Üretim: " + order.OrderNumber });
        order.StockDeducted = true;
        OrderService.Record(order, $"Metraj stoktan düşüldü ({lines.Count} kalem).", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("stock", "ProductionOrder", id.ToString(), $"{order.OrderNumber}: {lines.Count} kalem düşüldü");
        TempData["Notice"] = $"{lines.Count} kalem malzeme stoktan düşüldü.";
        return RedirectToAction(nameof(Materials), new { id });
    }

    // Geri alma, düşülen hareketlerin tersini yazar; geçmiş silinmez (fatura iptalindeki gibi).
    [HttpPost("stok-geri-al/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RevertStock(int id)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var net = (await _db.StockMovements.AsNoTracking().Where(x => x.ProductionOrderId == id).Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity))).Where(x => x.Quantity != 0).ToList();
        foreach (var (productId, quantity) in net)
            _db.StockMovements.Add(new StockMovement { ProductId = productId, Date = Today, Kind = StockMovementKind.ProductionReturn, Quantity = -quantity, ProductionOrderId = id, Note = "Üretim geri alındı: " + order.OrderNumber });
        if (order.StockDeducted || net.Count > 0)
        {
            order.StockDeducted = false;
            OrderService.Record(order, $"Stoktan düşülen malzeme geri alındı ({net.Count} kalem).", UserName);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("stock", "ProductionOrder", id.ToString(), $"{order.OrderNumber}: {net.Count} kalem geri alındı");
        }
        TempData["Notice"] = net.Count > 0 ? $"{net.Count} kalem stoğa geri alındı." : "Geri alınacak stok hareketi yok.";
        return RedirectToAction(nameof(Materials), new { id });
    }

    // Malzeme siparişi: eksikler (veya tümü) seçilen tedarikçiye taslak alış faturası olarak açılır.
    // Fatura onaylandığında stok muhasebedeki gibi artar. Tek yabancı para birimli listede fatura o para biriminde açılır.
    [HttpPost("alis-taslagi/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> PurchaseDraft(int id, int? supplierId, bool tumu = false)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        if (supplierId is not int supplier || !await _db.Customers.AnyAsync(x => x.Id == supplier && x.PartyType != PartyType.Customer))
        {
            TempData["Notice"] = "Alış faturası için tedarikçi cari seçin.";
            return RedirectToAction(nameof(Materials), new { id });
        }
        var takeoff = await TakeoffAsync(order);
        var ids = takeoff.Lines.Select(x => x.Product.Id).ToList();
        var stock = (await _db.StockMovements.AsNoTracking().Where(x => ids.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var lines = takeoff.Lines.Where(x => x.Product.Kind == ProductKind.Stock)
            .Select(x => (x.Product, Quantity: tumu || order.StockDeducted ? x.Quantity : Math.Max(0, x.Quantity - stock.GetValueOrDefault(x.Product.Id))))
            .Select(x => (x.Product, Quantity: MaterialTakeoff.IsCountUnit(x.Product.Unit) ? Math.Ceiling(x.Quantity) : x.Quantity))
            .Where(x => x.Quantity > 0).ToList();
        if (lines.Count == 0) { TempData["Notice"] = "Sipariş verilecek eksik malzeme yok."; return RedirectToAction(nameof(Materials), new { id }); }

        var (invoice, missingRate) = await _planning.PurchaseDraftAsync(supplier, lines, $"{order.OrderNumber} siparişinin malzeme ihtiyacı (metrajdan).");
        OrderService.Record(order, $"Malzeme için taslak alış faturası açıldı ({lines.Count} kalem).", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Invoice", invoice.Id.ToString(), $"{order.OrderNumber} malzeme taslağı");
        TempData["Notice"] = $"{lines.Count} kalemlik taslak alış faturası açıldı (Muhasebe › Faturalar, taslak no {invoice.Id}). Tedarikçinin fatura numarasını girip onaylayınca stok artar."
            + (missingRate.Count > 0 ? $" {string.Join(", ", missingRate)} kuru girilmediği için bu kalemlerin fiyatı 0 yazıldı." : "");
        return RedirectToAction(nameof(Materials), new { id });
    }

    private Task<TakeoffResult> TakeoffAsync(ProductionOrder order) => _planning.TakeoffAsync(order);

    private async Task LoadTakeoff(ProductionOrder order)
    {
        var takeoff = await TakeoffAsync(order);
        var ids = takeoff.Lines.Select(x => x.Product.Id).ToList();
        var stock = (await _db.StockMovements.AsNoTracking().Where(x => ids.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var deducted = (await _db.StockMovements.AsNoTracking().Where(x => x.ProductionOrderId == order.Id).Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => -g.Sum(x => x.Quantity));
        ViewBag.Takeoff = takeoff;
        ViewBag.Rates = await PriceLists.LatestRatesAsync(_db);
        ViewBag.Stock = stock;
        ViewBag.Deducted = deducted;
        ViewBag.Suppliers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.PartyType != PartyType.Customer).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
    }

    // ---- Çıktılar ----

    // İş emri: atölyeye giden poz listesi (çizimler), sipariş işaretleri ve tarihler.
    [HttpGet("is-emri/{id:int}")]
    public Task<IActionResult> WorkOrder(int id) => PrintWithPozlar(id, "WorkOrderPrint");

    // Teslim tutanağı: teslim edilen pozlar, açık ayıp bildirim süresi ve imzalar.
    [HttpGet("teslim-tutanagi/{id:int}")]
    public Task<IActionResult> Handover(int id) => PrintWithPozlar(id, "HandoverPrint");

    private async Task<IActionResult> PrintWithPozlar(int id, string view)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.Installer).Include(x => x.GlassSupplier).Include(x => x.DrawingList).Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var designs = order.DrawingListId is int listId
            ? await _db.WindowDesigns.AsNoTracking().Include(x => x.Brand).Include(x => x.ProfileSystem).Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
            : new List<KarakusAluminyum.Web.Models.Drawing.WindowDesign>();
        ViewBag.Pozlar = KarakusAluminyum.Web.Services.Drawing.PozSheet.Build(designs);
        ViewBag.Template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Contract);
        ViewBag.InspectionDays = await InspectionDaysAsync(order);
        ViewBag.OpenPunch = await _db.PunchItems.AsNoTracking().Include(x => x.WindowDesign).Where(x => x.ProductionOrderId == id && (x.Status == PunchStatus.Open || x.Status == PunchStatus.Waiting)).OrderBy(x => x.ReportedOn).ThenBy(x => x.Id).ToListAsync();
        ViewBag.PunchPositions = await PunchController.PositionsAsync(_db, new[] { order.DrawingListId });
        return View(view, order);
    }

    // Açık ayıp bildirim süresi sözleşmeden (değişken), yoksa şablondan gelir.
    private async Task<string> InspectionDaysAsync(ProductionOrder order)
    {
        if (order.ContractId is int contractId && await _db.Contracts.Where(x => x.Id == contractId).Select(x => x.DocumentJson).FirstOrDefaultAsync() is string json
            && KarakusAluminyum.Web.Services.Documents.DocumentJson.ParseContent(json).Variables.TryGetValue("muayene_suresi", out var days) && !string.IsNullOrWhiteSpace(days)) return days;
        var template = await KarakusAluminyum.Web.Services.Documents.DocumentViews.CurrentTemplateAsync(_db, KarakusAluminyum.Web.Models.Documents.DocumentKinds.Contract);
        return KarakusAluminyum.Web.Services.Documents.DocumentJson.ParseVariables(template.VariablesJson).GetValueOrDefault("muayene_suresi", "7");
    }

    // ---- Üretim planı ve kapasite ----

    [HttpGet("planlama")]
    public async Task<IActionResult> Planning()
    {
        var open = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.Installer)
            .Where(x => x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.Delivered).ToListAsync();
        ViewBag.Loads = await _orders.LoadsAsync(open.Select(x => x.DrawingListId));
        ViewBag.Capacity = await CapacityAsync();
        ViewBag.UnitCapacity = await CapacityAsync(UnitCapacityKey);
        var ids = open.Select(x => x.Id).ToList();
        ViewBag.Scans = (await _db.ProductionScans.AsNoTracking().Where(x => ids.Contains(x.ProductionOrderId)).GroupBy(x => new { x.ProductionOrderId, x.Station }).Select(g => new { g.Key.ProductionOrderId, g.Key.Station, Count = g.Count() }).ToListAsync())
            .GroupBy(x => x.ProductionOrderId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Station, x => x.Count));
        return View(open);
    }

    [HttpPost("planlama/kapasite"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCapacity(decimal capacity, decimal? unitCapacity)
    {
        if (capacity is < 0 or > 100000 || unitCapacity is < 0 or > 100000) { TempData["Notice"] = "Haftalık kapasite 0-100000 arasında olmalıdır."; return RedirectToAction(nameof(Planning)); }
        async Task Save(string key, string description, decimal value)
        {
            var row = await _db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == key);
            if (row is null) _db.SiteSettingRecords.Add(row = new SiteSettingRecord { Key = key, Description = description });
            row.Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        await Save(CapacityKey, "Haftalık üretim kapasitesi (m²)", capacity);
        await Save(UnitCapacityKey, "Haftalık üretim kapasitesi (adet)", unitCapacity ?? 0);
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Haftalık kapasite kaydedildi.";
        return RedirectToAction(nameof(Planning));
    }

    private async Task<decimal> CapacityAsync(string key = CapacityKey)
    {
        var value = await _db.SiteSettingRecords.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync();
        return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var capacity) ? capacity : 0;
    }

    // ---- Montajcılar ----

    [HttpGet("montajcilar")]
    public async Task<IActionResult> Installers()
    {
        ViewBag.Open = await _db.ProductionOrders.Where(x => x.InstallerId != null && x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.Delivered)
            .GroupBy(x => x.InstallerId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return View(await _db.Installers.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync());
    }

    [HttpGet("montajcilar/ekle")]
    public IActionResult CreateInstaller() => View("InstallerEditor", new Installer());

    [HttpPost("montajcilar/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInstaller(Installer input)
    {
        input.Id = 0;
        if (!ModelState.IsValid) return View("InstallerEditor", input);
        _db.Installers.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Installer", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Installers));
    }

    [HttpGet("montajcilar/duzenle/{id:int}")]
    public async Task<IActionResult> EditInstaller(int id)
    {
        var row = await _db.Installers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View("InstallerEditor", row);
    }

    [HttpPost("montajcilar/duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditInstaller(int id, Installer input)
    {
        if (!await _db.Installers.AnyAsync(x => x.Id == id)) return NotFound();
        input.Id = id;
        if (!ModelState.IsValid) return View("InstallerEditor", input);
        _db.Installers.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "Installer", id.ToString(), input.Name);
        return RedirectToAction(nameof(Installers));
    }

    // ---- Yardımcılar ----

    private async Task Validate(OrderInputVm input)
    {
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        if (input.DrawingListId is int listId && !await _db.DrawingLists.AnyAsync(x => x.Id == listId)) ModelState.AddModelError(nameof(input.DrawingListId), "Geçerli bir çizim listesi seçin.");
        if (input.PlannedShipDate is DateOnly ship && ship < input.OrderDate) ModelState.AddModelError(nameof(input.PlannedShipDate), "Sevk tarihi sipariş tarihinden önce olamaz.");
    }

    private static void Apply(ProductionOrder order, OrderInputVm input)
    {
        order.CustomerId = input.CustomerId;
        order.Title = input.Title.Trim();
        order.DrawingListId = input.DrawingListId;
        order.OrderDate = input.OrderDate;
        order.PlannedShipDate = input.PlannedShipDate;
        order.InstallAddress = input.InstallAddress?.Trim();
        order.ContactPhone = input.ContactPhone?.Trim();
        order.GlassExcluded = input.GlassExcluded;
        order.InstallationExcluded = input.InstallationExcluded;
        order.Reserved = input.Reserved;
        order.Notes = input.Notes?.Trim();
        order.UpdatedUtc = DateTime.UtcNow;
    }

    private async Task LoadOptions()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.Status != CustomerStatus.Passive && x.PartyType != PartyType.Supplier).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Suppliers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.PartyType != PartyType.Customer).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Lists = new SelectList(await _db.DrawingLists.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).Take(300).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
        ViewBag.Installers = await _db.Installers.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync();
    }

    public static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "-";

    public static string Line(KarakusAluminyum.Web.Models.Drawing.WindowDesign design) => $"{design.Name} - {KarakusAluminyum.Web.Models.Drawing.ProductCategories.Label(design.Category)}, {design.WidthMm} × {design.HeightMm} mm";
}
