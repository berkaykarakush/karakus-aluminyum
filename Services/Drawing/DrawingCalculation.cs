using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

// Çizimi ürün türüne göre doğru hesap motoruna yönlendirir.
public static class DrawingCalculation
{
    public static WindowCalculation? Calculate(WindowDesign design, ProfileSystem system)
        => Calculate(design.Category, design.WidthMm, design.HeightMm, design.Shape, design.ShapeParamMm, design.Quantity, design.LayoutJson, design.ParamsJson, system);

    public static WindowCalculation? Calculate(string category, int width, int height, string shape, int shapeParam, int quantity, string? layoutJson, string? paramsJson, ProfileSystem system)
    {
        if (ProductCategories.IsWindow(category))
        {
            var root = WindowLayout.Parse(layoutJson);
            return root is null ? null : WindowCalculator.Calculate(new WindowInput(width, height, shape, shapeParam, quantity, root), system);
        }
        var input = WindowDrawings.ParseParams(paramsJson);
        return input is null ? null : ProductCalculator.Calculate(category, width, height, quantity, input, system, WindowDrawings.ParseSettings(system.SettingsJson));
    }
}
