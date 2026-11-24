using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Models.Production;

public enum ServiceStatus { Open = 0, Scheduled = 1, Completed = 2, Cancelled = 3 }
public enum ServiceWarranty { Unknown = 0, InWarranty = 1, Paid = 2 }
public enum ServiceResult { Resolved = 0, NeedsRevisit = 1, NeedsPart = 2 }

public static class ServiceLabels
{
    public static readonly string[] Channels = { "Telefon", "WhatsApp", "E-posta", "Yerinde", "Web sitesi" };
    public static readonly string[] Categories = { "Ayar / kanat sarkması", "Donanım (kol, menteşe, kilit)", "Conta / sızdırmazlık / su alma", "Cam kırığı / buğulanma", "Panjur / motor", "Cam balkon", "Korkuluk", "Montaj kaynaklı", "Bakım", "Diğer" };

    public static string Label(ServiceStatus status) => status switch
    {
        ServiceStatus.Open => "Açık",
        ServiceStatus.Scheduled => "Ziyaret planlandı",
        ServiceStatus.Completed => "Tamamlandı",
        _ => "İptal edildi"
    };

    public static string Label(ServiceWarranty warranty) => warranty switch
    {
        ServiceWarranty.InWarranty => "Garanti kapsamında",
        ServiceWarranty.Paid => "Ücretli",
        _ => "Belirlenecek"
    };

    public static string Label(ServiceResult result) => result switch
    {
        ServiceResult.NeedsRevisit => "Tekrar ziyaret gerekli",
        ServiceResult.NeedsPart => "Parça bekleniyor",
        _ => "Sorun giderildi"
    };

    public static bool IsOpen(ServiceStatus status) => status is ServiceStatus.Open or ServiceStatus.Scheduled;
}

// Servis talebi: teslim edilmiş (bitmiş) işte müşterinin bildirdiği arıza veya bakım isteği.
public sealed class ServiceRequest
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Number { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public DateOnly ReceivedOn { get; set; }
    [StringLength(20)] public string Channel { get; set; } = "Telefon";
    [StringLength(60)] public string Category { get; set; } = "Diğer";
    public bool Urgent { get; set; }
    [StringLength(120)] public string? ContactName { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(500)] public string? PozIds { get; set; } // etkilenen pozlar: virgülle ayrılmış WindowDesign kimlikleri
    [Required(ErrorMessage = "Müşterinin bildirdiği sorunu yazın."), StringLength(2000)] public string Complaint { get; set; } = "";
    public ServiceWarranty Warranty { get; set; }
    public ServiceStatus Status { get; set; } = ServiceStatus.Open;
    public DateOnly? PlannedOn { get; set; }
    public TimeOnly? PlannedAt { get; set; } // ziyaret saati: müşteriye bildirilir
    public int? InstallerId { get; set; }
    public Installer? Installer { get; set; }
    public DateOnly? CompletedOn { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ServiceVisit> Visits { get; set; } = new();
}

// Servis formu: bir ziyarette yapılan tespit ve işlem, kullanılan parçalar ve ücret. Basılıp müşteriye imzalatılır.
public sealed class ServiceVisit
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Number { get; set; } = "";
    public int ServiceRequestId { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? ArrivedAt { get; set; }
    public TimeOnly? LeftAt { get; set; }
    [StringLength(200)] public string? Technicians { get; set; }
    [StringLength(2000)] public string? Findings { get; set; }
    [Required(ErrorMessage = "Yapılan işlemi yazın."), StringLength(2000)] public string WorkDone { get; set; } = "";
    public ServiceResult Result { get; set; }
    [Column(TypeName = "numeric(18,2)"), Range(0, 10_000_000)] public decimal LaborCharge { get; set; } // KDV hariç işçilik ücreti; garantide 0
    [StringLength(1000)] public string? CustomerNote { get; set; }
    public int? InvoiceId { get; set; } // ücretli servisten açılan satış faturası
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ServicePart> Parts { get; set; } = new();
}

public sealed class ServicePart
{
    public int Id { get; set; }
    public int ServiceVisitId { get; set; }
    public ServiceVisit? ServiceVisit { get; set; }
    public int SortOrder { get; set; }
    public int? ProductId { get; set; } // stok kartından seçilen parça ziyarette stoktan düşer
    [Required, StringLength(200)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; } = 1;
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Column(TypeName = "numeric(18,2)")] public decimal UnitPrice { get; set; } // KDV hariç; garantide 0
}

public sealed class ServiceRequestInputVm
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? ProductionOrderId { get; set; }
    public DateOnly ReceivedOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    [StringLength(20)] public string Channel { get; set; } = "Telefon";
    [StringLength(60)] public string Category { get; set; } = "Diğer";
    public bool Urgent { get; set; }
    [StringLength(120)] public string? ContactName { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public List<int> PozIds { get; set; } = new();
    [Required(ErrorMessage = "Müşterinin bildirdiği sorunu yazın."), StringLength(2000)] public string Complaint { get; set; } = "";
    public ServiceWarranty Warranty { get; set; }
    public DateOnly? PlannedOn { get; set; }
    public TimeOnly? PlannedAt { get; set; }
    public int? InstallerId { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}

public sealed class ServiceVisitInputVm
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    public TimeOnly? ArrivedAt { get; set; }
    public TimeOnly? LeftAt { get; set; }
    [StringLength(200)] public string? Technicians { get; set; }
    [StringLength(2000)] public string? Findings { get; set; }
    [Required(ErrorMessage = "Yapılan işlemi yazın."), StringLength(2000)] public string WorkDone { get; set; } = "";
    public ServiceResult Result { get; set; }
    [Range(0, 10_000_000, ErrorMessage = "Ücret 0 veya daha büyük olmalıdır.")] public decimal LaborCharge { get; set; }
    [StringLength(1000)] public string? CustomerNote { get; set; }
    public List<ServicePartInput> Parts { get; set; } = new();
}

public sealed class ServicePartInput
{
    public int? ProductId { get; set; }
    [StringLength(200)] public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    [StringLength(20)] public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
}
