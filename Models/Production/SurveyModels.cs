using System.ComponentModel.DataAnnotations;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Models.Production;

// README 7.11: keşif (yerinde ölçü) randevusu ve sahada alınan ölçüler. Ölçüler tek tıkla çizim listesine aktarılır;
// teklif o listeden hazırlanır. Web sitesinden gelen talepten de açılabilir (müşteri kartı yoksa oluşturulur).
public enum SurveyStatus { Planned = 0, Done = 1, Cancelled = 2 }

public static class SurveyLabels
{
    public static string Label(SurveyStatus status) => status switch
    {
        SurveyStatus.Planned => "Planlandı",
        SurveyStatus.Done => "Ölçü alındı",
        _ => "İptal edildi"
    };

    // Sahada hızlı seçim: çizim listesine aktarılırken başlangıç çizimi bu açılıma göre kurulur, sonra çizim ekranında ayrıntılandırılır.
    public static readonly (string Key, string Label)[] Openings =
    {
        ("fixed", "Sabit"), ("tilt", "Vasistas"), ("turn", "Açılır-devrilir"), ("slide", "Sürme (2 kanat)"), ("door", "Kapı")
    };

    public static string OpeningLabel(string? key) => Openings.FirstOrDefault(o => o.Key == key).Label ?? "Sabit";
}

public sealed class SiteSurvey
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Number { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? ContactMessageId { get; set; } // web sitesinden gelen talep
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(120)] public string? ContactName { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    public DateOnly? PlannedOn { get; set; }
    public TimeOnly? PlannedAt { get; set; }
    [StringLength(120)] public string? Surveyor { get; set; } // keşfe giden kişi
    public SurveyStatus Status { get; set; } = SurveyStatus.Planned;
    [StringLength(2000)] public string? Notes { get; set; } // müşterinin isteği, bina bilgisi (kat, asansör, iskele)
    public int? DrawingListId { get; set; } // ölçüler aktarılınca oluşan çizim listesi
    public DrawingList? DrawingList { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<SurveyLine> Lines { get; set; } = new();
}

public sealed class SurveyLine
{
    public int Id { get; set; }
    public int SiteSurveyId { get; set; }
    public SiteSurvey? SiteSurvey { get; set; }
    public int SortOrder { get; set; }
    [StringLength(120)] public string Location { get; set; } = ""; // oda / yer: Salon, Mutfak, Balkon
    [StringLength(20)] public string Category { get; set; } = ProductCategories.Aluminum;
    [StringLength(20)] public string Opening { get; set; } = "fixed";
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int Quantity { get; set; } = 1;
    [StringLength(300)] public string? Note { get; set; }
}

public sealed class SurveyInputVm
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public int? ContactMessageId { get; set; }
    // Müşteri kartı yoksa (web talebi) bu bilgilerle bireysel müşteri açılır.
    [StringLength(160)] public string? NewCustomerName { get; set; }
    [StringLength(40)] public string? NewCustomerPhone { get; set; }
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta yazın."), StringLength(180)] public string? NewCustomerEmail { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(120)] public string? ContactName { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    public DateOnly? PlannedOn { get; set; }
    public TimeOnly? PlannedAt { get; set; }
    [StringLength(120)] public string? Surveyor { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}

public sealed class SurveyLineInput
{
    [StringLength(120)] public string? Location { get; set; }
    public string Category { get; set; } = ProductCategories.Aluminum;
    public string Opening { get; set; } = "fixed";
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public int Quantity { get; set; } = 1;
    [StringLength(300)] public string? Note { get; set; }
}
