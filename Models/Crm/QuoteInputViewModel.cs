using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models.Crm;

public sealed class QuoteInputViewModel
{
    [Required] public int CustomerId { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    public DateTime? ValidUntilUtc { get; set; }
    [Range(0, 100)] public decimal TaxRate { get; set; } = 20m;
    [Range(0, 100)] public decimal DiscountRate { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public List<QuoteItemInput> Items { get; set; } = Enumerable.Range(0, 6).Select(_ => new QuoteItemInput()).ToList();
}

public sealed class QuoteItemInput
{
    [StringLength(200)] public string Description { get; set; } = "";
    [Range(0, 100000000)] public decimal Quantity { get; set; }
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Range(0, 100000000)] public decimal UnitPrice { get; set; }
}
