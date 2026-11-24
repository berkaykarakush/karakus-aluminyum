using System.ComponentModel.DataAnnotations;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Notifications;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Controllers;

// README 7.7: müşteri takip sayfası. QR veya bağlantıyla açılır (giriş yok, anahtar 128 bit rastgele, panelden yenilenir).
// Sipariş durumu, tarihler, pozlar ve servis talepleri görünür; fiyat, maliyet, iç not ve adres gösterilmez.
// Sevk edilen veya teslim edilen işte müşteri buradan doğrudan servis talebi açabilir.
[AllowAnonymous, Route("takip")]
public sealed class TrackingController : Controller
{
    public const int DailyWebRequestLimit = 3;
    private readonly AppDbContext _db;
    private readonly ServiceDesk _desk;
    private readonly SiteSettingsProvider _settings;
    private readonly NotificationOptions _options;
    private readonly NotificationSignal _signal;
    public TrackingController(AppDbContext db, ServiceDesk desk, SiteSettingsProvider settings, IOptions<NotificationOptions> options, NotificationSignal signal)
    { _db = db; _desk = desk; _settings = settings; _options = options.Value; _signal = signal; }

    public sealed class ServiceInput
    {
        [Required(ErrorMessage = "Adınızı yazın."), StringLength(120)] public string ContactName { get; set; } = "";
        [Required(ErrorMessage = "Size ulaşabileceğimiz telefonu yazın."), StringLength(40)] public string ContactPhone { get; set; } = "";
        [StringLength(60)] public string Category { get; set; } = "Diğer";
        [Required(ErrorMessage = "Sorunu kısaca yazın."), StringLength(2000, ErrorMessage = "En fazla 2000 karakter yazabilirsiniz.")] public string Complaint { get; set; } = "";
        public List<int> PozIds { get; set; } = new();
        public string? Website { get; set; } // bot tuzağı
        [Range(typeof(bool), "true", "true", ErrorMessage = "KVKK aydınlatma metnini onaylayın.")] public bool PrivacyAccepted { get; set; }
    }

    public sealed record TrackingVm(ProductionOrder Order, List<WindowDesign> Pozlar, List<ServiceRequest> Services, ServiceInput Input, bool CanRequestService, SiteSettings Site, PaymentSummary Payment);

    // Müşteriye gösterilen adımlar: iç durumlar sadeleştirilir.
    public static readonly (string Label, OrderStatus[] Statuses)[] Steps =
    {
        ("Sipariş alındı", new[] { OrderStatus.Plan, OrderStatus.AwaitingApproval, OrderStatus.Approved }),
        ("Üretimde", new[] { OrderStatus.InProduction }),
        ("Sevke hazır", new[] { OrderStatus.Warehouse }),
        ("Sevk edildi", new[] { OrderStatus.Shipped }),
        ("Teslim edildi", new[] { OrderStatus.Delivered })
    };

    public static int StepOf(OrderStatus status) => Array.FindIndex(Steps, s => s.Statuses.Contains(status));
    public static bool CanRequestService(OrderStatus status) => status is OrderStatus.Shipped or OrderStatus.Delivered;

    [HttpGet("{token}"), EnableRateLimiting(DocumentLinksController.RateLimitPolicy)]
    public async Task<IActionResult> Index(string token)
    {
        var vm = await LoadAsync(token, new ServiceInput());
        return vm is null ? NotFound() : Page(vm);
    }

