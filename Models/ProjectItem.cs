namespace KarakusAluminyum.Web.Models;

public sealed record ProjectItem(
    string Slug,
    string Title,
    string Type,
    string Summary,
    string Image,
    string Location = "İzmir");
