using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services.Seo;
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

    // Tüm sayfalarda tek bir şema grafı: işletme, site, sayfa, içerik yolu ve sayfaya özel düğümler birbirine @id ile bağlıdır.
    public object PageGraph(SeoViewModel page, SiteSettings site)
    {
        var business = (Dictionary<string, object?>)BuildSchema(site);
        business.Remove("@context");
        business["@id"] = "~/#organization";
        var pageUrl = Canonical(page.CanonicalPath, site);
        var nodes = new List<object?>
        {
            business,
            new Dictionary<string, object?> { ["@type"] = "WebSite", ["@id"] = "~/#website", ["url"] = Canonical("/", site), ["name"] = site.SiteName, ["inLanguage"] = "tr-TR", ["publisher"] = new Dictionary<string, object?> { ["@id"] = "~/#organization" } },
            new Dictionary<string, object?>
            {
                ["@type"] = page.PageType, ["@id"] = pageUrl + "#webpage", ["url"] = pageUrl, ["name"] = page.Title, ["description"] = page.Description, ["inLanguage"] = "tr-TR",
                ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = "~/#website" }, ["about"] = new Dictionary<string, object?> { ["@id"] = "~/#organization" },
                ["primaryImageOfPage"] = string.IsNullOrWhiteSpace(page.ImagePath) ? null : SchemaNodes.Url(page.ImagePath),
                ["dateModified"] = page.ModifiedUtc is { } modified ? DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ") : null
            }
        };
        if (page.Breadcrumbs.Count > 0)
        {
            var trail = new[] { ("Ana Sayfa", "/") }.Concat(page.Breadcrumbs).ToList();
            nodes.Add(new Dictionary<string, object?>
            {
                ["@type"] = "BreadcrumbList", ["@id"] = pageUrl + "#breadcrumb",
                ["itemListElement"] = trail.Select((x, i) => new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = i + 1, ["name"] = x.Item1, ["item"] = SchemaNodes.Url(x.Item2) }).ToList()
            });
        }
        nodes.AddRange(page.Schema);
        return new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@graph"] = nodes.Where(x => x is not null).Select(x => Resolve(x, site)).ToList() };
    }

    public string PageGraphJson(SeoViewModel page, SiteSettings site) => JsonSerializer.Serialize(PageGraph(page, site), GraphJson);

    private static readonly JsonSerializerOptions GraphJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private object? Resolve(object? value, SiteSettings site) => value switch
    {
        string text when text.StartsWith("~/", StringComparison.Ordinal) => Canonical(text[1..], site),
        Dictionary<string, object?> map => map.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => Resolve(x.Value, site)),
        System.Collections.IEnumerable list and not string => list.Cast<object?>().Select(x => Resolve(x, site)).ToList(),
        _ => value
    };

    private object BuildSchema(SiteSettings site) => new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "HomeAndConstructionBusiness",
        ["name"] = site.SiteName,
        ["url"] = Canonical("/", site),
        ["logo"] = Canonical("/images/transparent.webp", site),
        ["image"] = Canonical("/images/aluminyum-dograma.webp", site),
        ["description"] = site.SiteDescription,
        ["telephone"] = site.PhoneE164,
        ["contactPoint"] = site.Phones.Count > 1 ? site.Phones.Select(x => new Dictionary<string, object?> { ["@type"] = "ContactPoint", ["telephone"] = x.E164, ["contactType"] = "customer service", ["areaServed"] = "TR", ["availableLanguage"] = "Turkish" }).ToArray() : null,
        ["email"] = site.Email,
        ["address"] = new Dictionary<string, object?>
        {
            ["@type"] = "PostalAddress",
            ["addressLocality"] = string.IsNullOrWhiteSpace(site.PrimaryServiceArea) ? site.City : site.PrimaryServiceArea,
            ["addressRegion"] = site.City,
            ["streetAddress"] = site.Address,
            ["addressCountry"] = "TR"
        },
        ["foundingDate"] = SiteSettings.FoundedYear.ToString(),
        ["geo"] = site.MapCoordinates is { } point ? new Dictionary<string, object?> { ["@type"] = "GeoCoordinates", ["latitude"] = point.Latitude, ["longitude"] = point.Longitude } : null,
        ["hasMap"] = string.IsNullOrWhiteSpace(site.GoogleBusinessProfileUrl) ? null : site.GoogleBusinessProfileUrl,
        ["openingHours"] = string.IsNullOrWhiteSpace(site.OpeningHours) ? null : site.OpeningHours.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        ["areaServed"] = new[] { site.City, site.PrimaryServiceArea }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => new Dictionary<string, object?> { ["@type"] = x == site.City ? "City" : "AdministrativeArea", ["name"] = x }).ToArray(),
        ["sameAs"] = new[] { site.InstagramUrl, site.FacebookUrl, site.LinkedInUrl, site.GoogleBusinessProfileUrl }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
    };
}
