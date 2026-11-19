using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Documents;

public static class WatermarkModes
{
    public const string None = "none";
    public const string Logo = "logo";
    public const string Text = "text";
    public static readonly string[] All = { None, Logo, Text };
}

// Tüm belgelerin ortak anteti: logo, iletişim bilgileri, alt bilgi ve filigran.
// Teklif, sözleşme, çizim listesi, QR ile açılan sayfalar ve sipariş çıktıları aynı ayarı kullanır.
public sealed class DocumentBranding
{
    [StringLength(500)] public string? LogoPath { get; set; } // boş: logo yerine firma adı yazılır
    [Range(8, 40, ErrorMessage = "Logo yüksekliği 8-40 mm arasında olmalıdır.")] public int LogoHeightMm { get; set; } = 16;
    [StringLength(160)] public string? CompanyName { get; set; }
    [StringLength(160)] public string? Slogan { get; set; }
    [StringLength(60)] public string? Phone { get; set; }
    [StringLength(60)] public string? Phone2 { get; set; }
    [StringLength(60)] public string? WhatsApp { get; set; }
    [StringLength(160)] public string? Email { get; set; }
    [StringLength(160)] public string? Website { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(160)] public string? TaxInfo { get; set; }
    [StringLength(160)] public string? Instagram { get; set; }
    [StringLength(500)] public string? FooterNote { get; set; }

    // Üst bilgide logonun yanında iletişim satırı; alt bilgide seçilen iletişim bilgileri.
    public bool HeaderContact { get; set; } = true;
    public bool FooterPhone { get; set; } = true;
    public bool FooterEmail { get; set; } = true;
    public bool FooterWebsite { get; set; } = true;
    public bool FooterAddress { get; set; } = true;
    public bool FooterTaxInfo { get; set; }
    public bool FooterSocial { get; set; }
    public bool PageNumbers { get; set; } = true;
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Renk #RRGGBB biçiminde olmalıdır.")] public string AccentColor { get; set; } = "#b0202a";

    // Filigran: her basılan sayfanın ortasında soluk logo veya yazı.
    public string WatermarkMode { get; set; } = WatermarkModes.Logo;
    [StringLength(60)] public string? WatermarkText { get; set; }
    [Range(2, 30, ErrorMessage = "Filigran saydamlığı %2-30 arasında olmalıdır.")] public int WatermarkOpacity { get; set; } = 6;
    [Range(20, 100, ErrorMessage = "Filigran boyutu %20-100 arasında olmalıdır.")] public int WatermarkSize { get; set; } = 60;
    [Range(-60, 60, ErrorMessage = "Filigran açısı -60 ile 60 derece arasında olmalıdır.")] public int WatermarkAngle { get; set; } = -30;

    public IEnumerable<string> FooterItems()
    {
        if (FooterPhone) foreach (var phone in new[] { Phone, Phone2 }.Where(x => !string.IsNullOrWhiteSpace(x))) yield return "Tel: " + phone;
        if (FooterPhone && !string.IsNullOrWhiteSpace(WhatsApp)) yield return "WhatsApp: " + WhatsApp;
        if (FooterEmail && !string.IsNullOrWhiteSpace(Email)) yield return Email!;
        if (FooterWebsite && !string.IsNullOrWhiteSpace(Website)) yield return Website!;
        if (FooterSocial && !string.IsNullOrWhiteSpace(Instagram)) yield return "Instagram: " + Instagram;
    }

    public IEnumerable<string> HeaderItems() => new[] { Phone, Email, Website }.Where(x => !string.IsNullOrWhiteSpace(x))!;

    public string WatermarkLabel => string.IsNullOrWhiteSpace(WatermarkText) ? CompanyName ?? "" : WatermarkText!;
}

public sealed record BrandHeadVm(string? Title, string? Date);

public sealed class DocumentBrandingProvider
{
    public const string Key = "DocumentBranding";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    public const int MaxLength = 2000; // SiteSettingRecords.Value sınırı
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _site;
    private DocumentBranding? _cached;
    public DocumentBrandingProvider(AppDbContext db, SiteSettingsProvider site) { _db = db; _site = site; }

    public async Task<DocumentBranding> GetAsync()
    {
        if (_cached is not null) return _cached;
        var json = await _db.SiteSettingRecords.AsNoTracking().Where(x => x.Key == Key).Select(x => x.Value).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { if (JsonSerializer.Deserialize<DocumentBranding>(json, Json) is { } saved) return _cached = saved; }
            catch (JsonException) { }
        }
        return _cached = await DefaultsAsync();
    }

    // Kaydedilmemiş kurulum: teklif şablonundaki logo ve iletişim bilgileri ile site ayarlarından başlar.
    private async Task<DocumentBranding> DefaultsAsync()
    {
        var site = await _site.GetAsync();
        var quote = await _db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == DocumentKinds.Quote);
        static string? Real(string? value) => string.IsNullOrWhiteSpace(value) || value.Contains("XXX") || value.Contains("DOMAININIZ") ? null : value.Trim();
        return new DocumentBranding
        {
            LogoPath = quote?.LogoPath ?? "/images/transparent.webp",
            CompanyName = Real(quote?.SellerLegalName) ?? Real(quote?.SellerName) ?? Real(site.LegalName) ?? site.SiteName,
            Phone = Real(quote?.Phone) ?? Real(site.PhoneDisplay),
            Phone2 = Real(site.Phone2Display),
            Email = Real(quote?.SellerEmail) ?? Real(site.Email),
            Website = Real(site.BaseUrl)?.Replace("https://", "").Replace("http://", "").TrimEnd('/'),
            Address = Real(quote?.FooterAddress) ?? Real(quote?.SellerAddress) ?? Real(site.Address),
            TaxInfo = Real(quote?.SellerTaxInfo),
            Instagram = Real(site.InstagramUrl)
        };
    }

    public async Task SaveAsync(DocumentBranding branding)
    {
        var row = await _db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == Key);
        if (row is null) _db.SiteSettingRecords.Add(row = new SiteSettingRecord { Key = Key, Description = "Belge anteti ve filigranı" });
        var json = JsonSerializer.Serialize(branding, Json);
        if (json.Length > MaxLength) throw new InvalidOperationException("Antet bilgileri çok uzun; alt bilgi notunu veya adresi kısaltın.");
        row.Value = json;
        await _db.SaveChangesAsync();
        _cached = branding;
    }
}
