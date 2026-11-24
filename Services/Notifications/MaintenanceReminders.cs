using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Notifications;

// README 7.8: teslim edilmiş işlerde yıllık bakım hatırlatması (teslimin yıldönümünde) ve garanti bitmeden 30 gün önce hatırlatma.
// Önceden kuyruğa yazılmaz: bildirim kuyruğu saatte bir tarar, günü gelen (son 3 gün içinde kaçırılan dahil) hatırlatmayı bir kez ekler.
public sealed class MaintenanceReminders
{
    public const int MaxYears = 10;
    public const int WarrantyNoticeDays = 30;
    private const int CatchUpDays = 3;
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public MaintenanceReminders(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    public sealed record Due(string Event, DateOnly Date, int Year);

    // Bir iş için hatırlatma günleri. Garantinin bittiği yıldönümü atlanır: 30 gün önceki garanti hatırlatması onun yerine geçer.
    public static List<Due> Schedule(DateOnly delivered, int warrantyYears)
    {
        var list = new List<Due>();
        for (var n = 1; n <= MaxYears; n++)
            if (n != warrantyYears) list.Add(new Due(CustomerEvents.MaintenanceAnnual, delivered.AddYears(n), n));
        list.Add(new Due(CustomerEvents.MaintenanceWarranty, delivered.AddYears(warrantyYears).AddDays(-WarrantyNoticeDays), warrantyYears));
        return list.OrderBy(x => x.Date).ToList();
    }

    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var site = await _settings.GetAsync(cancellationToken);
        if (site.CustomerNotifyMaintenance != "true") return 0;
        var today = CustomerNotifier.Today(nowUtc);
        var earliest = today.AddYears(-MaxYears).AddDays(-CatchUpDays - 1);
        var orders = await _db.ProductionOrders.Include(x => x.Customer)
            .Where(x => x.Status == OrderStatus.Delivered && x.DeliveredOn != null && x.DeliveredOn >= earliest && !x.Customer!.NotificationsOptOut).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var order in orders)
        {
            var years = await ServiceDesk.WarrantyYearsAsync(_db, order);
            var due = Schedule(order.DeliveredOn!.Value, years).Where(d => d.Date <= today && d.Date > today.AddDays(-CatchUpDays)).ToList();
            foreach (var d in due)
            {
                if (await _db.NotificationJobs.AnyAsync(x => x.ProductionOrderId == order.Id && x.Event == d.Event && x.EventDate == d.Date, cancellationToken)) continue;
                var email = site.CustomerNotifyEmailEnabled == "true" && NotificationComposer.IsEmail(order.Customer?.Email) ? order.Customer!.Email!.Trim() : null;
                var phone = site.CustomerNotifyWhatsAppEnabled == "true" ? NotificationComposer.PhoneDigits(order.ContactPhone ?? order.Customer?.Phone) : null;
                if (email is null && phone is null) continue;
                order.TrackingToken ??= DocumentLinks.NewToken(); // bakım talebi bağlantısı
                if (email is not null) _db.NotificationJobs.Add(new NotificationJob { Channel = NotificationChannel.CustomerEmail, Recipient = email, Event = d.Event, EventDate = d.Date, ProductionOrderId = order.Id, NextAttemptUtc = nowUtc });
                if (phone is not null)
                {
                    var job = new NotificationJob { Channel = NotificationChannel.CustomerWhatsApp, Recipient = phone, Event = d.Event, EventDate = d.Date, ProductionOrderId = order.Id, NextAttemptUtc = nowUtc };
                    if (string.IsNullOrWhiteSpace(site.WhatsAppTemplateMaintenance)) (job.Status, job.LastError) = (NotificationStatus.Skipped, "Bakım hatırlatması için WhatsApp şablon adı yazılmamış (Site ayarları > Müşteri bildirimleri).");
                    _db.NotificationJobs.Add(job);
                }
                added++;
            }
        }
        if (added > 0) await _db.SaveChangesAsync(cancellationToken);
        return added;
    }

    // Gönderim anında: iş hâlâ teslim edilmiş ve tarih hâlâ bu işin takvimindeyse içerik bilgisi; değilse atlanır.
    public static async Task<CustomerNotifier.MaintenanceInfo> InfoAsync(AppDbContext db, NotificationJob job)
    {
        var order = job.ProductionOrder;
        if (order is not { Status: OrderStatus.Delivered, DeliveredOn: DateOnly delivered }) throw new SkipNotificationException("İş teslim edilmiş görünmüyor; gönderilmedi.");
        var years = await ServiceDesk.WarrantyYearsAsync(db, order);
        var match = Schedule(delivered, years).FirstOrDefault(d => d.Event == job.Event && d.Date == job.EventDate);
        if (match is null) throw new SkipNotificationException("Teslim tarihi veya garanti süresi değişti; gönderilmedi.");
        return new CustomerNotifier.MaintenanceInfo(match.Year, delivered.AddYears(years));
    }
}

