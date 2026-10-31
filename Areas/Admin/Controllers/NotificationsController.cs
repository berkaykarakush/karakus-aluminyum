using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin"), Route("yonetim/bildirimler")]
public sealed class NotificationsController : Controller
{
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    private readonly IEmailSender _email;
    private readonly IWhatsAppSender _whatsApp;
    private readonly NotificationSignal _signal;
    private readonly IAuditService _audit;
    public NotificationsController(AppDbContext db, SiteSettingsProvider settings, IEmailSender email, IWhatsAppSender whatsApp, NotificationSignal signal, IAuditService audit) { _db = db; _settings = settings; _email = email; _whatsApp = whatsApp; _signal = signal; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(NotificationStatus? durum)
    {
        var query = _db.NotificationJobs.AsNoTracking().Include(x => x.ContactMessage).AsQueryable();
        if (durum is not null) query = query.Where(x => x.Status == durum);
        ViewBag.Status = durum;
        ViewBag.Settings = await _settings.GetAsync();
        ViewBag.EmailReady = _email.IsConfigured;
        ViewBag.WhatsAppReady = _whatsApp.IsConfigured;
        ViewBag.FailedCount = await _db.NotificationJobs.CountAsync(x => x.Status == NotificationStatus.Failed);
        return View(await query.OrderByDescending(x => x.Id).Take(200).ToListAsync());
    }

    [HttpPost("yeniden/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int id)
    {
        var job = await _db.NotificationJobs.FindAsync(id);
        if (job is null) return NotFound();
        if (job.Status is NotificationStatus.Failed or NotificationStatus.Skipped)
        {
            (job.Status, job.Attempts, job.NextAttemptUtc, job.LastError) = (NotificationStatus.Pending, 0, DateTime.UtcNow, null);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("retry", "NotificationJob", id.ToString(), job.Channel.ToString());
            _signal.Wake();
            TempData["Notice"] = "Bildirim yeniden sıraya alındı.";
        }
        return RedirectToAction(nameof(Index));
    }

    // Ayarları denemek için doğrudan gönderir; kuyruğa girmez ve sonucu hemen gösterir.
    [HttpPost("test"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(string kanal, string? alici)
    {
        var site = await _settings.GetAsync();
        var sample = new ContactMessage { Name = "Deneme Bildirimi", ContactInfo = "ornek@example.com", Service = "Deneme", ProjectLocation = "İzmir", Message = "Bu bir deneme bildirimidir." };
        try
        {
            if (string.IsNullOrWhiteSpace(alici)) throw new PermanentNotificationException("Alıcı yazın.");
            alici = alici.Trim();
            if (kanal == "whatsapp") await _whatsApp.SendAsync(NotificationComposer.AdminWhatsApp(sample, alici, site), HttpContext.RequestAborted);
            else
            {
                if (!NotificationComposer.IsEmail(alici)) throw new PermanentNotificationException("Geçerli bir e-posta yazın.");
                await _email.SendAsync(NotificationComposer.AdminEmail(sample, alici, site), HttpContext.RequestAborted);
            }
            TempData["Notice"] = $"Deneme gönderildi: {alici}";
            await _audit.RecordAsync("test", "NotificationJob", kanal, alici);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            TempData["Notice"] = "Deneme başarısız: " + (error.Message.Length > 300 ? error.Message[..300] : error.Message);
        }
        return RedirectToAction(nameof(Index));
    }
}
