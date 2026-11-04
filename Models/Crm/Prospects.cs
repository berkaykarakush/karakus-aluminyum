using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models.Crm;

// Satış hunisi: potansiyel firma müşteriye dönüşene ya da elenene kadar bu aşamalardan geçer.
public enum ProspectStage { New = 0, Contacted = 1, Meeting = 2, Quoted = 3, Won = 4, Lost = 5, Unqualified = 6 }
public enum ProspectActivityKind { Call = 0, Visit = 1, Email = 2, WhatsApp = 3, Meeting = 4, Note = 5 }
public enum ProspectScanStatus { Queued = 0, Running = 1, Done = 2, Failed = 3 }

public static class ProspectLabels
{
    public static string Stage(ProspectStage s) => s switch
    {
        ProspectStage.New => "Yeni",
        ProspectStage.Contacted => "İletişime geçildi",
        ProspectStage.Meeting => "Görüşme yapıldı",
        ProspectStage.Quoted => "Teklif verildi",
        ProspectStage.Won => "Kazanıldı",
        ProspectStage.Lost => "Kaybedildi",
        _ => "Uygun değil"
    };

    public static string Activity(ProspectActivityKind k) => k switch
    {
        ProspectActivityKind.Call => "Telefon",
        ProspectActivityKind.Visit => "Ziyaret",
        ProspectActivityKind.Email => "E-posta",
        ProspectActivityKind.WhatsApp => "WhatsApp",
        ProspectActivityKind.Meeting => "Toplantı",
        _ => "Not"
    };

    public static string Scan(ProspectScanStatus s) => s switch
    {
        ProspectScanStatus.Queued => "Sırada",
        ProspectScanStatus.Running => "Çalışıyor",
        ProspectScanStatus.Done => "Tamamlandı",
        _ => "Hata"
    };
}

// Taranacak sektör. Her anahtar kelime, açık her ilçe için ayrı aranır: "inşaat firması Bornova İzmir".
public sealed class ProspectSector
{
    public const int MaxKeywords = 10;
    public int Id { get; set; }
    [Required(ErrorMessage = "Sektör adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    public string KeywordsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime? LastScanUtc { get; set; }
}

public sealed class ProspectDistrict
{
    public int Id { get; set; }
    [Required, StringLength(60)] public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

// Firma havuzundaki kayıt. Müşteriler tablosundan ayrıdır; anlaşma olunca müşteriye dönüştürülür.
public sealed class Prospect
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Firma adı yazın."), StringLength(200)] public string Name { get; set; } = "";
    public int? SectorId { get; set; }
    public ProspectSector? Sector { get; set; }
    public ProspectStage Stage { get; set; } = ProspectStage.New;
    [StringLength(120)] public string? ContactPerson { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta yazın."), StringLength(180)] public string? Email { get; set; }
    [StringLength(500)] public string? Website { get; set; }
    [StringLength(300)] public string? Instagram { get; set; }
    [StringLength(300)] public string? Facebook { get; set; }
    [StringLength(300)] public string? LinkedIn { get; set; }
    [StringLength(300)] public string? TikTok { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(60)] public string? District { get; set; }
    [StringLength(300)] public string? GooglePlaceId { get; set; }
    [StringLength(500)] public string? GoogleMapsUrl { get; set; }
    public double? Rating { get; set; }
    public int? RatingCount { get; set; }
    [StringLength(40)] public string Source { get; set; } = ProspectSources.Manual;
    public DateOnly? NextFollowUp { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public bool DoNotContact { get; set; } // firma iletişim istemediğini bildirdi; taramalar da geri açmaz
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime? EnrichedUtc { get; set; } // web sitesinden e-posta ve sosyal hesaplar en son ne zaman okundu
    [StringLength(300)] public string? EnrichError { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenUtc { get; set; } // en son hangi taramada görüldü
    public ICollection<ProspectActivity> Activities { get; set; } = new List<ProspectActivity>();
}

public static class ProspectSources
{
    public const string Google = "google";
    public const string Manual = "manual";
    public static string Label(string? source) => source == Google ? "Google Haritalar" : "Elle eklendi";
}

public sealed class ProspectActivity
{
    public int Id { get; set; }
    public int ProspectId { get; set; }
    public Prospect? Prospect { get; set; }
    public ProspectActivityKind Kind { get; set; }
    [Required(ErrorMessage = "Görüşme notu yazın."), StringLength(1000)] public string Text { get; set; } = "";
    [StringLength(256)] public string? CreatedBy { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProspectScan
{
    public int Id { get; set; }
    public int? SectorId { get; set; }
    public ProspectSector? Sector { get; set; }
    public ProspectScanStatus Status { get; set; } = ProspectScanStatus.Queued;
    [StringLength(256)] public string? RequestedBy { get; set; }
    public int RequestCount { get; set; } // Google'a giden ücretli istek
    public int FoundCount { get; set; }
    public int NewCount { get; set; }
    [StringLength(1000)] public string? Message { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
