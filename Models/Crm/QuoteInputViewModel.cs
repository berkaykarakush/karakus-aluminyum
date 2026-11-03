using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models.Crm;

public sealed class QuoteInputViewModel
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    [Required(ErrorMessage = "Teklif başlığı yazın."), StringLength(180)] public string Title { get; set; } = "";
    public DateTime DocumentDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? ValidUntilUtc { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Included;
    [Range(0, 100)] public decimal TaxRate { get; set; } = 20m;
    [Range(0, 100)] public decimal DiscountRate { get; set; }
    public int? DrawingListId { get; set; }
    public int? GlassTypeId { get; set; }
    [Range(0, 500, ErrorMessage = "Kâr marjı %0 ile %500 arasında olmalıdır.")] public decimal MarginPercent { get; set; }
    public string DocumentJson { get; set; } = "{}";
}
