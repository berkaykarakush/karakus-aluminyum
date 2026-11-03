using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Documents;

// Pozun tek adetlik maliyeti: sistem hesabı (profil, panel, donanım, işçilik) + cam alanı × seçilen cam fiyatı.
public sealed record DesignCost(int DesignId, string Name, int Quantity, decimal BaseCost, decimal GlassAreaM2, string? Problem)
{
    public decimal UnitCost(decimal glassPricePerM2) => LineCost(glassPricePerM2, null, null);

    // Taşerona verilen parça elle girilen tutarla, diğeri sistem hesabıyla gelir. Hesaplanamayan pozda sistem parçası sıfırdır.
    public decimal LineCost(decimal glassPricePerM2, decimal? manualBase, decimal? manualGlass)
    {
        var baseCost = manualBase ?? (Problem is null ? BaseCost : 0m);
        var glassCost = manualGlass ?? (Problem is null ? GlassAreaM2 * glassPricePerM2 : 0m);
        return decimal.Round(baseCost + glassCost, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed record GlassOption(int Id, string Name, decimal PricePerM2, bool Active);
public sealed record DesignCostOption(int Id, decimal BaseCost, decimal GlassAreaM2, string? Problem);

public static class QuotePricing
{
    public static async Task<List<(WindowDesign Design, DesignCost Cost)>> ForListAsync(AppDbContext db, int listId)
    {
        var designs = await db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem)
            .Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        return designs.Select(d => (d, Cost(d))).ToList();
    }

    public static DesignCost Cost(WindowDesign design)
    {
        if (design.ProfileSystem is null) return new(design.Id, design.Name, design.Quantity, 0, 0, "Profil sistemi seçilmemiş, maliyet hesaplanamadı.");
        var calculation = DrawingCalculation.Calculate(design, design.ProfileSystem);
        if (calculation is null || !calculation.IsValid)
            return new(design.Id, design.Name, design.Quantity, 0, 0, calculation?.Errors.FirstOrDefault() ?? "Çizim verisi okunamadı.");
        return new(design.Id, design.Name, design.Quantity, calculation.UnitCost, calculation.Glass.Sum(g => g.AreaM2), null);
    }

    // Çizim listesinin varsayılan marjı: pozlarda en çok kullanılan sistemin kâr yüzdesi.
    public static async Task<decimal> DefaultMarginAsync(AppDbContext db, int listId)
        => await db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == listId && x.ProfileSystem != null)
            .GroupBy(x => x.ProfileSystem!.MarginPercent).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefaultAsync();
}
