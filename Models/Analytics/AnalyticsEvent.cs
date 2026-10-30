using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models.Analytics;

// Çerez onayı veren ziyaretçinin anonim site hareketi. IP ve kullanıcı bilgisi tutulmaz;
// oturum kimliği tarayıcıda rastgele üretilir ve sekme kapanınca silinir.
public sealed class AnalyticsEvent
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    [Required, StringLength(20)] public string Type { get; set; } = AnalyticsEventTypes.PageView;
    [Required, StringLength(300)] public string Path { get; set; } = "/";
    [StringLength(120)] public string? ReferrerHost { get; set; }
    [StringLength(80)] public string? UtmSource { get; set; }
    [StringLength(80)] public string? UtmMedium { get; set; }
    [StringLength(80)] public string? UtmCampaign { get; set; }
    [StringLength(10)] public string Device { get; set; } = "desktop";
    [Required, StringLength(40)] public string SessionId { get; set; } = "";
}

public static class AnalyticsEventTypes
{
    public const string PageView = "pageview";
    public const string WhatsApp = "whatsapp";
    public const string Phone = "phone";
    public const string Email = "email";
    public const string QuoteCta = "quote_cta";
    public const string Lead = "lead";
    public const string ViewService = "view_service";
    public static readonly string[] All = { PageView, WhatsApp, Phone, Email, QuoteCta, Lead, ViewService };

    public static string Label(string type) => type switch
    {
        PageView => "Sayfa görüntüleme",
        WhatsApp => "WhatsApp tıklaması",
        Phone => "Telefon tıklaması",
        Email => "E-posta tıklaması",
        QuoteCta => "Teklif düğmesi tıklaması",
        Lead => "İletişim formu gönderimi",
        ViewService => "Hizmet sayfası görüntüleme",
        _ => type
    };
}

public sealed class AnalyticsInput
{
    public string? Type { get; set; }
    public string? Path { get; set; }
    public string? Referrer { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? SessionId { get; set; }
}
