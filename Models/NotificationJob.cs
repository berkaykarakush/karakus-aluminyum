using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public enum NotificationChannel { AdminEmail = 0, CustomerEmail = 1, AdminWhatsApp = 2, CustomerWhatsApp = 3 }
public enum NotificationStatus { Pending = 0, Processing = 1, Sent = 2, Failed = 3, Skipped = 4 }

// Gönderim kuyruğu: talep (veya sipariş/servis değişikliği) ile aynı işlemde yazılır, arka planda gönderilir.
// İçerik saklanmaz, gönderirken kaynaktan üretilir; kaynak değiştiyse (tarih kaydı, iptal) gönderilmez.
public sealed class NotificationJob
{
    public int Id { get; set; }
    public int? ContactMessageId { get; set; }
    public ContactMessage? ContactMessage { get; set; }
    // Müşteri bildirimleri (README 7.6): sipariş veya servis talebi ve olayı
    public int? ProductionOrderId { get; set; }
    public KarakusAluminyum.Web.Models.Production.ProductionOrder? ProductionOrder { get; set; }
    public int? ServiceRequestId { get; set; }
    public KarakusAluminyum.Web.Models.Production.ServiceRequest? ServiceRequest { get; set; }
    [StringLength(30)] public string? Event { get; set; } // CustomerEvents
    public DateOnly? EventDate { get; set; } // bildirilen tarih; gönderirken kayıttaki tarihle aynı değilse atlanır
    public TimeOnly? EventTime { get; set; }
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
    public static string Label(NotificationChannel channel) => channel switch { NotificationChannel.AdminEmail => "Ekip e-postası", NotificationChannel.CustomerEmail => "Müşteri e-postası", NotificationChannel.CustomerWhatsApp => "Müşteri WhatsApp", _ => "Ekip WhatsApp" };
    public static string Label(NotificationStatus status) => status switch { NotificationStatus.Pending => "Sırada", NotificationStatus.Processing => "Gönderiliyor", NotificationStatus.Sent => "Gönderildi", NotificationStatus.Failed => "Başarısız", _ => "Atlandı" };
}

// Müşteriye giden otomatik bildirimlerin olayları.
public static class CustomerEvents
{
    public const string Production = "order.production";   // sipariş üretime girdi
    public const string ShipDate = "order.shipDate";       // sevk tarihi belirlendi / değişti
    public const string Installation = "order.install";    // montajdan bir gün önce hatırlatma
    public const string ServiceVisit = "service.visit";    // servis ziyareti günü ve saati
    public static readonly string[] All = { Production, ShipDate, Installation, ServiceVisit };
    // Ekibe: müşteri takip sayfasından servis talebi açtı (README 7.7)
    public const string ServiceRequested = "service.request";

    public static string Label(string? e) => e switch
    {
        Production => "Üretime girdi",
        ShipDate => "Sevk tarihi",
        Installation => "Montaj hatırlatması",
        ServiceVisit => "Servis ziyareti",
        ServiceRequested => "Web servis talebi",
        _ => "Teklif talebi"
    };
}
