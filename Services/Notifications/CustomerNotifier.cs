using System.Globalization;
using System.Net;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Notifications;

// README 7.6: siparişte ve serviste müşteriyi ilgilendiren değişiklik olunca e-posta / WhatsApp bildirimi sıraya alınır.
// Kayıt ile aynı SaveChanges içinde yazılır. Gönderirken kaynak yeniden denetlenir: tarih değiştiyse veya iş iptal edildiyse gitmez.
public sealed record OrderSnapshot(OrderStatus Status, DateOnly? ShipDate, DateOnly? InstallDate)
{
    public static OrderSnapshot Of(ProductionOrder order) => new(order.Status, order.PlannedShipDate, order.InstallationDate);
}

public sealed record ServiceSnapshot(ServiceStatus Status, DateOnly? Date, TimeOnly? Time)
{
    public static ServiceSnapshot Of(ServiceRequest request) => new(request.Status, request.PlannedOn, request.PlannedAt);
    public static readonly ServiceSnapshot None = new(ServiceStatus.Open, null, null);
}

// Sıraya alınacak bir olay: ne zaman ve hangi tarih için.
public sealed record PlannedNotice(string Event, DateOnly? Date, TimeOnly? Time, DateTime SendAtUtc);

public sealed class SkipNotificationException(string message) : Exception(message);

