using System.Text.Json;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record TakeoffLine(Product Product, decimal Quantity, IReadOnlyList<string> Sources);
public sealed record TakeoffResult(IReadOnlyList<TakeoffLine> Lines, IReadOnlyList<string> Uncovered, MaterialList Materials);

// Seri kataloğu kurallarıyla çizimlerden stok kartı bazında malzeme ihtiyacı (metraj).
// Profil boyları kesim planından (optimizasyon) gelir; diğer esaslar pozun adediyle çarpılır.
public static class MaterialTakeoff
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] CountUnits = { "adet", "boy", "takım", "paket", "kutu", "tüp", "rulo", "koli" };

    public static List<MaterialRule> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<MaterialRule>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string SerializeRules(IEnumerable<MaterialRule> rules) => JsonSerializer.Serialize(rules, Json);

    // Adet, boy, takım gibi bölünemeyen birimlerde toplam yukarı yuvarlanır.
    public static bool IsCountUnit(string? unit) => CountUnits.Contains((unit ?? "").Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")));

    public static TakeoffResult Build(IEnumerable<WindowDesign> designs, IReadOnlyDictionary<int, Product> products)
    {
        var materials = DrawingMaterials.Build(designs);
        var totals = new Dictionary<int, (decimal Quantity, List<string> Sources)>();
        var uncovered = new List<string>();

        foreach (var group in materials.Calculated.GroupBy(x => x.Design.ProfileSystemId))
        {
            var items = group.ToList();
            var system = items[0].Design.ProfileSystem!;
            var rules = ParseRules(system.MaterialsJson);
            var plans = materials.Systems.First(x => x.System.Id == system.Id).Plans;
            var profiles = plans.Select(p => p.Profile).ToList();
            var hardware = items.SelectMany(x => x.Calculation.Hardware.Select(h => h.Name)).Distinct().ToList();

            if (rules.Count == 0)
            {
                uncovered.Add($"{system.Name}: malzeme kuralı tanımlı değil (Çizim › Sistemler).");
                continue;
            }
            foreach (var profile in profiles.Where(p => !rules.Any(r => r.Basis is MaterialBases.ProfileBars or MaterialBases.ProfileMetres && Same(r.Key, p))))
                uncovered.Add($"{system.Name}: \"{profile}\" profili için stok kartı yok.");
            foreach (var name in hardware.Where(h => !rules.Any(r => r.Basis == MaterialBases.Hardware && Same(r.Key, h))))
                uncovered.Add($"{system.Name}: \"{name}\" donanımı için stok kartı yok.");

            foreach (var rule in rules)
            {
                if (!products.TryGetValue(rule.ProductId, out var product))
                {
                    uncovered.Add($"{system.Name}: kuraldaki stok kartı bulunamadı (silinmiş olabilir).");
                    continue;
                }
                var basis = Value(rule, items, plans, system);
                if (basis <= 0) continue;
                var quantity = basis * rule.Factor;
                if (!totals.TryGetValue(product.Id, out var total)) total = (0, new List<string>());
                var source = $"{system.Name}: {MaterialBases.Label(rule.Basis)}{(string.IsNullOrWhiteSpace(rule.Key) ? "" : " " + rule.Key)}";
                if (!total.Sources.Contains(source)) total.Sources.Add(source);
                totals[product.Id] = (total.Quantity + quantity, total.Sources);
            }
        }

        var lines = totals.Select(x =>
            {
                var product = products[x.Key];
                var quantity = IsCountUnit(product.Unit) ? Math.Ceiling(x.Value.Quantity - 0.0005m) : decimal.Round(x.Value.Quantity, 3, MidpointRounding.AwayFromZero);
                return new TakeoffLine(product, quantity, x.Value.Sources);
            })
            .Where(x => x.Quantity > 0)
            .OrderBy(x => x.Product.Code ?? "").ThenBy(x => x.Product.Name).ToList();
        return new TakeoffResult(lines, uncovered.Distinct().ToList(), materials);
    }

    private static bool Same(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.CurrentCultureIgnoreCase);

    private static decimal Value(MaterialRule rule, List<(WindowDesign Design, WindowCalculation Calculation)> items, IReadOnlyList<BarPlan> plans, ProfileSystem system)
    {
        decimal Sum(Func<WindowDesign, WindowCalculation, decimal> perUnit) => items.Sum(x => perUnit(x.Design, x.Calculation) * x.Design.Quantity);
        return rule.Basis switch
        {
            MaterialBases.ProfileBars => plans.Where(p => Same(rule.Key, p.Profile)).Sum(p => p.Bars.Count),
            MaterialBases.ProfileMetres => Sum((_, c) => c.Cuts.Where(x => Same(rule.Key, x.Profile)).Sum(x => x.LengthMm * x.Count)) / 1000m * (1 + system.WastePercent / 100m),
            MaterialBases.Hardware => Sum((_, c) => c.Hardware.Where(x => Same(rule.Key, x.Name)).Sum(x => x.Count)),
            MaterialBases.Piece => Sum((_, _) => 1),
            MaterialBases.Perimeter => Sum((d, _) => 2m * (d.WidthMm + d.HeightMm) / 1000m),
            MaterialBases.Area => Sum((d, _) => d.WidthMm * (decimal)d.HeightMm / 1_000_000m),
            MaterialBases.GlassArea => Sum((_, c) => c.Glass.Sum(g => g.AreaM2)), // AreaM2 parçanın tüm adetini kapsar
            MaterialBases.GlassPerimeter => Sum((_, c) => c.Glass.Sum(g => 2m * (g.WidthMm + g.HeightMm) / 1000m * g.Count)),
            _ => 0
        };
    }
}
