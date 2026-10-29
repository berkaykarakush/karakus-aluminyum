namespace KarakusAluminyum.Web.Models;

public sealed class SiteSettings
{
    public string SiteName { get; set; } = "Karakuş Alüminyum";
    public string BaseUrl { get; set; } = "https://www.DOMAININIZ.com";
    public string PhoneDisplay { get; set; } = "0232 XXX XX XX";
    public string PhoneE164 { get; set; } = "+90232XXXXXXX";
    public string Email { get; set; } = "info@DOMAININIZ.com";
    public string WhatsAppNumber { get; set; } = "90XXXXXXXXXX";
    public string Address { get; set; } = "İzmir, Türkiye";
    public string City { get; set; } = "İzmir";
    public string PrimaryServiceArea { get; set; } = "Bornova";
    public string GoogleMapsUrl { get; set; } = "";
    public string GoogleBusinessProfileUrl { get; set; } = "";
    public string InstagramUrl { get; set; } = "";
    public string FacebookUrl { get; set; } = "";
    public string LinkedInUrl { get; set; } = "";
}
