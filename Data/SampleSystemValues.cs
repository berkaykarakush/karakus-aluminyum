using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Data;

// Örnek sistemlere internetten derlenmiş tahmini başlangıç değerleri (Kasım 2026, TL, KDV hariç).
// Gerçek değerler panelden girilecek. Yalnızca adı hâlâ seed'deki örnek adı olan, yani dokunulmamış kayıtlar güncellenir.
// Kaynaklar: Aden Yapı 2025-6 PVC profil listesi (70 mm seri, beyaz, ~%30 enflasyon eklendi), ESN Alüminyum 175–350 TL/kg
// (260 TL/kg × tipik kg/m), İstanbul Camcı / Kolaycam ısıcam m² fiyatları, Poledoor / Motodoor SL-39 lamel metre fiyatları.
public static class SampleSystemValues
{
    public const string MarkerKey = "SampleSystemValues";
    public const string Version = "2026-11";
    public const string EstimateTag = "(tahmini"; // panodaki kurulum listesi bu kayıtları gerçek fiyat saymaz
    public const string LegacyAluminumName = "Örnek Sistem (kendi profil değerlerinizi girin)";
    public const string LegacyGlassName = "Örnek Isıcam 4+16+4 (fiyat girin)";
    public static string LegacyName(string category) => $"Örnek {ProductCategories.Label(category)} (değerleri girin)";

    public static async Task ApplyAsync(AppDbContext db)
    {
        var marker = await db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == MarkerKey);
        if (marker?.Value == Version) return;
        if (marker is null) db.SiteSettingRecords.Add(marker = new SiteSettingRecord { Key = MarkerKey, Description = "Örnek sistemlere tahmini değerler yazıldı" });
        marker.Value = Version;

        var legacyNames = ProductCategories.All.Select(LegacyName).Append(LegacyAluminumName).ToList();
        foreach (var system in await db.ProfileSystems.Where(x => legacyNames.Contains(x.Name)).ToListAsync())
            Apply(system);
        foreach (var glass in await db.GlassTypes.Where(x => x.Name == LegacyGlassName).ToListAsync())
        {
            glass.Name = "Isıcam 4+16+4 (tahmini fiyat)";
            glass.PricePerM2 = 1400;
        }
        await db.SaveChangesAsync();
    }

    private static void Apply(ProfileSystem s)
    {
        s.WastePercent = 5;
        s.LaborPercent = 30;
        s.MarginPercent = 25;
        s.KerfMm = 4;
        switch (s.Category)
        {
            case ProductCategories.Pvc:
                s.Name = "PVC 70 mm 5 odacıklı (tahmini değerler)";
                (s.FrameWidthMm, s.SashWidthMm, s.MullionWidthMm, s.SashOverlapMm, s.GlassClearanceMm, s.MullionFitMm, s.WeldAllowanceMm, s.BarLengthMm) = (62, 78, 82, 8, 5, 0, 3, 6500);
                (s.FramePricePerM, s.SashPricePerM, s.MullionPricePerM, s.BeadPricePerM, s.PanelPricePerM2) = (150, 170, 175, 37, 700);
                (s.HardwareOpenPrice, s.HardwareTiltPrice, s.HardwareSlidePrice, s.HardwareDoorPrice) = (1200, 2200, 1600, 4000);
                break;
            case ProductCategories.Shutter:
                s.Name = "Alüminyum panjur SL-39 (tahmini değerler)";
                s.BarLengthMm = 6000;
                s.SettingsJson = WindowDrawings.Serialize(new ProductSettings { BoxHeightMm = 165, LamellaHeightMm = 39, GuideDeductMm = 25, LamellaPricePerM2 = 3650, GuidePricePerM = 250, BoxPricePerM = 900, MotorPrice = 3500, ManualPrice = 600 });
                break;
            case ProductCategories.Railing:
                s.Name = "Alüminyum korkuluk (tahmini değerler)";
                s.BarLengthMm = 6000;
                s.GlassClearanceMm = 5;
                s.SettingsJson = WindowDrawings.Serialize(new ProductSettings { PostWidthMm = 40, PostSpacingMm = 1200, RailHeightMm = 40, BarSpacingMm = 110, PostPricePerM = 350, RailPricePerM = 300, BarPricePerM = 120 });
                break;
            case ProductCategories.GlassBalcony:
                s.Name = "Katlanır cam balkon (tahmini değerler)";
                s.BarLengthMm = 6000;
                (s.SashWidthMm, s.GlassClearanceMm) = (40, 5);
                s.SettingsJson = WindowDrawings.Serialize(new ProductSettings { RailHeightMm = 40, PanelOverlapMm = 20, RailPricePerM = 600, PanelProfilePricePerM = 350, PanelHardwarePrice = 900 });
                break;
            default:
                s.Name = "Isı yalıtımlı alüminyum (tahmini değerler)";
                (s.FrameWidthMm, s.SashWidthMm, s.MullionWidthMm, s.SashOverlapMm, s.GlassClearanceMm, s.MullionFitMm, s.WeldAllowanceMm, s.BarLengthMm) = (55, 75, 80, 8, 5, 0, 0, 6000);
                (s.FramePricePerM, s.SashPricePerM, s.MullionPricePerM, s.BeadPricePerM, s.PanelPricePerM2) = (325, 375, 405, 60, 900);
                (s.HardwareOpenPrice, s.HardwareTiltPrice, s.HardwareSlidePrice, s.HardwareDoorPrice) = (1500, 2600, 1800, 4500);
                break;
        }
    }
}
