using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KarakusAluminyum.Web.Models.Crm;

public enum QuoteStatus { Draft = 0, Sent = 1, Accepted = 2, Rejected = 3, Expired = 4, Converted = 5 }

public sealed class Quote
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string QuoteNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntilUtc { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [Column(TypeName = "numeric(5,2)")] public decimal TaxRate { get; set; } = 20m;
    [Column(TypeName = "numeric(5,2)")] public decimal DiscountRate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GrandTotal { get; set; }
    public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
}

public sealed class QuoteItem
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public Quote? Quote { get; set; }
    [Required, StringLength(200)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; } = 1;
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Column(TypeName = "numeric(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal LineTotal { get; set; }
}
