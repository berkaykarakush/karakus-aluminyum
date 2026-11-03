using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Drawing;

// Üretim kapalıyken sistemsiz kaydedilen çizimlere türünün ilk aktif sistemini bir kez atar.
// Bir kez çalışır: sonradan "Seçilmedi" bırakılan çizimlere dokunmaz. Sistemin kuralına uymayan çizim sistemsiz kalır.
public static class LegacyDrawingSystems
{
    public const string MarkerKey = "LegacyDrawingSystemsAssigned";

    public static async Task<int> RunAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.SiteSettingRecords.AnyAsync(x => x.Key == MarkerKey, cancellationToken)) return 0;

        var systems = (await db.ProfileSystems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken))
            .GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.First());
        var assigned = 0;
        foreach (var design in await db.WindowDesigns.Where(x => x.ProfileSystemId == null).ToListAsync(cancellationToken))
        {
            if (!systems.TryGetValue(design.Category, out var system)) continue;
            if (WindowDrawings.Validate(design.Category, design.WidthMm, design.HeightMm, design.Shape, design.ShapeParamMm, design.Quantity, design.LayoutJson, design.ParamsJson).Count > 0) continue;
            if (DrawingCalculation.Calculate(design, system) is not { IsValid: true }) continue;
            design.ProfileSystemId = system.Id;
            assigned++;
        }
        db.SiteSettingRecords.Add(new SiteSettingRecord { Key = MarkerKey, Value = DateTime.UtcNow.ToString("O"), Description = $"Eski çizimlere profil sistemi atandı ({assigned})" });
        await db.SaveChangesAsync(cancellationToken);
        return assigned;
    }
}
