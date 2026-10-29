using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class GalleryUploadVm
{
    [Range(1, int.MaxValue, ErrorMessage = "Hizmet seçin.")]
    public int? ServiceId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Proje seçin.")]
    public int? ProjectId { get; set; }

    [StringLength(160)]
    public string? Title { get; set; }

    [StringLength(500)]
    public string? Caption { get; set; }

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}