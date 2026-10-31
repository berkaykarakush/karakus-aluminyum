using System.Text.RegularExpressions;

namespace KarakusAluminyum.Web.Services;

public enum SettingKind { Text, LongText, Toggle }

public sealed record SettingDefinition(string Key, string Group, string Label, string Default, SettingKind Kind = SettingKind.Text, string? Help = null, string? Pattern = null, string? PatternError = null);

// Panelde yönetilen tüm site ayarları. Değerler veritabanındadır; burası yalnızca başlangıç değeri ve form tanımıdır.
public static class SiteSettingCatalog
{
    public const string ContactGroup = "İletişim bilgileri";
    public const string SeoGroup = "Site, SEO ve sosyal medya";
    public const string AnalyticsGroup = "Analitik ve çerezler";
    public const string HomeGroup = "Ana sayfa metinleri";
    public const string AboutGroup = "Kurumsal sayfa metinleri";
    public const string FooterGroup = "Alt bilgi";
    public const string DrawingGroup = "Çizim ve üretim";

    public static readonly string[] Groups = { ContactGroup, SeoGroup, AnalyticsGroup, HomeGroup, AboutGroup, FooterGroup, DrawingGroup };

    private const string UrlPattern = "^https?://[^\\s\"'<>]+$";
    private const string UrlError = "http:// veya https:// ile başlayan geçerli bir adres yazın.";

