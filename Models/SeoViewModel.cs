namespace KarakusAluminyum.Web.Models;

public sealed class SeoViewModel
{
    public string Title { get; init; } = "Karakuş Alüminyum";
    public string Description { get; init; } = "";
    public string CanonicalPath { get; init; } = "/";
    public string? ImagePath { get; init; }
    public string Robots { get; init; } = "index,follow,max-image-preview:large,max-snippet:-1,max-video-preview:-1";
    public string Type { get; init; } = "website";
    public DateTime? PublishedUtc { get; init; }
    public DateTime? ModifiedUtc { get; init; }
    // Ana sayfadan sonraki içerik yolu: (ad, adres).
    public IReadOnlyList<(string Name, string Path)> Breadcrumbs { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<object?> Schema { get; init; } = Array.Empty<object?>();
    public string PageType { get; init; } = "WebPage";
    public bool IsIndexable => !Robots.Contains("noindex", StringComparison.OrdinalIgnoreCase);
    public string MarkdownPath => CanonicalPath == "/" ? "/index.md" : CanonicalPath + ".md";
}
