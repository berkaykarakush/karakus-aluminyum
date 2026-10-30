using System.Text.Json;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

public static class WindowLayout
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public static LayoutNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<LayoutNode>(json, Options); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(LayoutNode node) => JsonSerializer.Serialize(node, Options);
}

// Çizimler yalnızca ölçülü şekildir: imalat düşümü, kesim veya maliyet hesabı yapılmaz.
// Burada yalnızca çizilebilirlik denetlenir. Bölme ölçüleri toplamı, üst bölümün ölçüsüne eşittir.
public static class WindowDrawings
{
    private const int MaxLeaves = 100;
    private const int MaxDepth = 12;
    private const int MinSize = 200;
    private const int MaxWidth = 100000;
    private const int MaxHeight = 20000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ProductParams? ParseParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ProductParams>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static List<string> Validate(string category, int width, int height, string shape, int shapeParam, int quantity, string? layoutJson, string? paramsJson)
    {
        var errors = new List<string>();
        if (!ProductCategories.All.Contains(category)) { errors.Add("Geçersiz ürün türü."); return errors; }
        if (width is < MinSize or > MaxWidth || height is < MinSize or > MaxHeight) errors.Add("Ölçüler en az 20 cm olmalıdır; genişlik en fazla 100 m, yükseklik en fazla 20 m olabilir.");
        if (quantity < 1) errors.Add("Adet en az 1 olmalıdır.");
        if (errors.Count > 0) return errors;

        if (ProductCategories.IsWindow(category)) ValidateWindow(width, height, shape, shapeParam, layoutJson, errors);
        else ValidateProduct(category, paramsJson, errors);
        return errors;
    }

    private static void ValidateWindow(int width, int height, string shape, int shapeParam, string? layoutJson, List<string> errors)
    {
        if (!WindowShapes.All.Contains(shape)) errors.Add("Geçersiz şekil.");
        if (shape == WindowShapes.Arch && (shapeParam <= 0 || shapeParam > width / 2 || shapeParam >= height))
            errors.Add("Kemer yüksekliği sıfırdan büyük, genişliğin yarısından ve toplam yükseklikten küçük olmalıdır.");
        if (shape is WindowShapes.SlopeLeft or WindowShapes.SlopeRight && (shapeParam <= 0 || shapeParam >= height))
            errors.Add("Alçak kenar yüksekliği sıfırdan büyük, toplam yükseklikten küçük olmalıdır.");

        var root = WindowLayout.Parse(layoutJson);
        if (root is null) { errors.Add("Çizim verisi okunamadı."); return; }
        var leaves = 0;
        Walk(root, width, height, 0, ref leaves, errors);
    }

    private static void Walk(LayoutNode node, decimal width, decimal height, int depth, ref int leaves, List<string> errors)
    {
        if (depth > MaxDepth) { errors.Add("Bölme derinliği çok fazla."); return; }
        if (node.Split is null)
        {
            if (++leaves > MaxLeaves) errors.Add("Çok fazla bölme var.");
            if (!WindowOpenings.All.Contains(node.Opening)) errors.Add("Geçersiz açılım tipi.");
            if (node.Fill is not ("glass" or "panel")) errors.Add("Geçersiz dolgu tipi.");
            return;
        }
        if (node.Split is not ("v" or "h")) { errors.Add("Geçersiz bölme yönü."); return; }

        var children = node.Children ?? new();
        var sizes = node.Sizes ?? new();
        if (children.Count < 2 || sizes.Count != children.Count) { errors.Add("Bölme verisi tutarsız."); return; }
        if (sizes.Any(s => s <= 0 || s > MaxWidth)) { errors.Add("Bölme ölçüleri sıfırdan büyük ve dış ölçü içinde olmalıdır."); return; }

        var total = node.Split == "v" ? width : height;
        if (Math.Abs(sizes.Sum() - total) > 1) { errors.Add("Bölme ölçülerinin toplamı dış ölçüye eşit olmalıdır."); return; }
        for (var i = 0; i < children.Count; i++)
            Walk(children[i], node.Split == "v" ? sizes[i] : width, node.Split == "v" ? height : sizes[i], depth + 1, ref leaves, errors);
    }

    private static void ValidateProduct(string category, string? paramsJson, List<string> errors)
    {
        var input = ParseParams(paramsJson);
        if (input is null) { errors.Add("Çizim seçenekleri okunamadı."); return; }
        switch (category)
        {
            case ProductCategories.Shutter when input.Drive is not ("motor" or "manual"):
                errors.Add("Geçersiz panjur tipi.");
                break;
            case ProductCategories.Railing when input.Infill is not ("glass" or "bars"):
                errors.Add("Geçersiz korkuluk dolgusu.");
                break;
            case ProductCategories.Railing when input.PostSpacingMm is < 0 or (> 0 and < 100):
                errors.Add("Dikme aralığı en az 10 cm olmalıdır.");
                break;
            case ProductCategories.GlassBalcony when input.OpenType is not ("sliding" or "folding"):
                errors.Add("Geçersiz cam balkon tipi.");
                break;
            case ProductCategories.GlassBalcony when input.PanelCount is < 1 or > 30:
                errors.Add("Panel sayısı 1-30 arasında olmalıdır.");
                break;
        }
    }

    // Tarayıcıdaki çizimin ihtiyaç duyduğu veri.
    public static object Drawing(WindowDesign row) => new
    {
        category = row.Category, widthMm = row.WidthMm, heightMm = row.HeightMm, shape = row.Shape, shapeParamMm = row.ShapeParamMm, colorHex = row.ColorHex,
        root = ProductCategories.IsWindow(row.Category) ? WindowLayout.Parse(row.LayoutJson) : null,
        @params = ProductCategories.IsWindow(row.Category) ? null : ParseParams(row.ParamsJson)
    };
}
