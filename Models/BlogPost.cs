using System.ComponentModel.DataAnnotations;
namespace KarakusAluminyum.Web.Models;
public sealed class BlogPost
{
    public int Id { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    [Required, StringLength(180)] public string Slug { get; set; } = "";
    [Required, StringLength(500)] public string Summary { get; set; } = "";
    [Required] public string Content { get; set; } = "";
    [StringLength(500)] public string Image { get; set; } = "/images/aluminyum-dograma.png";
    [StringLength(180)] public string SeoTitle { get; set; } = "";
    [StringLength(320)] public string SeoDescription { get; set; } = "";
    public bool IsPublished { get; set; } = false;
    public DateTime? PublishedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
