using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record SampleMaterial(string Name, string Unit, string Basis, string? Key, decimal Factor, decimal Price);

// Örnek seri kataloğu: elle giriş için başlangıç noktası. Katsayılar sektörde yaygın tahminlerdir, firma kendi serisine göre düzeltir.
// Profil alış fiyatı, sistemdeki metre fiyatı × boy uzunluğudur.
public static class SampleCatalog
{
    public static IEnumerable<SampleMaterial> For(ProfileSystem system)
    {
        var bar = system.BarLengthMm / 1000m;
        decimal BarPrice(decimal perMetre) => decimal.Round(perMetre * bar, 2);
        var settings = WindowDrawings.ParseSettings(system.SettingsJson);
        switch (system.Category)
        {
            case ProductCategories.Shutter:
                yield return new("Lamel (örnek)", "Metre", MaterialBases.ProfileMetres, "Lamel", 1, 0);
                yield return new("Yan dikme profili (örnek)", "Boy", MaterialBases.ProfileBars, "Yan dikme", 1, BarPrice(settings.GuidePricePerM));
                yield return new("Panjur kutusu (örnek)", "Boy", MaterialBases.ProfileBars, "Kutu", 1, BarPrice(settings.BoxPricePerM));
                yield return new("Panjur motoru (örnek)", "Adet", MaterialBases.Hardware, "Motor", 1, settings.MotorPrice);
                yield return new("Manuel makara takımı (örnek)", "Takım", MaterialBases.Hardware, "Manuel kayış / makara", 1, settings.ManualPrice);
                yield return new("Lamel tapası (örnek)", "Adet", MaterialBases.Area, null, 50, 0); // 39'luk lamelde m² başına ~25 lamel × 2 tapa
                break;
            case ProductCategories.Railing:
                yield return new("Korkuluk dikmesi (örnek)", "Boy", MaterialBases.ProfileBars, "Dikme", 1, BarPrice(settings.PostPricePerM));
                yield return new("Küpeşte profili (örnek)", "Boy", MaterialBases.ProfileBars, "Küpeşte", 1, BarPrice(settings.RailPricePerM));
                yield return new("Emniyet çubuğu (örnek)", "Boy", MaterialBases.ProfileBars, "Emniyet çubuğu", 1, BarPrice(settings.BarPricePerM));
                yield return new("Dikme ayağı ve dübel takımı (örnek)", "Takım", MaterialBases.Perimeter, null, 0.42m, 0); // ~120 cm aralıkla
                break;
            case ProductCategories.GlassBalcony:
                yield return new("Cam balkon rayı (örnek)", "Boy", MaterialBases.ProfileBars, "Ray", 1, BarPrice(settings.RailPricePerM));
                yield return new("Cam balkon kanat profili (örnek)", "Boy", MaterialBases.ProfileBars, "Kanat profili", 1, BarPrice(settings.PanelProfilePricePerM));
                yield return new("Sürme kanat donanımı (örnek)", "Takım", MaterialBases.Hardware, "Sürme kanat donanımı", 1, settings.PanelHardwarePrice);
                yield return new("Katlanır kanat donanımı (örnek)", "Takım", MaterialBases.Hardware, "Katlanır kanat donanımı", 1, settings.PanelHardwarePrice);
                yield return new("Cam fitili (örnek)", "Metre", MaterialBases.GlassPerimeter, null, 1, 0);
                break;
            default:
                yield return new("Kasa profili (örnek)", "Boy", MaterialBases.ProfileBars, WindowCalculator.FrameProfile, 1, BarPrice(system.FramePricePerM));
                yield return new("Kanat profili (örnek)", "Boy", MaterialBases.ProfileBars, WindowCalculator.SashProfile, 1, BarPrice(system.SashPricePerM));
                yield return new("Kayıt profili (örnek)", "Boy", MaterialBases.ProfileBars, WindowCalculator.MullionProfile, 1, BarPrice(system.MullionPricePerM));
                yield return new("Cam çıtası (örnek)", "Boy", MaterialBases.ProfileBars, WindowCalculator.BeadProfile, 1, BarPrice(system.BeadPricePerM));
                yield return new("Açılır kanat donanımı (örnek)", "Takım", MaterialBases.Hardware, "Açılır kanat donanımı", 1, system.HardwareOpenPrice);
                yield return new("Açılır-devrilir donanım (örnek)", "Takım", MaterialBases.Hardware, "Açılır-devrilir / vasistas donanımı", 1, system.HardwareTiltPrice);
                yield return new("Sürme donanımı (örnek)", "Takım", MaterialBases.Hardware, "Sürme donanımı", 1, system.HardwareSlidePrice);
                yield return new("Kapı donanımı (örnek)", "Takım", MaterialBases.Hardware, "Kapı donanımı", 1, system.HardwareDoorPrice);
                yield return new("Cam contası (örnek)", "Metre", MaterialBases.GlassPerimeter, null, 2, 0);        // iç ve dış conta
                yield return new("Kanat contası (örnek)", "Metre", MaterialBases.ProfileMetres, WindowCalculator.SashProfile, 1, 0);
                yield return new("Köşe takozu (örnek)", "Adet", MaterialBases.Piece, null, 4, 0);
                yield return new("Silikon (örnek)", "Tüp", MaterialBases.Perimeter, null, 0.1m, 0);             // ~10 m çevreye bir tüp
                yield return new("Montaj dübeli (örnek)", "Adet", MaterialBases.Perimeter, null, 2.5m, 0);       // ~40 cm aralıkla
                break;
        }
    }
}
