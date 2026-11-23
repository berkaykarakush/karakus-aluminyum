using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Accounting;

namespace KarakusAluminyum.Web.Models.Production;

// Personel takvimi (puantaj): hangi personel hangi gün hangi işte çalıştı / çalışacak. İleri tarihli kayıt plandır.
public sealed class WorkLog
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    // Kayıt ya bir personele ya da bir montaj ekibine (taşeron dahil) yazılır.
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int? InstallerId { get; set; }
    public Installer? Installer { get; set; }
    public int People { get; set; } = 1; // ekipte kayıt anındaki kişi sayısı; adam-gün = Days × People
    public int? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public int? ServiceRequestId { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }
    [StringLength(20)] public string Activity { get; set; } = WorkActivities.Installation;
    [Column(TypeName = "numeric(4,2)")] public decimal Days { get; set; } = 1; // 1 tam gün, 0,5 yarım gün
    [StringLength(300)] public string? Note { get; set; }
    [StringLength(160)] public string? CreatedBy { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class WorkActivities
{
    public const string Production = "production";
    public const string Installation = "installation";
    public const string Service = "service";
    public const string Survey = "survey";
    public const string Workshop = "workshop";
    public const string Other = "other";
    public static readonly string[] All = { Installation, Production, Service, Survey, Workshop, Other };

    public static string Label(string activity) => activity switch
    {
        Production => "Üretim",
        Installation => "Montaj",
        Service => "Servis",
        Survey => "Keşif / ölçü",
        Workshop => "Atölye (genel)",
        _ => "Diğer"
    };
}

public sealed class WorkLogInputVm
{
    public List<int> EmployeeIds { get; set; } = new();
    public List<int> InstallerIds { get; set; } = new();
    public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    public DateOnly? To { get; set; }
    public bool SkipSundays { get; set; } = true;
    public int? ProductionOrderId { get; set; }
    public int? ServiceRequestId { get; set; }
    public string Activity { get; set; } = WorkActivities.Installation;
    public decimal Days { get; set; } = 1;
    [StringLength(300)] public string? Note { get; set; }
    public string? ReturnWeek { get; set; }
}
