using KarakusAluminyum.Web.Models;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services;

public sealed class SeoService
{
    private readonly SiteSettings _settings;
    public SeoService(IOptions<SiteSettings> options) => _settings = options.Value;

    public string Canonical(string path)
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var cleanPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        if (!cleanPath.StartsWith('/')) cleanPath = "/" + cleanPath;
        return baseUrl + cleanPath;
    }

    public object LocalBusinessSchema() => new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "HomeAndConstructionBusiness",
        ["name"] = _settings.SiteName,
        ["url"] = Canonical("/"),
        ["logo"] = Canonical("/images/transparent.png"),
        ["image"] = Canonical("/images/aluminum-dograma.png"),
        ["description"] = "İzmir'de mimari alüminyum doğrama ve sistem çözümleri.",
        ["telephone"] = _settings.PhoneE164,
        ["email"] = _settings.Email,
        ["address"] = new Dictionary<string, object?>
        {
            ["@type"] = "PostalAddress",
            ["addressLocality"] = _settings.City,
            ["addressRegion"] = _settings.City,
            ["streetAddress"] = _settings.Address,
            ["addressCountry"] = "TR"
        },
        ["areaServed"] = new[] { _settings.City, _settings.PrimaryServiceArea },
        ["sameAs"] = new[] { _settings.InstagramUrl, _settings.FacebookUrl, _settings.LinkedInUrl }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
    };
}
