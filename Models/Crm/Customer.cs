using System.ComponentModel.DataAnnotations;
using KarakusAluminyum.Web.Models.Accounting;

namespace KarakusAluminyum.Web.Models.Crm;

public enum CustomerKind { Individual = 0, Company = 1 }
public enum CustomerStatus { Lead = 0, Active = 1, Passive = 2 }

public sealed class Customer
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    public CustomerKind Kind { get; set; } = CustomerKind.Company;
    public CustomerStatus Status { get; set; } = CustomerStatus.Lead;
    public PartyType PartyType { get; set; } = PartyType.Customer;
    [StringLength(120)] public string? ContactPerson { get; set; }
    [EmailAddress, StringLength(180)] public string? Email { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(40)] public string? TaxNumber { get; set; }
    [StringLength(120)] public string? TaxOffice { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? District { get; set; }
    [StringLength(80)] public string? Source { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