    public static readonly IReadOnlyList<SettingDefinition> All = new SettingDefinition[]
    {
        new("PhoneDisplay", ContactGroup, "Görünen telefon", "+90 553 835 48 72"),
        new("PhoneE164", ContactGroup, "Telefon (aranabilir biçim)", "+905538354872", Help: "Örn. +905551112233", Pattern: "^\\+?[0-9]{7,15}$", PatternError: "Yalnızca rakam ve başta + kullanın."),
        new("WhatsAppNumber", ContactGroup, "WhatsApp numarası", "905538354872", Help: "Ülke koduyla, + olmadan. Boş bırakılırsa düğme gizlenir.", Pattern: "^[0-9]{7,15}$", PatternError: "Yalnızca rakam kullanın."),
        new("Email", ContactGroup, "E-posta", "info@karakusaluminyum.com", Pattern: "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", PatternError: "Geçerli bir e-posta yazın."),
        new("Address", ContactGroup, "Adres", "Karacaoğlan Mah. 6252 Sk. No: 3, Bornova/İzmir"),
        new("City", ContactGroup, "Şehir", "İzmir"),
        new("PrimaryServiceArea", ContactGroup, "Ana hizmet bölgesi", "Bornova"),
        new("GoogleMapsUrl", ContactGroup, "Google Haritalar gömme adresi", "https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d3125.168057091929!2d27.206807025382613!3d38.43758697370485!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x14b962db502ab2f7%3A0xe5025465f1957d53!2zS2FyYWt1xZ8gQWzDvG1pbnl1bQ!5e0!3m2!1str!2str!4v1793255765258!5m2!1str!2str", Pattern: UrlPattern, PatternError: UrlError),

        new("SiteName", SeoGroup, "Site adı", "Karakuş Alüminyum"),
        new("BaseUrl", SeoGroup, "Site adresi", "https://www.karakusaluminyum.com", Help: "Site haritası ve arama motoru bağlantıları bu adresi kullanır.", Pattern: UrlPattern, PatternError: UrlError),
        new("SiteDescription", SeoGroup, "İşletme açıklaması", "İzmir'de alüminyum doğrama, sürme sistemler, kapı ve pencere, giydirme cephe, cam balkon ve otomatik kapı çözümleri sunan mimari alüminyum firması.", SettingKind.LongText, "Arama motoru şeması ve yapay zekâ özeti (llms.txt) bu metni kullanır."),
        new("IndexNowEnabled", SeoGroup, "Yeni ve değişen sayfaları arama motorlarına otomatik bildir (Bing, Yandex)", "true", SettingKind.Toggle),
        new("AiCrawlersAllowed", SeoGroup, "Yapay zekâ tarayıcılarının siteyi okumasına izin ver (ChatGPT, Claude, Perplexity, Gemini)", "true", SettingKind.Toggle),
        new("GoogleBusinessProfileUrl", SeoGroup, "Google İşletme Profili", "https://share.google/0QhP26b6Mp7BmGUGt", Pattern: UrlPattern, PatternError: UrlError),
        new("InstagramUrl", SeoGroup, "Instagram", "https://www.instagram.com/karakus-aluminyum", Pattern: UrlPattern, PatternError: UrlError),
        new("FacebookUrl", SeoGroup, "Facebook", "https://www.facebook.com/karakus-aluminyum", Pattern: UrlPattern, PatternError: UrlError),
        new("LinkedInUrl", SeoGroup, "LinkedIn", "https://www.linkedin.com/karakus-aluminyum", Pattern: UrlPattern, PatternError: UrlError),

        new("Ga4MeasurementId", AnalyticsGroup, "Google Analytics 4 ölçüm kimliği", "", Help: "Örn. G-ABC123DEF4. Analytics > Yönetici > Veri akışları ekranında yazar.", Pattern: "^G-[A-Z0-9]{4,20}$", PatternError: "G- ile başlayan kimliği büyük harflerle yazın."),
        new("MetaPixelId", AnalyticsGroup, "Meta Pixel kimliği", "", Help: "Yalnızca rakamlardan oluşur. Meta Events Manager > Veri kaynakları ekranında yazar.", Pattern: "^[0-9]{6,20}$", PatternError: "Yalnızca rakam kullanın."),
        new("GoogleSiteVerification", AnalyticsGroup, "Google Search Console doğrulama kodu", "", Help: "Yalnızca content değeri.", Pattern: "^[A-Za-z0-9_\\-]{10,120}$", PatternError: "Yalnızca kodun kendisini yazın."),
        new("MetaDomainVerification", AnalyticsGroup, "Meta alan adı doğrulama kodu", "", Help: "Yalnızca content değeri.", Pattern: "^[A-Za-z0-9_\\-]{10,120}$", PatternError: "Yalnızca kodun kendisini yazın."),
        new("FirstPartyAnalyticsEnabled", AnalyticsGroup, "Panel raporları için site ziyaretlerini kaydet", "true", SettingKind.Toggle, "Yalnızca çerez onayı veren ziyaretçiler için, IP adresi olmadan kaydedilir."),
        new("ConsentText", AnalyticsGroup, "Çerez bildirimi metni", "Sitemizi geliştirmek ve reklamlarımızı ölçmek için analiz çerezleri kullanıyoruz. Google ve Meta hizmetleri nedeniyle veriler yurt dışına aktarılabilir. Onay vermezseniz bu çerezler yüklenmez.", SettingKind.LongText),

        new("ProductionDetailsEnabled", DrawingGroup, "Üretim detaylarını göster", "false", SettingKind.Toggle),

        new("HeroEyebrow", HomeGroup, "Üst başlık", "Mimari Alüminyum Sistemleri"),
        new("HeroTitle", HomeGroup, "Büyük başlık", "Mimariye değer katan"),
        new("HeroTitleEmphasis", HomeGroup, "Büyük başlık (vurgulu kısım)", "alüminyum çözümleri."),
        new("HeroText", HomeGroup, "Giriş metni", "Alüminyum doğrama, sürme sistemler, cephe ve tamamlayıcı mimari çözümler. Projenizin ihtiyaçlarını birlikte değerlendirelim.", SettingKind.LongText),
        new("TrustItems", HomeGroup, "Giriş altındaki maddeler", "Projeye özel çözümler\nModern mimari yaklaşım\nİzmir ve çevresi", SettingKind.LongText, "Her satıra bir madde."),
        new("ApproachTitle", HomeGroup, "Yaklaşım başlığı", "Detaylara önem veren, projeye odaklı yaklaşım."),
        new("ApproachText", HomeGroup, "Yaklaşım metni", "Her proje farklı ölçülerle ve farklı beklentilerle başlar. Açıklıktan donanıma, camdan montaj detayına kadar sistemi projenizin ihtiyaçlarına göre birlikte değerlendiriyoruz.", SettingKind.LongText),
        new("CtaTitle", HomeGroup, "Sayfa sonu çağrı başlığı", "Projeniz için doğru sistemi birlikte belirleyelim."),
        new("CtaText", HomeGroup, "Sayfa sonu çağrı metni", "İhtiyacınızı anlatın, ekibimizle iletişime geçin."),

        new("AboutTitle", AboutGroup, "Başlık", "Karakuş Alüminyum'u tanıyın."),
        new("AboutIntro", AboutGroup, "Giriş metni", "İzmir’de mimari alüminyum sistemleri alanında, projeye özel çözümlerle çalışıyoruz.", SettingKind.LongText),
        new("AboutSectionTitle", AboutGroup, "Bölüm başlığı", "Proje odaklı çalışma yaklaşımı."),
        new("AboutText", AboutGroup, "Bölüm metni", "Her proje farklı ölçülerle ve farklı beklentilerle başlar. Sistem seçiminden uygulama detaylarına kadar süreci ihtiyaçlarınıza göre birlikte değerlendiriyoruz.", SettingKind.LongText),

        new("FooterText", FooterGroup, "Alt bilgi tanıtım metni", "Mimari projeler için estetik, işlevsel ve uzun ömürlü alüminyum çözümleri.", SettingKind.LongText)
    };

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(x => x.Key == key);

    // Boş değer geçerlidir (ör. Analytics henüz bağlanmamış olabilir).
    public static string? Validate(SettingDefinition definition, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || definition.Pattern is null) return null;
        return Regex.IsMatch(value.Trim(), definition.Pattern) ? null : $"{definition.Label}: {definition.PatternError}";
    }
}
