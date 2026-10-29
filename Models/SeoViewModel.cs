namespace KarakusAluminyum.Web.Models;

public sealed class SeoViewModel
{
    public string Title { get; init; } = "Karakuş Alüminyum";
    public string Description { get; init; } = "";
    public string CanonicalPath { get; init; } = "/";
    public string? ImagePath { get; init; }
    public string Robots { get; init; } = "index,follow,max-image-preview:large,max-snippet:-1,max-video-preview:-1";
    public string Type { get; init; } = "website";
}
