using System.ComponentModel.DataAnnotations;
namespace KarakusAluminyum.Web.Models;
public sealed class ServiceContent
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(180)] public string Slug { get; set; } = "";
    [Required, StringLength(500)] public string ShortDescription { get; set; } = "";
    [Required] public string Intro { get; set; } = "";
    [StringLength(500)] public string? Image { get; set; }
    [StringLength(180)] public string SeoTitle { get; set; } = "";
    [StringLength(320)] public string SeoDescription { get; set; } = "";
    public string BenefitsJson { get; set; } = "[]";
    public string FaqsJson { get; set; } = "[]";
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
