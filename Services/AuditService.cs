using System.Security.Claims;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;

namespace KarakusAluminyum.Web.Services;

public interface IAuditService
{
    Task RecordAsync(string action, string entityType, string? entityId = null, string? details = null, CancellationToken cancellationToken = default);
}

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    public AuditService(AppDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

    public async Task RecordAsync(string action, string entityType, string? entityId = null, string? details = null, CancellationToken cancellationToken = default)
    {
        var context = _http.HttpContext;
        var user = context?.User;
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system",
            ActorEmail = user?.Identity?.Name ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = context?.Connection.RemoteIpAddress?.ToString(),
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
