using System.ComponentModel.DataAnnotations;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Models.Production;

// README madde 8: işin cam, korkuluk ve doğrama listelerindeki eksik ve kusurlar ("cam kırıldı", "kapı kolu eksik").
// Her kayıt bir siparişe (işe) bağlıdır; istenirse çizim listesindeki pozu da gösterir.
public enum PunchStatus { Open = 0, Waiting = 1, Resolved = 2, Cancelled = 3 }

public static class PunchLabels
{
    // Tür, eksiğin hangi listeyle ilgili olduğunu da söyler (cam listesi, korkuluk, doğrama).
    public static readonly string[] Kinds =
    {
        "Cam kırık / çatlak", "Cam çizik / lekeli", "Cam eksik", "Cam ölçüsü hatalı",
        "Kol / kilit eksik", "Menteşe / makas eksik", "Aksesuar eksik (kapak, tapa, vida)", "Conta / fitil eksik",
        "Profil hasarlı / çizik", "Profil eksik", "Ölçü hatası (doğrama)",
        "Korkuluk parçası eksik", "Korkuluk hasarlı",
        "Panjur / motor arızası", "Montaj eksik (silikon, köpük, ayar)", "Diğer"
    };

    // Eksiğin fark edildiği yer.
    public static readonly string[] Stages = { "Üretim", "Sevkiyat / yükleme", "Montaj", "Teslim", "Servis" };

    // Gidermekten (veya bedelinden) kim sorumlu.
    public static readonly string[] Parties = { "Atölye", "Camcı", "Tedarikçi", "Montaj ekibi", "Nakliye", "Müşteri", "Belirsiz" };

    public static string Label(PunchStatus status) => status switch
    {
        PunchStatus.Open => "Açık",
        PunchStatus.Waiting => "Parça / cam bekleniyor",
        PunchStatus.Resolved => "Giderildi",
        _ => "İptal edildi"
    };

    public static bool IsOpen(PunchStatus status) => status is PunchStatus.Open or PunchStatus.Waiting;
}

public sealed class PunchItem
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public int? WindowDesignId { get; set; } // çizim listesindeki poz; poz silinirse kayıt kalır, konum yazısı görünür
    public WindowDesign? WindowDesign { get; set; }
    [StringLength(160)] public string? Location { get; set; } // ör. "Salon, 2. kanat" veya pozsuz işlerde yer tarifi
    [StringLength(60)] public string Kind { get; set; } = "Diğer";
    [Required(ErrorMessage = "Eksiği veya kusuru yazın."), StringLength(1000)] public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    [StringLength(40)] public string Stage { get; set; } = "Montaj";
    [StringLength(40)] public string Responsible { get; set; } = "Belirsiz";
    public PunchStatus Status { get; set; } = PunchStatus.Open;
    public DateOnly ReportedOn { get; set; }
    public DateOnly? DueOn { get; set; } // giderilmesi sözü verilen gün
    public DateOnly? ResolvedOn { get; set; }
    [StringLength(1000)] public string? Resolution { get; set; } // yapılan işlem: "cam değiştirildi", "kol takıldı"
    [StringLength(160)] public string? ReportedBy { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PunchInputVm
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public int? WindowDesignId { get; set; }
    [StringLength(160)] public string? Location { get; set; }
    [StringLength(60)] public string Kind { get; set; } = "Diğer";
    [Required(ErrorMessage = "Eksiği veya kusuru yazın."), StringLength(1000)] public string Description { get; set; } = "";
    [Range(1, 10000, ErrorMessage = "Adet 1 veya daha büyük olmalıdır.")] public int Quantity { get; set; } = 1;
    [StringLength(40)] public string Stage { get; set; } = "Montaj";
    [StringLength(40)] public string Responsible { get; set; } = "Belirsiz";
    public PunchStatus Status { get; set; } = PunchStatus.Open;
    public DateOnly ReportedOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    public DateOnly? DueOn { get; set; }
    public DateOnly? ResolvedOn { get; set; }
    [StringLength(1000)] public string? Resolution { get; set; }
}
