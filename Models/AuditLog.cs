using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    [Required, StringLength(450)] public string ActorUserId { get; set; } = "system";
    [Required, StringLength(256)] public string ActorEmail { get; set; } = "system";
    [Required, StringLength(100)] public string Action { get; set; } = "";
    [Required, StringLength(100)] public string EntityType { get; set; } = "";
    [StringLength(100)] public string? EntityId { get; set; }
    [StringLength(2000)] public string? Details { get; set; }
    [StringLength(64)] public string? IpAddress { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
