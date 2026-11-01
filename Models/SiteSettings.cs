namespace KarakusAluminyum.Web.Models;

public sealed class SiteSettings
{
    public string SiteName { get; set; } = "Karakuş Alüminyum";
    public string BaseUrl { get; set; } = "https://www.DOMAININIZ.com";
    public string PhoneDisplay { get; set; } = "0232 XXX XX XX";
    public string PhoneE164 { get; set; } = "+90232XXXXXXX";
    public string Phone2Display { get; set; } = "";
    public string Phone2E164 { get; set; } = "";
    // İkinci numara ancak iki alanı da doluysa gösterilir: aranabilir adresi olmayan numara tıklanınca çalışmaz.
    private bool HasPhone2 => !string.IsNullOrWhiteSpace(Phone2Display) && !string.IsNullOrWhiteSpace(Phone2E164);
    public IReadOnlyList<(string Display, string E164)> Phones
    {
        get
        {
            var phones = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(PhoneDisplay) && !PhoneE164.Contains('X')) phones.Add((PhoneDisplay, PhoneE164));
            if (HasPhone2) phones.Add((Phone2Display, Phone2E164));
            return phones;
        }
    }
    // Düz metinde kullanılan numara yazısı.
    public string PhoneText => HasPhone2 ? $"{PhoneDisplay} veya {Phone2Display}" : PhoneDisplay;
    public string Email { get; set; } = "info@DOMAININIZ.com";
    public string WhatsAppNumber { get; set; } = "90XXXXXXXXXX";
    public string Address { get; set; } = "İzmir, Türkiye";
    public string City { get; set; } = "İzmir";
    public string PrimaryServiceArea { get; set; } = "Bornova";
    public string GoogleMapsUrl { get; set; } = "";
    // Çerçeve içinde açılmayan adresler (paylaşım bağlantısı vb.) boş kutu göstermesin diye harita olarak kullanılmaz.
    public string? MapEmbedUrl => GoogleMapsUrl.StartsWith("https://www.google.com/maps/embed", StringComparison.Ordinal) ? GoogleMapsUrl : null;
    public string MapDirectionsUrl => "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(Address);
    public string GoogleBusinessProfileUrl { get; set; } = "";
    public string InstagramUrl { get; set; } = "";
    public string FacebookUrl { get; set; } = "";
    public string LinkedInUrl { get; set; } = "";
    public string Ga4MeasurementId { get; set; } = "";
    public string MetaPixelId { get; set; } = "";
    public string GoogleSiteVerification { get; set; } = "";
    public string MetaDomainVerification { get; set; } = "";
    public string FirstPartyAnalyticsEnabled { get; set; } = "true";
    public string LegalName { get; set; } = "Karakuş Alüminyum";
    public string SiteDescription { get; set; } = "İzmir'de mimari alüminyum doğrama ve sistem çözümleri.";
    public string IndexNowEnabled { get; set; } = "true";
    public string AiCrawlersAllowed { get; set; } = "true";
    public string NotifyEmailEnabled { get; set; } = "true";
    public string NotifyEmailTo { get; set; } = "";
    public string NotifyCustomerEmailEnabled { get; set; } = "true";
    public string NotifyWhatsAppEnabled { get; set; } = "true";
    public string NotifyWhatsAppTo { get; set; } = "";
    public string WhatsAppTemplateName { get; set; } = "";
    public string WhatsAppTemplateLanguage { get; set; } = "tr";
    public string ProductionDetailsEnabled { get; set; } = "false";
    public bool ProductionDetails => ProductionDetailsEnabled == "true";
    public string ConsentText { get; set; } = "";
    public string HeroEyebrow { get; set; } = "";
    public string HeroTitle { get; set; } = "";
    public string HeroTitleEmphasis { get; set; } = "";
    public string HeroText { get; set; } = "";
    public string ApproachTitle { get; set; } = "";
    public string ApproachText { get; set; } = "";
    public string CtaTitle { get; set; } = "";
    public string CtaText { get; set; } = "";
    public string AboutTitle { get; set; } = "";
    public string AboutIntro { get; set; } = "";
    public string AboutSectionTitle { get; set; } = "";
    public string AboutText { get; set; } = "";
    public string FooterText { get; set; } = "";

    public const int FoundedYear = 2001;
    // Sunucu saati UTC olabilir; yıl Türkiye saatine göre değişir.
    public static string CopyrightYears => $"{FoundedYear}-{DateTime.UtcNow.AddHours(3).Year}";
}
