using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Barkodlu üretim: poz ve parça etiketleri, istasyonlarda okutma ve sipariş bazında ilerleme.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/uretim")]
public sealed class ProductionController : Controller
{
    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    private readonly IAuditService _audit;
    public ProductionController(AppDbContext db, OrderService orders, IAuditService audit) { _db = db; _orders = orders; _audit = audit; }

    private string? UserName => User.Identity?.Name;

    // tur: poz | parca; boyut: a4 (3 × 8, 70 × 37 mm) | termal (100 × 50 mm tek etiket)
    [HttpGet("etiketler/{id:int}")]
    public async Task<IActionResult> Labels(int id, string tur = "poz", string boyut = "a4")
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var designs = await DesignsAsync(order);
        ViewBag.Kind = tur == "parca" ? "parca" : "poz";
        ViewBag.Size = boyut == "termal" ? "termal" : "a4";
        ViewBag.Pozlar = ProductionLabels.Pozlar(order, designs);
        ViewBag.Parts = tur == "parca" ? ProductionLabels.Parts(order, designs) : new List<PartLabel>();
        return View(order);
    }

    [HttpGet("okut")]
    public async Task<IActionResult> Scan(string? istasyon)
    {
        ViewBag.Station = ProductionStations.All.Contains(istasyon) ? istasyon : ProductionStations.Cutting;
        ViewBag.Recent = await _db.ProductionScans.AsNoTracking().Include(x => x.ProductionOrder).OrderByDescending(x => x.AtUtc).Take(30).ToListAsync();
        var designIds = ((List<ProductionScan>)ViewBag.Recent).Select(x => x.WindowDesignId).Distinct().ToList();
        ViewBag.Names = await _db.WindowDesigns.AsNoTracking().Where(x => designIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        return View();
    }

    // El tipi okuyucu klavye gibi yazar ve Enter gönderir; form her okutmada bu adrese gelir.
    [HttpPost("okut"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(string? code, string? station)
    {
        station = ProductionStations.All.Contains(station) ? station! : ProductionStations.Cutting;
        try
        {
            TempData["Notice"] = await RecordAsync(code, station);
            TempData["ScanOk"] = true;
        }
        catch (OrderException error) { TempData["Notice"] = error.Message; }
        return RedirectToAction(nameof(Scan), new { istasyon = station });
    }

    [HttpPost("okut/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScan(int id, string? istasyon)
    {
        var scan = await _db.ProductionScans.FindAsync(id);
        if (scan is not null)
        {
            _db.ProductionScans.Remove(scan);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("delete", "ProductionScan", id.ToString(), $"{ProductionLabels.PozCode(scan.ProductionOrderId, scan.WindowDesignId, scan.Unit)} {scan.Station}");
            TempData["Notice"] = "Okutma silindi.";
        }
        return RedirectToAction(nameof(Scan), new { istasyon });
    }

    private async Task<string> RecordAsync(string? code, string station)
    {
        var parsed = ProductionLabels.Parse(code) ?? throw new OrderException($"\"{code}\" bir üretim etiketi değil.");
        var order = await _db.ProductionOrders.Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == parsed.OrderId) ?? throw new OrderException("Etiketin siparişi bulunamadı.");
        if (!OrderStatuses.IsOpen(order.Status)) throw new OrderException($"{order.OrderNumber} kapanmış bir sipariş ({OrderStatuses.Label(order.Status)}).");
        var design = await _db.WindowDesigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == parsed.DesignId && x.DrawingListId == order.DrawingListId)
            ?? throw new OrderException($"Etiketteki poz {order.OrderNumber} siparişinin çizim listesinde yok; etiketleri yeniden basın.");
        if (parsed.Unit > design.Quantity) throw new OrderException($"{design.Name} pozunun {design.Quantity} adedi var; etiket {parsed.Unit}. adedi gösteriyor.");

        var label = $"{order.OrderNumber} · {design.Name} ({parsed.Unit}/{design.Quantity})";
        if (await _db.ProductionScans.AnyAsync(x => x.ProductionOrderId == order.Id && x.WindowDesignId == design.Id && x.Unit == parsed.Unit && x.Station == station))
            return $"{label}: {ProductionStations.Label(station)} zaten okutulmuş.";

        _db.ProductionScans.Add(new ProductionScan { ProductionOrderId = order.Id, WindowDesignId = design.Id, Unit = parsed.Unit, Station = station, User = UserName });
        var extra = "";
        // Ercom akışı: onaylı siparişte ilk okutma üretime alır; son adet paketlenince depoya geçer.
        if (order.Status is OrderStatus.Approved)
        {
            _orders.ChangeStatus(order, OrderStatus.InProduction, "İlk barkod okutması.", UserName);
            extra = " Sipariş üretime alındı.";
        }
        await _db.SaveChangesAsync();
        if (station == ProductionStations.Packed && order.Status == OrderStatus.InProduction)
        {
            var total = await _db.WindowDesigns.Where(x => x.DrawingListId == order.DrawingListId).SumAsync(x => x.Quantity);
            var packed = await _db.ProductionScans.CountAsync(x => x.ProductionOrderId == order.Id && x.Station == ProductionStations.Packed);
            if (packed >= total)
            {
                _orders.ChangeStatus(order, OrderStatus.Warehouse, "Tüm pozlar paketlendi.", UserName);
                await _db.SaveChangesAsync();
                extra += " Tüm pozlar paketlendi; sipariş depoya alındı.";
            }
        }
        return $"{label}: {ProductionStations.Label(station)} kaydedildi.{extra}";
    }

    // ---- Kesim makinesi aktarımı ----

    [HttpGet("kesim-aktarimi/{id:int}")]
    public async Task<IActionResult> MachineExportPage(int id, int? makine)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();
        var profiles = await _db.MachineExportProfiles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        var (pieces, warnings) = await PiecesAsync(order);
        ViewBag.Profiles = profiles;
        ViewBag.Profile = profiles.FirstOrDefault(x => x.Id == makine) ?? profiles.FirstOrDefault();
        ViewBag.Pieces = pieces;
        ViewBag.Warnings = warnings;
        return View("MachineExport", order);
    }

    [HttpGet("kesim-dosyasi/{id:int}")]
    public async Task<IActionResult> MachineFile(int id, int makine)
    {
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id);
        var profile = await _db.MachineExportProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == makine);
        if (order is null || profile is null) return NotFound();
        var (pieces, warnings) = await PiecesAsync(order);
        if (pieces.Count == 0) { TempData["Notice"] = "Aktarılacak kesim parçası yok."; return RedirectToAction(nameof(MachineExportPage), new { id, makine }); }
        var file = MachineExport.Build(profile, order, pieces, warnings);
        await _audit.RecordAsync("export", "ProductionOrder", id.ToString(), $"{order.OrderNumber}: {profile.Name} ({file.Rows} satır)");
        return File(file.Content, "text/plain", file.FileName);
    }

    private async Task<(List<MachinePiece>, List<string>)> PiecesAsync(ProductionOrder order)
    {
        var designs = await DesignsAsync(order);
        var productIds = designs.Where(x => x.ProfileSystem is not null).SelectMany(x => MaterialTakeoff.ParseRules(x.ProfileSystem!.MaterialsJson).Select(r => r.ProductId)).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return MachineExport.Pieces(order, designs, products);
    }

    [HttpGet("makineler")]
    public async Task<IActionResult> Machines() => View(await _db.MachineExportProfiles.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync());

    [HttpGet("makineler/ekle")]
    public IActionResult CreateMachine() => View("MachineEditor", new MachineExportProfile());

    [HttpPost("makineler/ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMachine(MachineExportProfile input)
    {
        input.Id = 0;
        if (!PrepareMachine(input)) return View("MachineEditor", input);
        _db.MachineExportProfiles.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "MachineExportProfile", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Machines));
    }

    [HttpGet("makineler/duzenle/{id:int}")]
    public async Task<IActionResult> EditMachine(int id)
    {
        var row = await _db.MachineExportProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return row is null ? NotFound() : View("MachineEditor", row);
    }

    [HttpPost("makineler/duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMachine(int id, MachineExportProfile input)
    {
        if (!await _db.MachineExportProfiles.AnyAsync(x => x.Id == id)) return NotFound();
        input.Id = id;
        if (!PrepareMachine(input)) return View("MachineEditor", input);
        _db.MachineExportProfiles.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "MachineExportProfile", id.ToString(), input.Name);
        return RedirectToAction(nameof(Machines));
    }

    [HttpPost("makineler/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMachine(int id)
    {
        var row = await _db.MachineExportProfiles.FindAsync(id);
        if (row is null) return NotFound();
        _db.MachineExportProfiles.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "MachineExportProfile", id.ToString(), row.Name);
        return RedirectToAction(nameof(Machines));
    }

    // Sütunlar formda alan başına sıra numarasıyla gelir (col_length=3); boş olanlar dosyaya yazılmaz.
    private bool PrepareMachine(MachineExportProfile input)
    {
        var ordered = MachineExportFields.All
            .Select(f => (f.Key, Order: int.TryParse(Request.Form[$"col_{f.Key}"], out var n) ? n : 0))
            .Where(x => x.Order > 0).OrderBy(x => x.Order).Select(x => x.Key).ToList();
        if (Request.HasFormContentType && Request.Form.Keys.Any(k => k.StartsWith("col_"))) input.Columns = string.Join(",", ordered);
        ModelState.Remove(nameof(input.Columns));
        if (MachineExport.Columns(input.Columns).Count == 0) ModelState.AddModelError(nameof(input.Columns), "En az bir sütun seçin.");
        if (input.Delimiter is not (";" or "," or "tab" or "|")) ModelState.AddModelError(nameof(input.Delimiter), "Ayraç ; , | veya sekme olabilir.");
        if (input.DecimalSeparator is not ("," or ".")) ModelState.AddModelError(nameof(input.DecimalSeparator), "Ondalık işareti virgül veya nokta olabilir.");
        if (input.Delimiter == input.DecimalSeparator) ModelState.AddModelError(nameof(input.Delimiter), "Ayraç ile ondalık işareti aynı olamaz.");
        if (!MachineExportFields.Encodings.Contains(input.Encoding)) ModelState.AddModelError(nameof(input.Encoding), "Geçersiz karakter kodlaması.");
        input.Extension = string.IsNullOrWhiteSpace(input.Extension) ? "csv" : new string(input.Extension.Trim().TrimStart('.').Where(char.IsLetterOrDigit).ToArray());
        if (input.Extension.Length == 0) input.Extension = "csv";
        input.Name = input.Name?.Trim() ?? "";
        return ModelState.IsValid;
    }

    private async Task<List<WindowDesign>> DesignsAsync(ProductionOrder order) => order.DrawingListId is int listId
        ? await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
        : new List<WindowDesign>();

    // Sipariş detayındaki ilerleme: istasyon başına okutulan adet / toplam adet.
    public static async Task<Dictionary<string, int>> ProgressAsync(AppDbContext db, int orderId)
        => await db.ProductionScans.Where(x => x.ProductionOrderId == orderId).GroupBy(x => x.Station).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
}
