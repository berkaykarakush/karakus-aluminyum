namespace KarakusAluminyum.Web.Models;

public sealed record ServicePage(
    string Slug,
    string Name,
    string ShortDescription,
    string Intro,
    string Image,
    string SeoTitle,
    string SeoDescription,
    IReadOnlyList<string> Benefits);
