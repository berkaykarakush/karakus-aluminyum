using System.Text.Json;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

// Çizimi ürün türüne göre doğru hesap motoruna yönlendirir.
public static class DrawingCalculation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ProductSettings ParseSettings(string? json) => Parse<ProductSettings>(json) ?? new ProductSettings();
    public static ProductParams? ParseParams(string? json) => Parse<ProductParams>(json);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static WindowCalculation? Calculate(WindowDesign design, ProfileSystem system)
        => Calculate(design.Category, design.WidthMm, design.HeightMm, design.Shape, design.ShapeParamMm, design.Quantity, design.LayoutJson, design.ParamsJson, system);

    public static WindowCalculation? Calculate(string category, int width, int height, string shape, int shapeParam, int quantity, string? layoutJson, string? paramsJson, ProfileSystem system)
    {
        if (ProductCategories.IsWindow(category))
        {
            var root = WindowLayout.Parse(layoutJson);
            return root is null ? null : WindowCalculator.Calculate(new WindowInput(width, height, shape, shapeParam, quantity, root), system);
        }
        var input = ParseParams(paramsJson);
        return input is null ? null : ProductCalculator.Calculate(category, width, height, quantity, input, system, ParseSettings(system.SettingsJson));
    }

    // Tarayıcıdaki çizimin ihtiyaç duyduğu veri.
    public static object Drawing(WindowDesign row, ProfileSystem s) => new
    {
        category = row.Category, widthMm = row.WidthMm, heightMm = row.HeightMm, shape = row.Shape, shapeParamMm = row.ShapeParamMm, colorHex = row.ColorHex,
        root = ProductCategories.IsWindow(row.Category) ? WindowLayout.Parse(row.LayoutJson) : null,
        @params = ProductCategories.IsWindow(row.Category) ? null : ParseParams(row.ParamsJson),
        settings = ParseSettings(s.SettingsJson),
        sys = new { frame = s.FrameWidthMm, sash = s.SashWidthMm, mullion = s.MullionWidthMm, overlap = s.SashOverlapMm }
    };
}
