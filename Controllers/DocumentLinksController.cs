using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Controllers;

// Belgedeki QR kodun açtığı sayfa: teklif, sözleşme veya çizim listesinin sistemdeki asıl hali.
// Giriş gerekmez; anahtar 128 bit rastgeledir ve panelden yenilenebilir. Maliyet, marj ve malzeme bilgisi gösterilmez.
// Sayfalar arama motorlarına kapalıdır ve önbelleğe alınmaz (yenilenen bağlantı hemen geçersiz olur).
[AllowAnonymous, Route("belge"), EnableRateLimiting(RateLimitPolicy)]
public sealed class DocumentLinksController : Controller
{
    public const string RateLimitPolicy = "documents";
    private readonly AppDbContext _db;
    private readonly KarakusAluminyum.Web.Services.Production.OrderService _orders;
    private readonly KarakusAluminyum.Web.Services.SiteSettingsProvider _settings;
    private readonly KarakusAluminyum.Web.Services.NotificationOptions _options;
    private readonly KarakusAluminyum.Web.Services.Notifications.NotificationSignal _signal;
    public DocumentLinksController(AppDbContext db, KarakusAluminyum.Web.Services.Production.OrderService orders, KarakusAluminyum.Web.Services.SiteSettingsProvider settings,
        Microsoft.Extensions.Options.IOptions<KarakusAluminyum.Web.Services.NotificationOptions> options, KarakusAluminyum.Web.Services.Notifications.NotificationSignal signal)
    { _db = db; _orders = orders; _settings = settings; _options = options.Value; _signal = signal; }

    [HttpGet("teklif/{token}")]
    public async Task<IActionResult> Quote(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.PublicToken == token);
        if (quote is null) return NotFound();
        Private($"{quote.QuoteNumber} numaralı proforma teklif · {TurkishNumbers.Date(quote.DocumentDate)} · Durum: {Status(quote.Status)}", DocumentLinks.Quote, token);
        ViewData["Acceptance"] = new QuoteAcceptanceVm(token, QuoteAcceptance.Blocked(quote, DateTime.UtcNow), quote.AcceptedUtc, quote.AcceptedName, quote.AcceptedTotal);
        return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", await DocumentViews.QuotePrintAsync(_db, quote));
    }

    // README 7.18: müşteri teklifi adını yazıp onaylar; teklif "kabul edildi" olur, sipariş açılır, ekibe ve müşteriye e-posta gider.
    [HttpPost("teklif/{token}/kabul"), ValidateAntiForgeryToken, EnableRateLimiting(KarakusAluminyum.Web.Controllers.ContactController.RateLimitPolicy), RequestSizeLimit(8000)]
    public async Task<IActionResult> AcceptQuote(string token, string? name, bool agree)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var quote = await _db.Quotes.Include(x => x.Customer).FirstOrDefaultAsync(x => x.PublicToken == token);
        if (quote is null) return NotFound();
        var back = $"/belge/{DocumentLinks.Quote}/{token}#onay";
        if (QuoteAcceptance.Blocked(quote, DateTime.UtcNow) is string blocked) { TempData["AcceptNotice"] = blocked; return Redirect(back); }
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < QuoteAcceptance.MinNameLength || !agree)
        {
            TempData["AcceptNotice"] = "Onay için adınızı soyadınızı yazın ve kutuyu işaretleyin.";
            return Redirect(back);
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        QuoteAcceptance.Accept(quote, name, ip, Request.Headers.UserAgent.ToString(), DateTime.UtcNow);
        var order = await _orders.FromQuoteAsync(quote.Id, null); // teklif ve sipariş aynı anda kaydedilir
        var tracked = await _db.ProductionOrders.Include(x => x.Events).FirstAsync(x => x.Id == order.Id);
        tracked.TrackingToken ??= DocumentLinks.NewToken();
        KarakusAluminyum.Web.Services.Production.OrderService.Record(tracked, $"Müşteri teklifi online onayladı: {quote.AcceptedName}, {quote.AcceptedTotal:#,0.00} ₺.", null);
        await KarakusAluminyum.Web.Services.Notifications.TeamNotices.AddAsync(_db, _settings, _options, KarakusAluminyum.Web.Models.CustomerEvents.QuoteAccepted, job => job.ProductionOrderId = order.Id);
        var site = await _settings.GetAsync();
        if (site.CustomerNotifyEmailEnabled == "true" && quote.Customer is { NotificationsOptOut: false } customer && KarakusAluminyum.Web.Services.Notifications.NotificationComposer.IsEmail(customer.Email))
            _db.NotificationJobs.Add(new KarakusAluminyum.Web.Models.NotificationJob { Channel = KarakusAluminyum.Web.Models.NotificationChannel.CustomerEmail, Recipient = customer.Email!.Trim(), Event = KarakusAluminyum.Web.Models.CustomerEvents.QuoteAccepted, ProductionOrderId = order.Id });
        await _db.SaveChangesAsync();
        _signal.Wake();
        TempData["AcceptNotice"] = $"Teşekkür ederiz, teklif onaylandı. {order.OrderNumber} numaralı siparişiniz oluşturuldu; ekibimiz sizinle iletişime geçecek.";
        return Redirect(back);
    }

    [HttpGet("sozlesme/{token}")]
    public async Task<IActionResult> Contract(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.PublicToken == token);
        if (contract is null) return NotFound();
        Private($"{contract.ContractNumber} numaralı sözleşme · {TurkishNumbers.Date(contract.DocumentDate)} · Durum: {contract.Status switch { ContractStatus.Signed => "İmzalandı", ContractStatus.Cancelled => "İptal edildi", _ => "Taslak" }}", DocumentLinks.Contract, token);
        return View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", await DocumentViews.ContractPrintAsync(_db, contract));
    }

    [HttpGet("cizim/{token}")]
    public async Task<IActionResult> Drawing(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Brand)
            .Include(x => x.Items).ThenInclude(x => x.ProfileSystem).AsSplitQuery().FirstOrDefaultAsync(x => x.PublicToken == token);
        if (list is null) return NotFound();
        Private($"Çizim listesi #{list.Id} · Son güncelleme {TurkishNumbers.Date(list.UpdatedUtc.ToLocalTime())}", DocumentLinks.Drawing, token);
        return View("~/Views/DocumentLinks/Drawing.cshtml", list);
    }

    private void Private(string note, string kind, string token)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ViewData["PublicNote"] = note + ". Bu sayfa belgenin Karakuş Alüminyum kayıtlarındaki asıl halidir.";
        ViewData["Qr"] = DocumentLinks.Qr(Request, kind, token);
    }

    private static string Status(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "Taslak", QuoteStatus.Sent => "Gönderildi", QuoteStatus.Accepted => "Kabul edildi", QuoteStatus.Rejected => "Reddedildi",
        QuoteStatus.Expired => "Süresi doldu", _ => "Sözleşmeye dönüştürüldü"
    };
}
