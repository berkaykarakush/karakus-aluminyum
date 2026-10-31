using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services.Notifications;

// Gönderici uyandırma işareti; kaçırılsa bile düzenli tur kuyruğu tarar.
public sealed class NotificationSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public void Wake() { if (_signal.CurrentCount == 0) { try { _signal.Release(); } catch (SemaphoreFullException) { } } }
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

public sealed class NotificationQueue
{
    public const int HourlyLimit = 60;
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    private readonly NotificationOptions _options;
    public NotificationQueue(AppDbContext db, SiteSettingsProvider settings, IOptions<NotificationOptions> options) { _db = db; _settings = settings; _options = options.Value; }

    private static NotificationJob Skipped(ContactMessage message, NotificationChannel channel, string recipient, string reason) => new()
    { ContactMessage = message, Channel = channel, Recipient = recipient.Length > 200 ? recipient[..200] : recipient, Status = NotificationStatus.Skipped, LastError = reason };

    // Talep kaydedilirken çağrılır: talep ve bildirim kaydı aynı işlemde yazılır, biri olmadan diğeri kalmaz.
    public async Task AddForAsync(ContactMessage message)
    {
        var site = await _settings.GetAsync();
        var now = DateTime.UtcNow;
        // Toplu sahte form gönderiminde e-posta kotası ve WhatsApp ücreti korunur; talepler yine panele düşer.
        var limited = await _db.NotificationJobs.CountAsync(x => x.CreatedUtc > now.AddHours(-1) && x.Status != NotificationStatus.Skipped) >= HourlyLimit;

        void Add(NotificationChannel channel, string recipient)
            => _db.NotificationJobs.Add(limited ? Skipped(message, channel, recipient, "Saatlik bildirim sınırı aşıldı; talep panelde.") : new NotificationJob { ContactMessage = message, Channel = channel, Recipient = recipient });

        if (site.NotifyEmailEnabled == "true")
            foreach (var to in NotificationComposer.Split(string.IsNullOrWhiteSpace(site.NotifyEmailTo) ? _options.Smtp.To : site.NotifyEmailTo)) Add(NotificationChannel.AdminEmail, to);
        if (site.NotifyWhatsAppEnabled == "true")
            foreach (var to in NotificationComposer.Split(string.IsNullOrWhiteSpace(site.NotifyWhatsAppTo) ? _options.WhatsApp.To : site.NotifyWhatsAppTo)) Add(NotificationChannel.AdminWhatsApp, to);

        var customer = message.ContactInfo.Trim();
        if (site.NotifyCustomerEmailEnabled == "true" && NotificationComposer.IsEmail(customer))
        {
            // Aynı adrese günde en fazla bir onay: form bir başkasını rahatsız etmek için kullanılamaz.
            var lower = customer.ToLowerInvariant();
            var recent = await _db.NotificationJobs.AnyAsync(x => x.Channel == NotificationChannel.CustomerEmail && x.Recipient.ToLower() == lower && x.CreatedUtc > now.AddDays(-1) && x.Status != NotificationStatus.Skipped);
            if (recent) _db.NotificationJobs.Add(Skipped(message, NotificationChannel.CustomerEmail, customer, "Bu adrese son 24 saatte onay gönderildi."));
            else Add(NotificationChannel.CustomerEmail, customer);
        }
    }
}

public sealed class NotificationDispatcher
{
    public const int MaxAttempts = 6;
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6) };
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    private readonly IEmailSender _email;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<NotificationDispatcher> _logger;
    public NotificationDispatcher(AppDbContext db, SiteSettingsProvider settings, IEmailSender email, IWhatsAppSender whatsApp, ILogger<NotificationDispatcher> logger) { _db = db; _settings = settings; _email = email; _whatsApp = whatsApp; _logger = logger; }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Gönderim sırasında kapanan uygulamanın yarım bıraktıkları yeniden sıraya girer.
        await _db.NotificationJobs.Where(x => x.Status == NotificationStatus.Processing && x.ClaimedUtc < now.AddMinutes(-5))
            .ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, NotificationStatus.Pending), cancellationToken);
        // Alıcı adresi kişisel veridir: tamamlanan kayıtlar 90 gün sonra silinir.
        await _db.NotificationJobs.Where(x => x.CreatedUtc < now.AddDays(-90) && x.Status != NotificationStatus.Pending && x.Status != NotificationStatus.Processing).ExecuteDeleteAsync(cancellationToken);

        var ids = await _db.NotificationJobs.Where(x => x.Status == NotificationStatus.Pending && x.NextAttemptUtc <= now).OrderBy(x => x.NextAttemptUtc).Select(x => x.Id).Take(20).ToListAsync(cancellationToken);
        var site = await _settings.GetAsync(cancellationToken);
        var sent = 0;
        foreach (var id in ids)
        {
            // Aynı işi iki gönderici almasın: durum koşullu güncellenir.
            var claimed = await _db.NotificationJobs.Where(x => x.Id == id && x.Status == NotificationStatus.Pending)
                .ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, NotificationStatus.Processing).SetProperty(j => j.ClaimedUtc, now), cancellationToken);
            if (claimed == 0) continue;

            var job = await _db.NotificationJobs.Include(x => x.ContactMessage).SingleAsync(x => x.Id == id, cancellationToken);
            job.Attempts++;
            try
            {
                job.ProviderMessageId = job.Channel switch
                {
                    NotificationChannel.AdminEmail => await _email.SendAsync(NotificationComposer.AdminEmail(job.ContactMessage!, job.Recipient, site), cancellationToken),
                    NotificationChannel.CustomerEmail => await _email.SendAsync(NotificationComposer.CustomerEmail(job.Recipient, site), cancellationToken),
                    _ => await _whatsApp.SendAsync(NotificationComposer.AdminWhatsApp(job.ContactMessage!, job.Recipient, site), cancellationToken)
                };
                job.Status = NotificationStatus.Sent;
                job.SentUtc = DateTime.UtcNow;
                job.LastError = null;
                sent++;
            }
            catch (PermanentNotificationException error) { Fail(job, error.Message); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                job.Status = NotificationStatus.Pending;
                job.Attempts--;
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception error)
            {
                var message = error is NotificationNotConfiguredException ? error.Message : Trim(error.Message);
                if (job.Attempts >= MaxAttempts) Fail(job, message);
                else
                {
                    job.Status = NotificationStatus.Pending;
                    job.LastError = message;
                    job.NextAttemptUtc = DateTime.UtcNow + Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)];
                }
                _logger.LogWarning("Bildirim gönderilemedi: {Id} {Channel} deneme {Attempt}: {Error}", job.Id, job.Channel, job.Attempts, message);
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        return sent;
    }

    private void Fail(NotificationJob job, string message)
    {
        job.Status = NotificationStatus.Failed;
        job.LastError = Trim(message);
        _logger.LogError("Bildirim kalıcı olarak başarısız: {Id} {Channel}: {Error}", job.Id, job.Channel, job.LastError);
    }

    private static string Trim(string message) => message.Length > 500 ? message[..500] : message;
}

public sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly NotificationSignal _signal;
    private readonly ILogger<NotificationWorker> _logger;
    public NotificationWorker(IServiceScopeFactory scopes, NotificationSignal signal, ILogger<NotificationWorker> logger) { _scopes = scopes; _signal = signal; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(stoppingToken);
                await _signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                _logger.LogError(error, "Bildirim kuyruğu turu başarısız; sonraki turda devam edilecek.");
                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}
