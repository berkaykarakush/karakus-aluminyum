using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Mvc;
using KarakusAluminyum.Web.Services;
namespace KarakusAluminyum.Web.Controllers;
[Route("iletisim")]
public class ContactController : Controller
{
    private readonly SiteSettingsProvider _settingsProvider;
    private readonly AppDbContext _db;
    private readonly IContactNotificationService _notifications;
    public ContactController(SiteSettingsProvider settingsProvider, AppDbContext db, IContactNotificationService notifications)
    {
        _settingsProvider = settingsProvider;
        _db = db;
        _notifications = notifications;
    }
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Seo"] = new SeoViewModel { Title="İletişim ve Teklif | Karakuş Alüminyum", Description="Alüminyum doğrama ve mimari sistem projeniz için Karakuş Alüminyum ile iletişime geçin.", CanonicalPath="/iletisim" };
        ViewBag.Settings=await _settingsProvider.GetAsync(); ViewBag.Services=await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).Select(x=>x.Name).ToListAsync(); return View(new ContactRequest());
    }
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(32000)]
    public async Task<IActionResult> Index(ContactRequest model)
    {
        ViewData["Seo"] = new SeoViewModel { Title="İletişim ve Teklif | Karakuş Alüminyum", Description="Alüminyum doğrama ve mimari sistem projeniz için Karakuş Alüminyum ile iletişime geçin.", CanonicalPath="/iletisim" };
        ViewBag.Settings=await _settingsProvider.GetAsync();
        ViewBag.Services=await _db.Services.AsNoTracking().Where(x=>x.IsPublished).OrderBy(x=>x.SortOrder).Select(x=>x.Name).ToListAsync();
        if (!string.IsNullOrWhiteSpace(model.Website)) return RedirectToAction(nameof(Thanks));
        if (!ModelState.IsValid) return View(model);
        _db.ContactMessages.Add(new ContactMessage { Name=model.Name.Trim(), ContactInfo=model.ContactInfo.Trim(), ProjectLocation=model.ProjectLocation?.Trim(), Service=model.Service?.Trim(), Message=model.Message.Trim(), PrivacyAccepted=model.PrivacyAccepted });
        await _db.SaveChangesAsync();
        await _notifications.NotifyAsync(model);
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
