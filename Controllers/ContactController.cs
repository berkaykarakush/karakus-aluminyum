using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Mvc;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Notifications;
namespace KarakusAluminyum.Web.Controllers;
[Route("iletisim")]
public class ContactController : Controller
{
    private readonly SiteSettingsProvider _settingsProvider;
    private readonly AppDbContext _db;
    private readonly NotificationQueue _notifications;
    private readonly NotificationSignal _signal;
    public ContactController(SiteSettingsProvider settingsProvider, AppDbContext db, NotificationQueue notifications, NotificationSignal signal)
    {
        _settingsProvider = settingsProvider;
        _db = db;
        _notifications = notifications;
        _signal = signal;
    }
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel { Title="İletişim ve Teklif | Karakuş Alüminyum", Description="Alüminyum doğrama ve mimari sistem projeniz için Karakuş Alüminyum ile iletişime geçin.", CanonicalPath="/iletisim", PageType="ContactPage", Breadcrumbs=new[]{("İletişim","/iletisim")} };
        ViewBag.Settings=await _settingsProvider.GetAsync(); ViewBag.Services=await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).Select(x=>x.Name).ToListAsync(); return View(new ContactRequest());
    }
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(32000)]
    public async Task<IActionResult> Index(ContactRequest model)
    {
        ViewData["Seo"] = new SeoViewModel { Title="İletişim ve Teklif | Karakuş Alüminyum", Description="Alüminyum doğrama ve mimari sistem projeniz için Karakuş Alüminyum ile iletişime geçin.", CanonicalPath="/iletisim", PageType="ContactPage", Breadcrumbs=new[]{("İletişim","/iletisim")} };
        ViewBag.Settings=await _settingsProvider.GetAsync();
        ViewBag.Services=await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).Select(x=>x.Name).ToListAsync();
        if (!string.IsNullOrWhiteSpace(model.Website)) return RedirectToAction(nameof(Thanks));
        if (!ModelState.IsValid) return View(model);
        var contact = model.ContactInfo.Trim();
        var text = model.Message.Trim();
        var windowStart = DateTime.UtcNow.AddMinutes(-10);
        // Çift tıklama ve sayfa yenileme aynı talebi ikinci kez kaydetmez, bildirim de göndermez.
        if (!await _db.ContactMessages.AnyAsync(x => x.ContactInfo == contact && x.Message == text && x.CreatedUtc > windowStart))
        {
            var message = new ContactMessage { Name=model.Name.Trim(), ContactInfo=model.ContactInfo.Trim(), ProjectLocation=model.ProjectLocation?.Trim(), Service=model.Service?.Trim(), Message=model.Message.Trim(), PrivacyAccepted=model.PrivacyAccepted };
            _db.ContactMessages.Add(message);
            await _notifications.AddForAsync(message);
            await _db.SaveChangesAsync();
            _signal.Wake();
        }
        TempData["ContactNotice"]="Talebiniz alındı. En kısa sürede dönüş yapılacaktır.";
        return RedirectToAction(nameof(Thanks));
    }
    [HttpGet("tesekkurler")]
    public IActionResult Thanks()
    {
        // Yalnızca gerçek form gönderiminden gelindiğinde dönüşüm sayılır; bot tuzağı veya doğrudan açılış sayılmaz.
        if (TempData["ContactNotice"] is string) { ViewData["TrackEvent"] = "lead"; }
        ViewData["Seo"] = new SeoViewModel { Title="Talebiniz Alındı | Karakuş Alüminyum", Description="İletişim talebi bilgilendirme sayfası.", CanonicalPath="/iletisim/tesekkurler", Robots="noindex,follow" };
        return View();
    }
}
