using KarakusAluminyum.Web.Models;

namespace KarakusAluminyum.Web.Services.Seo;

// Sayfaya özel schema.org düğümleri. "~/" ile başlayan adresler sayfa basılırken site adresine çevrilir.
public static class SchemaNodes
{
    public const string OrganizationId = "~/#organization";
    private static Dictionary<string, object?> Reference(string id) => new() { ["@id"] = id };
    public static string Url(string path) => path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : "~" + (path.StartsWith('/') ? path : "/" + path);
    private static string Date(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

    public static object Service(string name, string description, string path, string? image, string city) => new Dictionary<string, object?>
    {
        ["@type"] = "Service", ["@id"] = Url(path) + "#service", ["name"] = name, ["description"] = description, ["url"] = Url(path), ["image"] = image is null ? null : Url(image),
        ["provider"] = Reference(OrganizationId), ["areaServed"] = city, ["serviceType"] = name
    };

    public static object? FaqPage(string path, IReadOnlyCollection<FaqItem> faqs) => faqs.Count == 0 ? null : new Dictionary<string, object?>
    {
        ["@type"] = "FAQPage", ["@id"] = Url(path) + "#faq",
        ["mainEntity"] = faqs.Select(x => new Dictionary<string, object?> { ["@type"] = "Question", ["name"] = x.Question, ["acceptedAnswer"] = new Dictionary<string, object?> { ["@type"] = "Answer", ["text"] = x.Answer } }).ToList()
    };

    public static object Article(BlogPost post, string path) => new Dictionary<string, object?>
    {
        ["@type"] = "BlogPosting", ["@id"] = Url(path) + "#article", ["headline"] = post.Title.Length > 110 ? post.Title[..110] : post.Title, ["description"] = post.Summary, ["image"] = Url(post.Image), ["url"] = Url(path), ["inLanguage"] = "tr-TR",
        ["datePublished"] = Date(post.PublishedUtc ?? post.CreatedUtc), ["dateModified"] = Date(post.UpdatedUtc), ["author"] = Reference(OrganizationId), ["publisher"] = Reference(OrganizationId), ["mainEntityOfPage"] = Url(path), ["articleBody"] = post.Content
    };

    public static object Project(string title, string summary, string type, string location, string image, string path, DateTime created, DateTime updated) => new Dictionary<string, object?>
    {
        ["@type"] = "CreativeWork", ["@id"] = Url(path) + "#project", ["name"] = title, ["description"] = summary, ["genre"] = type, ["image"] = Url(image), ["url"] = Url(path), ["inLanguage"] = "tr-TR",
        ["locationCreated"] = new Dictionary<string, object?> { ["@type"] = "Place", ["name"] = location }, ["creator"] = Reference(OrganizationId), ["dateCreated"] = Date(created), ["dateModified"] = Date(updated)
    };

    public static object ItemList(string path, IEnumerable<(string Path, string Name)> items) => new Dictionary<string, object?>
    {
        ["@type"] = "ItemList", ["@id"] = Url(path) + "#list",
        ["itemListElement"] = items.Select((x, i) => new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = i + 1, ["url"] = Url(x.Path), ["name"] = x.Name }).ToList()
    };

    public static object ImageGallery(string path, string name, IEnumerable<GalleryImage> images) => new Dictionary<string, object?>
    {
        ["@type"] = "ImageGallery", ["@id"] = Url(path) + "#gallery", ["name"] = name, ["url"] = Url(path),
        ["image"] = images.Take(100).Select(x => new Dictionary<string, object?> { ["@type"] = "ImageObject", ["contentUrl"] = Url(x.ImagePath), ["name"] = x.Title, ["caption"] = x.Caption }).ToList()
    };
}