// README 7.20: teslimden birkaç gün sonra Google yorum isteği. Açık eksiği (cam kırık, kol eksik) veya açık servis talebi olan,
// bildirim istemeyen müşteriye gönderilmez; her işe bir kez. Yorum bağlantısı yazılmamışsa hiç çalışmaz.
public sealed class ReviewRequests
{
    private const int WindowDays = 7;
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public ReviewRequests(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    public static int DelayDays(SiteSettings site) => int.TryParse(site.ReviewRequestDelayDays, out var days) && days is >= 1 and <= 60 ? days : 3;

    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var site = await _settings.GetAsync(cancellationToken);
        if (site.CustomerReviewRequestEnabled != "true" || string.IsNullOrWhiteSpace(site.GoogleReviewUrl)) return 0;
        var due = CustomerNotifier.Today(nowUtc).AddDays(-DelayDays(site));
        var from = due.AddDays(-WindowDays);
        var orders = await _db.ProductionOrders.Include(x => x.Customer)
            .Where(x => x.Status == OrderStatus.Delivered && x.DeliveredOn != null && x.DeliveredOn <= due && x.DeliveredOn > from && !x.Customer!.NotificationsOptOut)
            .Where(x => !_db.NotificationJobs.Any(j => j.ProductionOrderId == x.Id && j.Event == CustomerEvents.ReviewRequest))
            .ToListAsync(cancellationToken);
        var added = 0;
        foreach (var order in orders)
        {
            if (await OpenIssueAsync(_db, order.Id, cancellationToken) is not null) continue; // sorun giderilince sonraki taramada (pencere içindeyse) gönderilir
            var email = site.CustomerNotifyEmailEnabled == "true" && NotificationComposer.IsEmail(order.Customer?.Email) ? order.Customer!.Email!.Trim() : null;
            var phone = site.CustomerNotifyWhatsAppEnabled == "true" && !string.IsNullOrWhiteSpace(site.WhatsAppTemplateReview) ? NotificationComposer.PhoneDigits(order.ContactPhone ?? order.Customer?.Phone) : null;
            if (email is null && phone is null) continue;
            order.TrackingToken ??= DocumentLinks.NewToken();
            foreach (var (channel, to) in new[] { (NotificationChannel.CustomerEmail, email), (NotificationChannel.CustomerWhatsApp, phone) })
                if (to is not null) _db.NotificationJobs.Add(new NotificationJob { Channel = channel, Recipient = to, Event = CustomerEvents.ReviewRequest, EventDate = order.DeliveredOn, ProductionOrderId = order.Id, NextAttemptUtc = nowUtc });
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync(cancellationToken);
        return added;
    }

    // Gönderim anında da denetlenir: bu arada eksik veya servis talebi açıldıysa yorum istenmez.
    public static async Task<string?> OpenIssueAsync(AppDbContext db, int orderId, CancellationToken cancellationToken = default)
    {
        if (await db.PunchItems.AnyAsync(x => x.ProductionOrderId == orderId && (x.Status == PunchStatus.Open || x.Status == PunchStatus.Waiting), cancellationToken)) return "İşte açık eksik var; yorum istenmedi.";
        if (await db.ServiceRequests.AnyAsync(x => x.ProductionOrderId == orderId && (x.Status == ServiceStatus.Open || x.Status == ServiceStatus.Scheduled), cancellationToken)) return "İşte açık servis talebi var; yorum istenmedi.";
        return null;
    }
}
