using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class FaqItem
{
    public int Id { get; set; }

    // Bir hizmete bağlıysa o hizmetin sayfasında, null ise ana sayfada gösterilir.
    public int? ServiceId { get; set; }
    public ServiceContent? Service { get; set; }

    [Required]
    public string Question { get; set; } = "";

    [Required]
    public string Answer { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
