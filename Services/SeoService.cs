using KarakusAluminyum.Web.Models;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services;

public sealed class SeoService
{
    private readonly SiteSettings _settings;
    public SeoService(IOptions<SiteSettings> options) => _settings = options.Value;

    // Panel ayarları verilirse onlar kullanılır; appsettings yalnızca başlangıç değeridir.
    public string Canonical(string path, SiteSettings? settings = null)
    {
        var baseUrl = (settings ?? _settings).BaseUrl.TrimEnd('/');
        var cleanPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        if (!cleanPath.StartsWith('/')) cleanPath = "/" + cleanPath;
        return baseUrl + cleanPath;
    }

    public object LocalBusinessSchema(SiteSettings? current = null) => BuildSchema(current ?? _settings);

    private object BuildSchema(SiteSettings site) => new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "HomeAndConstructionBusiness",
        ["name"] = site.SiteName,
        ["url"] = Canonical("/", site),
        ["logo"] = Canonical("/images/transparent.png", site),
        ["image"] = Canonical("/images/aluminyum-dograma.png", site),
        ["description"] = "İzmir'de mimari alüminyum doğrama ve sistem çözümleri.",
        ["telephone"] = site.PhoneE164,
        ["email"] = site.Email,
        ["address"] = new Dictionary<string, object?>
        {
            ["@type"] = "PostalAddress",
            ["addressLocality"] = site.City,
            ["addressRegion"] = site.City,
            ["streetAddress"] = site.Address,
            ["addressCountry"] = "TR"
        },
        ["areaServed"] = new[] { site.City, site.PrimaryServiceArea },
        ["sameAs"] = new[] { site.InstagramUrl, site.FacebookUrl, site.LinkedInUrl }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
    };
}
