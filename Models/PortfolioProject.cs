using System.ComponentModel.DataAnnotations;
namespace KarakusAluminyum.Web.Models;
public sealed class PortfolioProject
{
    public int Id { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    [Required, StringLength(180)] public string Slug { get; set; } = "";
    [Required, StringLength(120)] public string Type { get; set; } = "";
    [Required, StringLength(1000)] public string Summary { get; set; } = "";
    [StringLength(500)] public string? Image { get; set; }
    [StringLength(120)] public string Location { get; set; } = "İzmir";
    public string? Content { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
