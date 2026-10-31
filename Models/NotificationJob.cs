using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public enum NotificationChannel { AdminEmail = 0, CustomerEmail = 1, AdminWhatsApp = 2 }
public enum NotificationStatus { Pending = 0, Processing = 1, Sent = 2, Failed = 3, Skipped = 4 }

// Gönderim kuyruğu: talep ile aynı işlemde yazılır, arka planda gönderilir. İçerik saklanmaz, gönderirken talepten üretilir.
public sealed class NotificationJob
{
    public int Id { get; set; }
    public int ContactMessageId { get; set; }
    public ContactMessage? ContactMessage { get; set; }
    public NotificationChannel Channel { get; set; }
    [Required, StringLength(200)] public string Recipient { get; set; } = "";
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    [StringLength(200)] public string? ProviderMessageId { get; set; }
    [StringLength(500)] public string? LastError { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class NotificationLabels
{
    public static string Label(NotificationChannel channel) => channel switch { NotificationChannel.AdminEmail => "Ekip e-postası", NotificationChannel.CustomerEmail => "Müşteri onay e-postası", _ => "Ekip WhatsApp" };
    public static string Label(NotificationStatus status) => status switch { NotificationStatus.Pending => "Sırada", NotificationStatus.Processing => "Gönderiliyor", NotificationStatus.Sent => "Gönderildi", NotificationStatus.Failed => "Başarısız", _ => "Atlandı" };
}