    [HttpPost("{token}/servis"), ValidateAntiForgeryToken, EnableRateLimiting(ContactController.RateLimitPolicy), RequestSizeLimit(32000)]
    public async Task<IActionResult> RequestService(string token, ServiceInput input)
    {
        var vm = await LoadAsync(token, input);
        if (vm is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(input.Website)) return Redirect(DocumentLinks.TrackingPath(token)); // bot: sessizce yok sayılır
        if (!vm.CanRequestService) ModelState.AddModelError("", "Servis talebi teslim edilen işler için açılabilir; bize telefonla ulaşabilirsiniz.");
        var order = vm.Order;
        var since = DateTime.UtcNow.AddDays(-1);
        if (await _db.ServiceRequests.CountAsync(x => x.ProductionOrderId == order.Id && x.Channel == "Web sitesi" && x.CreatedUtc > since) >= DailyWebRequestLimit)
            ModelState.AddModelError("", "Bu iş için bugün yeterince talep aldık; ekibimiz sizi arayacak. Acil durumda bize telefonla ulaşın.");
        if (!ModelState.IsValid) return Page(vm);

        var complaint = input.Complaint.Trim();
        var recent = DateTime.UtcNow.AddMinutes(-10);
        // Çift tıklama ve sayfa yenileme ikinci talep açmaz.
        var existing = await _db.ServiceRequests.Where(x => x.ProductionOrderId == order.Id && x.Complaint == complaint && x.CreatedUtc > recent).Select(x => x.Number).FirstOrDefaultAsync();
        if (existing is null)
        {
            var today = PrintDates.Today;
            var pozIds = vm.Pozlar.Select(p => p.Id).Intersect(input.PozIds).ToList();
            var request = new ServiceRequest
            {
                Number = await _desk.NextRequestNumberAsync(today), CustomerId = order.CustomerId, ProductionOrderId = order.Id, ReceivedOn = today, Channel = "Web sitesi",
                Category = ServiceLabels.Categories.Contains(input.Category) ? input.Category : "Diğer", ContactName = input.ContactName.Trim(), ContactPhone = input.ContactPhone.Trim(),
                Address = order.InstallAddress ?? OrderService.Location(order.Customer), PozIds = pozIds.Count == 0 ? null : string.Join(",", pozIds), Complaint = complaint,
                Warranty = ServiceDesk.SuggestWarranty(order.DeliveredOn, await _desk.WarrantyYearsAsync(order), today), Notes = "Müşteri takip sayfasından açıldı."
            };
            _db.ServiceRequests.Add(request);
            var tracked = await _db.ProductionOrders.Include(x => x.Events).FirstAsync(x => x.Id == order.Id);
            OrderService.Record(tracked, $"Müşteri takip sayfasından servis talebi açtı: {request.Number} ({request.Category}).", null);
            await TeamNotices.AddServiceRequestAsync(_db, _settings, _options, request);
            await _db.SaveChangesAsync();
            _signal.Wake();
            existing = request.Number;
        }
        TempData["TrackingNotice"] = $"Servis talebiniz alındı ({existing}). Ekibimiz en kısa sürede sizi arayacak.";
        return Redirect(DocumentLinks.TrackingPath(token) + "#servis-alindi");
    }

    private async Task<TrackingVm?> LoadAsync(string token, ServiceInput input)
    {
        if (!DocumentLinks.LooksValid(token)) return null;
        var order = await _db.ProductionOrders.AsNoTracking().Include(x => x.Customer).Include(x => x.Installer).FirstOrDefaultAsync(x => x.TrackingToken == token);
        if (order is null) return null;
        var pozlar = order.DrawingListId is int listId
            ? await _db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
            : new List<WindowDesign>();
        var services = await _db.ServiceRequests.AsNoTracking().Where(x => x.ProductionOrderId == order.Id && x.Status != ServiceStatus.Cancelled).OrderByDescending(x => x.Id).Take(20).ToListAsync();
        var payment = await PaymentPlans.SummaryAsync(_db, order.Id, PrintDates.Today);
        return new TrackingVm(order, pozlar, services, input, CanRequestService(order.Status), await _settings.GetAsync(), payment);
    }

    private IActionResult Page(TrackingVm vm)
    {
        // Anahtar adreste olduğu için: arama motoru, önbellek ve yönlendiren adres kapalı; noindex sayfada analitik de yüklenmez.
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ViewData["Seo"] = new SeoViewModel { Title = $"Sipariş {vm.Order.OrderNumber} | {vm.Site.SiteName}", Description = "Sipariş takip sayfası.", CanonicalPath = "/takip", Robots = "noindex,nofollow" };
        return View("Index", vm);
    }
}
