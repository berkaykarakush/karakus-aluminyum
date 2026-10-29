using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class GalleryImage
{
    public int Id { get; set; }

    // Bir hizmete bağlıysa ServiceId, projeye bağlıysa ProjectId dolu;
    // ikisi de null ise genel galeriye düşer.
    public int? ServiceId { get; set; }
    public ServiceContent? Service { get; set; }

    public int? ProjectId { get; set; }
    public PortfolioProject? Project { get; set; }

    [Required, StringLength(500)]
    public string ImagePath { get; set; } = "";

    [StringLength(160)]
    public string? Title { get; set; }

    [StringLength(500)]
    public string? Caption { get; set; }

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}