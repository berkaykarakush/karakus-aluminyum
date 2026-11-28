using System.Security.Claims;
using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Documents;

public sealed record StaffName(string FullName, string? JobTitle);

// Belgelere otomatik yazılan kişiler: ölçüyü alan (keşif) ve imza yetkilisi (teklif, sözleşme).
public static class DocumentPeople
{
    // Giriş yapan kullanıcının ad soyadı; kullanıcı ayarlarında ad yazılmamışsa null döner ve şablondaki yetkili kullanılır.
    public static async Task<StaffName?> CurrentAsync(AppDbContext db, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.FullName, x.JobTitle }).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(user?.FullName) ? null : new StaffName(user.FullName.Trim(), string.IsNullOrWhiteSpace(user.JobTitle) ? null : user.JobTitle.Trim());
    }

    // Formlardaki öneri listesi: adı yazılmış, kilitli olmayan kullanıcılar.
    public static async Task<List<string>> StaffNamesAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var names = await db.Users.AsNoTracking().Where(x => x.FullName != null && x.FullName != "" && (x.LockoutEnd == null || x.LockoutEnd <= now)).Select(x => x.FullName!).ToListAsync();
        return names.Select(x => x.Trim()).Distinct().OrderBy(x => x, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true)).ToList();
    }

    // Belgedeki pozların geldiği keşiflerde ölçüyü alan kişiler, sırasıyla ve bir kez: "Ahmet Yılmaz, Mehmet Kaya".
    public static async Task<string?> SurveyorsAsync(AppDbContext db, int? drawingListId, IReadOnlyCollection<int> designIds)
    {
        var lists = new List<int>();
        if (drawingListId is int listId) lists.Add(listId);
        if (designIds.Count > 0) lists.AddRange(await db.WindowDesigns.AsNoTracking().Where(x => designIds.Contains(x.Id)).Select(x => x.DrawingListId).Distinct().ToListAsync());
        if (lists.Count == 0) return null;
        var surveyors = await db.SiteSurveys.AsNoTracking().Where(x => x.DrawingListId != null && lists.Contains(x.DrawingListId.Value) && x.Surveyor != null && x.Surveyor != "")
            .OrderBy(x => x.Id).Select(x => x.Surveyor!).ToListAsync();
        return Join(surveyors);
    }

    // Keşfe giden alanına birden çok kişi virgülle yazılabilir; her ad bir kez yazılır.
    public static string? Join(IEnumerable<string> names)
    {
        var seen = new List<string>();
        foreach (var name in names.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase)) seen.Add(name);
        return seen.Count == 0 ? null : string.Join(", ", seen);
    }
}
