using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// README madde 8: işlerdeki eksik ve kusurlar (cam kırık, kol eksik...). Siparişe ve isteğe bağlı olarak poza bağlanır,
// giderilene kadar takip edilir ve sorumluya (camcı, atölye, montaj ekibi) verilecek liste olarak basılır.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/eksikler")]
public sealed class PunchController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public PunchController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    private string? UserName => User.Identity?.Name;

    [HttpGet("")]
    public async Task<IActionResult> Index(PunchStatus? durum, string? tur, string? sorumlu, string? q, bool tumu = false)
    {
        var query = _db.PunchItems.AsNoTracking().Include(x => x.ProductionOrder).ThenInclude(x => x!.Customer).Include(x => x.WindowDesign).AsQueryable();
        if (durum is PunchStatus status) query = query.Where(x => x.Status == status);
        else if (!tumu) query = query.Where(x => x.Status == PunchStatus.Open || x.Status == PunchStatus.Waiting);
        if (!string.IsNullOrWhiteSpace(tur)) query = query.Where(x => x.Kind == tur);
        if (!string.IsNullOrWhiteSpace(sorumlu)) query = query.Where(x => x.Responsible == sorumlu);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => EF.Functions.ILike(x.Description, $"%{q}%") || EF.Functions.ILike(x.ProductionOrder!.OrderNumber, $"%{q}%")
                || EF.Functions.ILike(x.ProductionOrder!.Title, $"%{q}%") || EF.Functions.ILike(x.ProductionOrder!.Customer!.Name, $"%{q}%"));
        ViewBag.Counts = await _db.PunchItems.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        (ViewBag.Status, ViewBag.Kind, ViewBag.Party, ViewBag.Query, ViewBag.All) = (durum, tur, sorumlu, q, tumu);
        var items = await query.Take(500).ToListAsync();
        ViewBag.Positions = await PositionsAsync(_db, items.Select(x => x.ProductionOrder?.DrawingListId));
        var today = KarakusAluminyum.Web.Services.Documents.PrintDates.Today;
        return View(items.OrderBy(x => PunchLabels.IsOpen(x.Status) ? 0 : 1).ThenBy(x => IsLate(x, today) ? 0 : 1).ThenBy(x => x.DueOn ?? DateOnly.MaxValue).ThenByDescending(x => x.Id).ToList());
    }

    public static bool IsLate(PunchItem item, DateOnly today) => PunchLabels.IsOpen(item.Status) && item.DueOn is DateOnly due && due < today;

    // poz: sipariş ekranındaki poz satırından açılınca seçili gelir.
    [HttpGet("ekle/{orderId:int}")]
    public async Task<IActionResult> Create(int orderId, int? poz, string? asama)
    {
        var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId);
        if (order is null) return NotFound();
        var input = new PunchInputVm { ProductionOrderId = orderId, WindowDesignId = poz, ReportedOn = KarakusAluminyum.Web.Services.Documents.PrintDates.Today };
        if (asama is not null && PunchLabels.Stages.Contains(asama)) input.Stage = asama;
        else input.Stage = DefaultStage(order.Status);
        await LoadOptions(order);
        return View("Editor", input);
    }

    // Siparişin durumuna göre eksiğin büyük olasılıkla fark edildiği yer.
    public static string DefaultStage(OrderStatus status) => status switch
    {
        OrderStatus.Plan or OrderStatus.AwaitingApproval or OrderStatus.Approved or OrderStatus.InProduction => "Üretim",
        OrderStatus.Warehouse => "Sevkiyat / yükleme",
        OrderStatus.Delivered => "Teslim",
        _ => "Montaj"
    };

    [HttpPost("ekle/{orderId:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int orderId, PunchInputVm input, string? donus)
    {
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == orderId);
        if (order is null) return NotFound();
        input.Id = 0;
        input.ProductionOrderId = orderId;
        await Validate(input, order);
        if (!ModelState.IsValid) { await LoadOptions(order); return View("Editor", input); }
        var item = new PunchItem { ProductionOrderId = orderId, ReportedBy = UserName };
        Apply(item, input);
        _db.PunchItems.Add(item);
        OrderService.Record(order, $"Eksik kaydedildi: {Summary(item)}.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "PunchItem", item.Id.ToString(), $"{order.OrderNumber}: {item.Kind}");
        TempData["Notice"] = "Eksik kaydedildi.";
        return Back(orderId, donus);
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.PunchItems.AsNoTracking().Include(x => x.ProductionOrder).FirstOrDefaultAsync(x => x.Id == id);
        if (item?.ProductionOrder is null) return NotFound();
        await LoadOptions(item.ProductionOrder);
        return View("Editor", new PunchInputVm
        {
            Id = id, ProductionOrderId = item.ProductionOrderId, WindowDesignId = item.WindowDesignId, Location = item.Location, Kind = item.Kind, Description = item.Description,
            Quantity = item.Quantity, Stage = item.Stage, Responsible = item.Responsible, Status = item.Status, ReportedOn = item.ReportedOn, DueOn = item.DueOn, ResolvedOn = item.ResolvedOn, Resolution = item.Resolution
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PunchInputVm input, string? donus)
    {
        var item = await _db.PunchItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstAsync(x => x.Id == item.ProductionOrderId);
        input.Id = id;
        input.ProductionOrderId = item.ProductionOrderId;
        await Validate(input, order);
        if (!ModelState.IsValid) { await LoadOptions(order); return View("Editor", input); }
        var oldStatus = item.Status;
        Apply(item, input);
        if (oldStatus != item.Status) OrderService.Record(order, $"Eksik {PunchLabels.Label(item.Status).ToLowerInvariant()}: {Summary(item)}.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "PunchItem", id.ToString(), $"{order.OrderNumber}: {item.Kind}");
        TempData["Notice"] = "Eksik güncellendi.";
        return Back(item.ProductionOrderId, donus);
    }

    // Hızlı durum değişikliği: bekleniyor, giderildi (yapılan işlemle), iptal, yeniden aç.
    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, PunchStatus status, string? resolution, string? donus)
    {
        var item = await _db.PunchItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        if (item.Status != status)
        {
            var order = await _db.ProductionOrders.Include(x => x.Events).FirstAsync(x => x.Id == item.ProductionOrderId);
            ChangeStatus(item, status, resolution, KarakusAluminyum.Web.Services.Documents.PrintDates.Today);
            OrderService.Record(order, $"Eksik {PunchLabels.Label(status).ToLowerInvariant()}: {Summary(item)}.", UserName);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("status", "PunchItem", id.ToString(), $"{order.OrderNumber}: {status}");
            TempData["Notice"] = $"Eksik: {PunchLabels.Label(status)}.";
        }
        return Back(item.ProductionOrderId, donus);
    }

    public static void ChangeStatus(PunchItem item, PunchStatus status, string? resolution, DateOnly today)
    {
        item.Status = status;
        if (!string.IsNullOrWhiteSpace(resolution)) item.Resolution = resolution.Trim()[..Math.Min(resolution.Trim().Length, 1000)];
        item.ResolvedOn = status == PunchStatus.Resolved ? item.ResolvedOn ?? today : null;
        item.UpdatedUtc = DateTime.UtcNow;
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? donus)
    {
        var item = await _db.PunchItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstAsync(x => x.Id == item.ProductionOrderId);
        _db.PunchItems.Remove(item);
        OrderService.Record(order, $"Eksik kaydı silindi: {Summary(item)}.", UserName);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "PunchItem", id.ToString(), $"{order.OrderNumber}: {item.Kind}");
        TempData["Notice"] = "Eksik kaydı silindi.";
        return Back(item.ProductionOrderId, donus);
    }

    // Eksik listesi çıktısı: sorumluya (ör. yalnızca camcıya) verilecek liste; varsayılan olarak yalnızca açık kayıtlar.
    [HttpGet("yazdir/{orderId:int}")]
    public async Task<IActionResult> Print(int orderId, string? sorumlu, bool tumu = false)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.Installer).FirstOrDefaultAsync(x => x.Id == orderId);
        if (order is null) return NotFound();
        var query = _db.PunchItems.AsNoTracking().Include(x => x.WindowDesign).Where(x => x.ProductionOrderId == orderId);
        if (!tumu) query = query.Where(x => x.Status == PunchStatus.Open || x.Status == PunchStatus.Waiting);
        if (!string.IsNullOrWhiteSpace(sorumlu)) query = query.Where(x => x.Responsible == sorumlu);
        ViewBag.Items = await query.OrderBy(x => x.ReportedOn).ThenBy(x => x.Id).ToListAsync();
        ViewBag.Positions = await PositionsAsync(_db, new[] { order.DrawingListId });
        (ViewBag.Party, ViewBag.All) = (sorumlu, tumu);
        return View(order);
    }

    // Poz numarası çizim listesindeki sırasıdır (çıktılardaki "Poz 1, Poz 2").
    public static async Task<Dictionary<int, int>> PositionsAsync(AppDbContext db, IEnumerable<int?> listIds)
    {
        var ids = listIds.OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.WindowDesigns.AsNoTracking().Where(x => ids.Contains(x.DrawingListId)).Select(x => new { x.Id, x.DrawingListId, x.SortOrder }).ToListAsync();
        return rows.GroupBy(x => x.DrawingListId).SelectMany(g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select((x, i) => (x.Id, Position: i + 1))).ToDictionary(x => x.Id, x => x.Position);
    }

    public static string Where(PunchItem item, IReadOnlyDictionary<int, int> positions)
    {
        string? poz = null;
        if (item.WindowDesign is { } d) poz = (positions.TryGetValue(d.Id, out var p) ? $"Poz {p}: " : "") + $"{d.Name} ({d.WidthMm} × {d.HeightMm} mm)";
        return string.Join(" · ", new[] { poz, item.Location }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } text ? text : "-";
    }

    private static string Summary(PunchItem item)
    {
        var text = item.Description.Length > 80 ? item.Description[..80] + "…" : item.Description;
        return $"{item.Kind} ({item.Quantity} adet) – {text}";
    }

    private IActionResult Back(int orderId, string? donus) => donus == "liste" ? Redirect("/yonetim/eksikler") : Redirect($"/yonetim/siparisler/detay/{orderId}#eksikler");

    private async Task Validate(PunchInputVm input, ProductionOrder order)
    {
        if (!PunchLabels.Kinds.Contains(input.Kind)) input.Kind = "Diğer";
        if (!PunchLabels.Stages.Contains(input.Stage)) input.Stage = "Montaj";
        if (!PunchLabels.Parties.Contains(input.Responsible)) input.Responsible = "Belirsiz";
        if (!Enum.IsDefined(input.Status)) input.Status = PunchStatus.Open;
        input.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        input.Resolution = string.IsNullOrWhiteSpace(input.Resolution) ? null : input.Resolution.Trim();
        if (input.WindowDesignId is int designId && (order.DrawingListId is not int listId || !await _db.WindowDesigns.AnyAsync(x => x.Id == designId && x.DrawingListId == listId)))
            ModelState.AddModelError(nameof(input.WindowDesignId), "Seçilen poz bu siparişin çizim listesinde yok.");
        if (input.Status == PunchStatus.Resolved && input.ResolvedOn is DateOnly resolved && resolved < input.ReportedOn)
            ModelState.AddModelError(nameof(input.ResolvedOn), "Giderildiği tarih bildirim tarihinden önce olamaz.");
    }

    private static void Apply(PunchItem item, PunchInputVm input)
    {
        item.WindowDesignId = input.WindowDesignId;
        item.Location = input.Location;
        item.Kind = input.Kind;
        item.Description = input.Description.Trim();
        item.Quantity = input.Quantity;
        item.Stage = input.Stage;
        item.Responsible = input.Responsible;
        item.ReportedOn = input.ReportedOn;
        item.DueOn = input.DueOn;
        item.ResolvedOn = input.ResolvedOn;
        ChangeStatus(item, input.Status, input.Resolution, KarakusAluminyum.Web.Services.Documents.PrintDates.Today);
        if (input.Resolution is null) item.Resolution = null;
    }

    private async Task LoadOptions(ProductionOrder order)
    {
        ViewBag.Order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstAsync(x => x.Id == order.Id);
        ViewBag.Pozlar = order.DrawingListId is int listId
            ? await _db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
            : new List<WindowDesign>();
    }
}
