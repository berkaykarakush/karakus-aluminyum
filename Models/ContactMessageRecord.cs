using System.ComponentModel.DataAnnotations;
namespace KarakusAluminyum.Web.Models;
public sealed class ContactMessage
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(120)] public string ContactInfo { get; set; } = "";
    [StringLength(120)] public string? ProjectLocation { get; set; }
    [StringLength(80)] public string? Service { get; set; }
    [Required, StringLength(3000)] public string Message { get; set; } = "";
    public bool PrivacyAccepted { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