public sealed class CustomerNotifier
{
    // Kısa bekleme: yanlış girilip hemen düzeltilen tarih müşteriye gitmez, yalnızca sonuncusu gider.
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);
    private static readonly OrderStatus[] ShipStatuses = { OrderStatus.Approved, OrderStatus.InProduction, OrderStatus.Warehouse };

    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public CustomerNotifier(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    public static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(nowUtc + TurkeyOffset);

    // Montaj hatırlatması: montajdan önceki akşam 18:00 (Türkiye). O saat geçtiyse (montaj bugün/yarın) kısa beklemeyle hemen.
    public static DateTime ReminderUtc(DateOnly installDate, DateTime nowUtc)
    {
        var at = installDate.AddDays(-1).ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc) - TurkeyOffset;
        return at < nowUtc + Delay ? nowUtc + Delay : at;
    }

    // Siparişin önceki ve sonraki hâlinden müşteriye bildirilecek olaylar.
    public static List<PlannedNotice> PlanOrder(OrderSnapshot before, ProductionOrder order, DateTime nowUtc)
    {
        var list = new List<PlannedNotice>();
        var today = Today(nowUtc);
        if (order.Status == OrderStatus.Cancelled) return list;
        var production = before.Status < OrderStatus.InProduction && order.Status == OrderStatus.InProduction;
        if (production) list.Add(new(CustomerEvents.Production, order.PlannedShipDate, null, nowUtc + Delay));
        // Sevk tarihi: onaylı, sevk edilmemiş işte tarih değişince veya iş onaylanınca (üretim bildirimi tarihi zaten içerir)
        if (!production && order.PlannedShipDate is DateOnly ship && ship >= today && ShipStatuses.Contains(order.Status)
            && (ship != before.ShipDate || !ShipStatuses.Contains(before.Status)))
            list.Add(new(CustomerEvents.ShipDate, ship, null, nowUtc + Delay));
        if (order.InstallationDate is DateOnly install && install != before.InstallDate && install >= today && !order.InstallationExcluded && order.Status != OrderStatus.Delivered)
            list.Add(new(CustomerEvents.Installation, install, null, ReminderUtc(install, nowUtc)));
        return list;
    }

    public static List<PlannedNotice> PlanService(ServiceSnapshot before, ServiceRequest request, DateTime nowUtc)
    {
        if (request.Status != ServiceStatus.Scheduled || request.PlannedOn is not DateOnly date || date < Today(nowUtc)) return new();
        if (before.Status == ServiceStatus.Scheduled && before.Date == date && before.Time == request.PlannedAt) return new();
        return new() { new(CustomerEvents.ServiceVisit, date, request.PlannedAt, nowUtc + Delay) };
    }

    public async Task OrderChangedAsync(ProductionOrder order, OrderSnapshot before, DateTime? now = null)
    {
        var nowUtc = now ?? DateTime.UtcNow;
        // Değişen olayın sıradaki eski bildirimi artık geçersiz (tarih değişti, iş iptal edildi).
        var changed = new List<string>();
        if (order.Status == OrderStatus.Cancelled && before.Status != OrderStatus.Cancelled) changed.AddRange(CustomerEvents.All);
        if (order.PlannedShipDate != before.ShipDate) changed.Add(CustomerEvents.ShipDate);
        if (order.InstallationDate != before.InstallDate) changed.Add(CustomerEvents.Installation);
        var planned = PlanOrder(before, order, nowUtc);
        if (changed.Count == 0 && planned.Count == 0) return;
        await SupersedeAsync(x => x.ProductionOrderId == order.Id, changed.Concat(planned.Select(p => p.Event)).Distinct().ToList(), planned);
        if (planned.Count == 0) return;
        var customer = order.Customer ?? await _db.Customers.FirstOrDefaultAsync(x => x.Id == order.CustomerId);
        order.TrackingToken ??= KarakusAluminyum.Web.Services.Documents.DocumentLinks.NewToken(); // e-postadaki takip bağlantısı
        await AddAsync(planned, customer, order.ContactPhone, job => job.ProductionOrderId = order.Id, nowUtc);
    }

    public async Task ServiceChangedAsync(ServiceRequest request, ServiceSnapshot before, DateTime? now = null)
    {
        var nowUtc = now ?? DateTime.UtcNow;
        var planned = PlanService(before, request, nowUtc);
        var changed = before.Date != request.PlannedOn || before.Time != request.PlannedAt || request.Status != ServiceStatus.Scheduled;
        if (!changed && planned.Count == 0) return;
        if (request.Id != 0) await SupersedeAsync(x => x.ServiceRequestId == request.Id, new List<string> { CustomerEvents.ServiceVisit }, planned);
        if (planned.Count == 0) return;
        var customer = request.Customer ?? await _db.Customers.FirstOrDefaultAsync(x => x.Id == request.CustomerId);
        await AddAsync(planned, customer, request.ContactPhone, job => job.ServiceRequest = request, nowUtc);
    }

    private async Task SupersedeAsync(System.Linq.Expressions.Expression<Func<NotificationJob, bool>> owner, List<string> events, List<PlannedNotice> planned)
    {
        var pending = await _db.NotificationJobs.Where(owner).Where(x => x.Status == NotificationStatus.Pending && x.Event != null && events.Contains(x.Event)).ToListAsync();
        foreach (var job in pending)
        {
            // Aynı tarih yeniden planlandıysa eski kayıt kalır (AddAsync yinelenmeyi önler).
            if (planned.Any(p => p.Event == job.Event && p.Date == job.EventDate && p.Time == job.EventTime)) continue;
            job.Status = NotificationStatus.Skipped;
            job.LastError = "Tarih veya durum değişti; gönderilmedi.";
        }
    }

    private async Task AddAsync(List<PlannedNotice> planned, Customer? customer, string? phone, Action<NotificationJob> link, DateTime nowUtc)
    {
        if (customer is null || customer.NotificationsOptOut) return;
        var site = await _settings.GetAsync();
        var email = site.CustomerNotifyEmailEnabled == "true" && NotificationComposer.IsEmail(customer.Email) ? customer.Email!.Trim() : null;
        var whatsApp = site.CustomerNotifyWhatsAppEnabled == "true" ? NotificationComposer.PhoneDigits(string.IsNullOrWhiteSpace(phone) ? customer.Phone : phone) : null;
        foreach (var notice in planned.Where(p => Enabled(site, p.Event)))
        {
            foreach (var (channel, recipient) in new[] { (NotificationChannel.CustomerEmail, email), (NotificationChannel.CustomerWhatsApp, whatsApp) })
            {
                if (recipient is null) continue;
                var job = new NotificationJob { Channel = channel, Recipient = recipient, Event = notice.Event, EventDate = notice.Date, EventTime = notice.Time, NextAttemptUtc = notice.SendAtUtc };
                link(job);
                // Aynı olay ve tarih bu alıcıya zaten gittiyse veya sıradaysa yinelenmez.
                if (await DuplicateAsync(job)) continue;
                if (channel == NotificationChannel.CustomerWhatsApp && string.IsNullOrWhiteSpace(TemplateFor(site, notice.Event)))
                    (job.Status, job.LastError) = (NotificationStatus.Skipped, "Bu olay için WhatsApp şablon adı yazılmamış (Site ayarları > Müşteri bildirimleri).");
                _db.NotificationJobs.Add(job);
            }
        }
    }

    private Task<bool> DuplicateAsync(NotificationJob job)
    {
        var tracked = _db.ChangeTracker.Entries<NotificationJob>().Select(e => e.Entity)
            .Any(x => x != job && Same(x, job) && x.Status is NotificationStatus.Pending or NotificationStatus.Sent);
        if (tracked) return Task.FromResult(true);
        int? orderId = job.ProductionOrderId, serviceId = job.ServiceRequest?.Id is int id && id != 0 ? id : null;
        if (orderId is null && serviceId is null) return Task.FromResult(false); // yeni kayıt: veritabanında eşi olamaz
        var (e, channel, recipient, date, time) = (job.Event, job.Channel, job.Recipient, job.EventDate, job.EventTime);
        return _db.NotificationJobs.AnyAsync(x => x.Event == e && x.Channel == channel && x.Recipient == recipient && x.EventDate == date && x.EventTime == time
            && (orderId != null ? x.ProductionOrderId == orderId : x.ServiceRequestId == serviceId)
            && (x.Status == NotificationStatus.Pending || x.Status == NotificationStatus.Sent || x.Status == NotificationStatus.Processing));
    }

    private static bool Same(NotificationJob a, NotificationJob b) => a.Event == b.Event && a.Channel == b.Channel && a.Recipient == b.Recipient && a.EventDate == b.EventDate && a.EventTime == b.EventTime
        && a.ProductionOrderId == b.ProductionOrderId && ReferenceEquals(a.ServiceRequest, b.ServiceRequest) && a.ServiceRequestId == b.ServiceRequestId;

    public static bool Enabled(SiteSettings site, string? e) => e switch
    {
        CustomerEvents.Production => site.CustomerNotifyProduction == "true",
        CustomerEvents.ShipDate => site.CustomerNotifyShipDate == "true",
        CustomerEvents.Installation => site.CustomerNotifyInstallation == "true",
        CustomerEvents.ServiceVisit => site.CustomerNotifyServiceVisit == "true",
        _ => false
    };

    public static string TemplateFor(SiteSettings site, string? e) => e switch
    {
        CustomerEvents.Production => site.WhatsAppTemplateProduction,
        CustomerEvents.ShipDate => site.WhatsAppTemplateShipDate,
        CustomerEvents.Installation => site.WhatsAppTemplateInstallation,
        CustomerEvents.ServiceVisit => site.WhatsAppTemplateServiceVisit,
        _ => ""
    };

    // Gönderim anında: kayıt hâlâ bu bildirimi gerektiriyor mu? Gerektirmiyorsa nedeni döner.
    public static string? Obsolete(NotificationJob job, DateTime nowUtc)
    {
        var today = Today(nowUtc);
        if (job.Event == CustomerEvents.ServiceRequested) return job.ServiceRequest is null ? "Servis talebi silindi." : null;
        if (job.ServiceRequestId is not null)
        {
            var r = job.ServiceRequest;
            if (r is null) return "Servis talebi bulunamadı.";
            if (r.Customer?.NotificationsOptOut == true) return "Müşteri bildirim istemiyor.";
            if (r.Status != ServiceStatus.Scheduled || r.PlannedOn != job.EventDate || r.PlannedAt != job.EventTime) return "Servis ziyareti değişti; gönderilmedi.";
            return job.EventDate < today ? "Ziyaret günü geçti." : null;
        }
        var o = job.ProductionOrder;
        if (o is null) return "Sipariş bulunamadı.";
        if (o.Customer?.NotificationsOptOut == true) return "Müşteri bildirim istemiyor.";
        if (o.Status == OrderStatus.Cancelled) return "Sipariş iptal edildi.";
        return job.Event switch
        {
            CustomerEvents.ShipDate when o.PlannedShipDate != job.EventDate || o.Status >= OrderStatus.Shipped => "Sevk tarihi değişti veya sipariş sevk edildi.",
            CustomerEvents.Installation when o.InstallationDate != job.EventDate || o.InstallationExcluded || o.Status == OrderStatus.Delivered => "Montaj tarihi değişti.",
            CustomerEvents.Installation when job.EventDate < today => "Montaj günü geçti.",
            _ => null
        };
    }

    // ---- İçerik ----

    private static readonly CultureInfo Tr = new("tr-TR");
    public static string DateText(DateOnly? date, TimeOnly? time = null)
        => date is DateOnly d ? d.ToString("d MMMM yyyy dddd", Tr) + (time is TimeOnly t ? $", saat {t:HH\\:mm}" : "") : "henüz belirlenmedi";

    public sealed record CustomerMessage(string Subject, string Name, string Number, string Body, string DateText, string Extra);

    public static CustomerMessage Compose(NotificationJob job, SiteSettings site)
    {
        if (job.ServiceRequest is { } r)
        {
            var name = Name(r.ContactName, r.Customer);
            var when = DateText(r.PlannedOn, r.PlannedAt);
            var address = string.IsNullOrWhiteSpace(r.Address) ? "kayıtlı adresiniz" : r.Address.Trim();
            return new($"Servis ziyaretiniz planlandı | {site.SiteName}", name, r.Number,
                $"{r.Number} numaralı servis talebiniz için ziyaretimiz {when} olarak planlandı. Adres: {address}.", when, address);
        }
        var o = job.ProductionOrder!;
        var customer = Name(o.Customer?.ContactPerson, o.Customer);
        return job.Event switch
        {
            CustomerEvents.Production => new($"Siparişiniz üretime alındı | {site.SiteName}", customer, o.OrderNumber,
                $"{o.OrderNumber} numaralı \"{o.Title}\" siparişiniz üretime alındı." + (o.PlannedShipDate is null ? "" : $" Planlanan sevk tarihi: {DateText(o.PlannedShipDate)}."), DateText(o.PlannedShipDate), o.Title),
            CustomerEvents.ShipDate => new($"Sevk tarihiniz: {DateText(o.PlannedShipDate)} | {site.SiteName}", customer, o.OrderNumber,
                $"{o.OrderNumber} numaralı \"{o.Title}\" siparişinizin sevk tarihi {DateText(o.PlannedShipDate)} olarak planlandı.", DateText(o.PlannedShipDate), o.Title),
            _ => Installation(o, customer, site)
        };
    }

    private static CustomerMessage Installation(ProductionOrder o, string customer, SiteSettings site)
    {
        var address = !string.IsNullOrWhiteSpace(o.InstallAddress) ? o.InstallAddress.Trim() : KarakusAluminyum.Web.Services.Production.OrderService.Location(o.Customer) is { Length: > 0 } a ? a : "kayıtlı adresiniz";
        var when = DateText(o.InstallationDate);
        return new($"Montaj hatırlatması: {when} | {site.SiteName}", customer, o.OrderNumber,
            $"{o.OrderNumber} numaralı \"{o.Title}\" siparişinizin montajı {when} günü {address} adresinde yapılacaktır. Montaj alanının ulaşılabilir olmasını rica ederiz.", when, address);
    }

    private static string Name(string? contact, Customer? customer) => !string.IsNullOrWhiteSpace(contact) ? contact.Trim() : customer?.Name ?? "Değerli müşterimiz";

    public static OutgoingEmail Email(NotificationJob job, SiteSettings site)
    {
        var m = Compose(job, site);
        string H(string v) => WebUtility.HtmlEncode(v);
        var footer = $"Sorularınız için {site.PhoneText} numarasından veya {site.Email} adresinden bize ulaşabilirsiniz.";
        var token = job.ProductionOrder?.TrackingToken ?? job.ServiceRequest?.ProductionOrder?.TrackingToken;
        var track = token is null ? null : KarakusAluminyum.Web.Services.Documents.DocumentLinks.TrackingUrl(site.BaseUrl, token);
        var trackText = track is null ? "" : $"\n\nSiparişinizin durumunu görmek veya servis talebi açmak için: {track}";
        var text = $"Merhaba {m.Name},\n\n{m.Body}{trackText}\n\n{footer}\n\n{site.SiteName}\n{site.BaseUrl}\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#111;font-size:15px;line-height:1.6">
              <p>Merhaba {H(m.Name)},</p>
              <p>{H(m.Body)}</p>
              {(track is null ? "" : $"<p><a href=\"{H(track)}\" style=\"background:#2c5f7c;color:#fff;padding:10px 16px;border-radius:7px;text-decoration:none\">Siparişimi takip et</a></p>")}
              <p>{H(footer)}</p>
              <p><strong>{H(site.SiteName)}</strong><br /><a href="{H(site.BaseUrl)}">{H(site.BaseUrl)}</a></p>
            </div>
            """;
        return new OutgoingEmail(job.Recipient, m.Subject.Length > 150 ? m.Subject[..150] : m.Subject, text, html, NotificationComposer.IsEmail(site.Email) ? site.Email : null);
    }

    public static OutgoingWhatsApp WhatsApp(NotificationJob job, SiteSettings site)
    {
        var m = Compose(job, site);
        var template = TemplateFor(site, job.Event);
        if (string.IsNullOrWhiteSpace(template)) throw new PermanentNotificationException("Bu olay için WhatsApp şablon adı yazılmamış.");
        return new OutgoingWhatsApp(job.Recipient, template, string.IsNullOrWhiteSpace(site.WhatsAppTemplateLanguage) ? "tr" : site.WhatsAppTemplateLanguage,
            new[] { m.Name, m.Number, m.DateText, m.Extra }, $"Merhaba {m.Name}, {m.Body}");
    }

    // Ekibe: müşteri takip sayfasından servis talebi geldi.
    public static OutgoingEmail TeamServiceEmail(NotificationJob job, SiteSettings site)
    {
        var r = job.ServiceRequest!;
        var panel = $"{site.BaseUrl.TrimEnd('/')}/yonetim/servis/detay/{r.Id}";
        string H(string? v) => WebUtility.HtmlEncode(v ?? "-");
        var subject = $"Web servis talebi: {r.Number} - {r.Customer?.Name}";
        var text = $"Müşteri takip sayfasından servis talebi açıldı.\n\nNo: {r.Number}\nMüşteri: {r.Customer?.Name}\nSipariş: {r.ProductionOrder?.OrderNumber}\nİlgili kişi: {r.ContactName}\nTelefon: {r.ContactPhone}\nKonu: {r.Category}\n\n{r.Complaint}\n\nPanelde görüntüle: {panel}\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:620px;margin:auto;font-size:14px;color:#111">
              <h2 style="font-size:17px">Web servis talebi {H(r.Number)}</h2>
              <p><strong>Müşteri:</strong> {H(r.Customer?.Name)}<br /><strong>Sipariş:</strong> {H(r.ProductionOrder?.OrderNumber)}<br /><strong>İlgili kişi:</strong> {H(r.ContactName)}<br /><strong>Telefon:</strong> {H(r.ContactPhone)}<br /><strong>Konu:</strong> {H(r.Category)}</p>
              <p style="white-space:pre-wrap">{H(r.Complaint)}</p>
              <p><a href="{H(panel)}" style="background:#2c5f7c;color:#fff;padding:10px 16px;border-radius:7px;text-decoration:none">Panelde görüntüle</a></p>
            </div>
            """;
        return new OutgoingEmail(job.Recipient, subject.Length > 150 ? subject[..150] : subject, text, html, null);
    }
}
