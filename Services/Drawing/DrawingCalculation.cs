using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

// Çizimi ürün türüne göre doğru hesap motoruna yönlendirir.
public static class DrawingCalculation
{
    public static WindowCalculation? Calculate(WindowDesign design, ProfileSystem system)
        => Calculate(design.Category, design.WidthMm, design.HeightMm, design.Shape, design.ShapeParamMm, design.Quantity, design.LayoutJson, design.ParamsJson, system);

    public static WindowCalculation? Calculate(string category, int width, int height, string shape, int shapeParam, int quantity, string? layoutJson, string? paramsJson, ProfileSystem system)
    {
        var priced = InTry(system);
        WindowCalculation? result;
        if (ProductCategories.IsWindow(category))
        {
            var root = WindowLayout.Parse(layoutJson);
            result = root is null ? null : WindowCalculator.Calculate(new WindowInput(width, height, shape, shapeParam, quantity, root), priced);
        }
        else
        {
            var input = WindowDrawings.ParseParams(paramsJson);
            result = input is null ? null : ProductCalculator.Calculate(category, width, height, quantity, input, priced, WindowDrawings.ParseSettings(priced.SettingsJson));
        }
        if (result is not null && system.PriceCurrency is not (null or "TRY") && system.RateToTry is null)
        {
            // Kur yokken fiyatlar 0 hesaplanır; "fiyat girilmemiş" uyarıları yerine tek uyarı verilir.
            result.Warnings.RemoveAll(w => w.Contains("girilmemiş"));
            result.Warnings.Add($"{system.PriceCurrency} kuru girilmemiş; fiyat hesaplanamadı (Tanımlar › Fiyat listeleri).");
        }
        return result;
    }

    // Sistem fiyatları döviz ise hesaptan önce güncel kurla TL'ye çevrilir (yuvarlama TL'de yapılsın diye).
    // Kur girilmemişse fiyatlar 0 alınır. Asıl kayıt değişmez; hesap bir kopya üzerinde yapılır.
    public static ProfileSystem InTry(ProfileSystem system)
    {
        if (system.PriceCurrency is null or "TRY") return system;
        var rate = system.RateToTry ?? 0;
        var copy = System.Text.Json.JsonSerializer.Deserialize<ProfileSystem>(System.Text.Json.JsonSerializer.Serialize(system))!;
        copy.PriceCurrency = "TRY";
        copy.RateToTry = 1;
        copy.FramePricePerM *= rate; copy.SashPricePerM *= rate; copy.MullionPricePerM *= rate; copy.BeadPricePerM *= rate; copy.PanelPricePerM2 *= rate;
        copy.HardwareOpenPrice *= rate; copy.HardwareTiltPrice *= rate; copy.HardwareSlidePrice *= rate; copy.HardwareDoorPrice *= rate;
        var settings = WindowDrawings.ParseSettings(system.SettingsJson);
        settings.LamellaPricePerM2 *= rate; settings.GuidePricePerM *= rate; settings.BoxPricePerM *= rate; settings.MotorPrice *= rate; settings.ManualPrice *= rate;
        settings.PostPricePerM *= rate; settings.RailPricePerM *= rate; settings.BarPricePerM *= rate; settings.PanelProfilePricePerM *= rate; settings.PanelHardwarePrice *= rate;
        copy.SettingsJson = WindowDrawings.Serialize(settings);
        return copy;
    }
}
