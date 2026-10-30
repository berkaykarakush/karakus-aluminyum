using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class FaqInputVm
{
    public int Id { get; set; }

    public int? ServiceId { get; set; }

    [Required(ErrorMessage = "Soru yazın."), StringLength(500, ErrorMessage = "Soru en fazla 500 karakter olabilir.")]
    public string Question { get; set; } = "";

    [Required(ErrorMessage = "Cevap yazın."), StringLength(4000, ErrorMessage = "Cevap en fazla 4000 karakter olabilir.")]
    public string Answer { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}
