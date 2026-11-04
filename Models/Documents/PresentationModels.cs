using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models.Documents;

// Kurumsal sunumun bölüm türleri. Hizmet, referans, proje ve marka bölümlerinin listesi
// panelin ilgili sayfalarından her basımda yeniden okunur; burada yalnızca başlık ve metin tutulur.
public static class PresentationKinds
{
    public const string Cover = "cover";
    public const string Contents = "contents";
    public const string About = "about";
    public const string WhyUs = "whyus";
    public const string Services = "services";
    public const string Process = "process";
    public const string Brands = "brands";
    public const string References = "references";
    public const string Projects = "projects";
    public const string Quality = "quality";
    public const string Contact = "contact";
    public const string Text = "text";

    public static readonly string[] All = { Cover, Contents, About, WhyUs, Services, Process, Brands, References, Projects, Quality, Contact, Text };

    public static string Label(string? kind) => kind switch
    {
        Cover => "Kapak",
        Contents => "İçindekiler",
        About => "Hakkımızda",
        WhyUs => "Neden biz",
        Services => "Hizmetler",
        Process => "Çalışma süreci",
        Brands => "Markalar ve sistemler",
        References => "Referanslar",
        Projects => "Projeler",
        Quality => "Kalite ve garanti",
        Contact => "İletişim",
        _ => "Serbest bölüm"
    };

    public static string Help(string? kind) => kind switch
    {
        Cover => "İlk sayfa. Teklifle birlikte basıldığında müşteri adı, teklif başlığı, numarası ve tarihi kapağa otomatik yazılır.",
        Contents => "Açık olan bölümlerin başlıkları ve sayfa numaraları otomatik listelenir.",
        About => "Firmanın tanıtımı. Deneyim yılı, hizmet, referans ve proje sayıları paneldeki kayıtlardan otomatik hesaplanır.",
        WhyUs => "Maddeler kart olarak dizilir (en fazla 6 önerilir).",
        Services => "Liste, Site içeriği > Hizmetler sayfasındaki yayında olan hizmetlerden gelir; sayfa başına 6 hizmet.",
        Process => "Maddeler soldan sağa adım adım dizilir (en fazla 6 önerilir).",
        Brands => "Liste, Çizim ve cam > Markalar sayfasındaki etkin markalardan kategoriye göre gelir.",
        References => "Liste, Site içeriği > Referanslar sayfasındaki yayında olan logolardan gelir; sayfa başına 15 logo.",
        Projects => "Liste, Site içeriği > Projeler sayfasındaki yayında olan projelerden gelir; sayfa başına 3 proje.",
        Quality => "Garanti ve kalite maddeleri. Garanti süresi gibi taahhütleri firmanızın gerçek koşullarına göre yazın.",
        Contact => "Kapanış sayfası. Telefon, e-posta, adres ve web sitesi Site ayarlarından otomatik gelir.",
        _ => "Görselli veya görselsiz metin sayfası; maddeler eklenebilir."
    };

    // Maddeleri sayfada gösterilen bölümler.
    public static bool HasItems(string? kind) => kind is About or WhyUs or Process or Quality or Text;
}

public sealed class PresentationSection
{
    public const int MaxItems = 8;

    public int Id { get; set; }
    [Required, StringLength(20)] public string Kind { get; set; } = PresentationKinds.Text;
    [StringLength(80, ErrorMessage = "Üst başlık en fazla 80 karakter olabilir.")] public string? Eyebrow { get; set; }
    [Required(ErrorMessage = "Başlık yazın."), StringLength(160, ErrorMessage = "Başlık en fazla 160 karakter olabilir.")] public string Title { get; set; } = "";
    [StringLength(1500, ErrorMessage = "Metin en fazla 1500 karakter olabilir; sayfaya sığması için kısa tutun.")] public string? Body { get; set; }
    [StringLength(500)] public string? ImagePath { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PresentationItem
{
    public string? Title { get; set; }
    public string? Text { get; set; }
}
