using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

public sealed record SystemMaterials(ProfileSystem System, IReadOnlyList<BarPlan> Plans);
public sealed record GlassTotal(string Name, decimal WidthMm, decimal HeightMm, bool Shaped, int Count, decimal AreaM2);
public sealed record HardwareTotal(string Name, int Count);
public sealed record SkippedDrawing(WindowDesign Design, IReadOnlyList<string> Errors);
// Hesaplanan çizimlerin adetlerle çarpılmış maliyeti; camlar fiyata dahil değildir (imalat föyündeki gibi).
public sealed record MaterialCost(decimal Profile, decimal Panel, decimal Hardware, decimal Labor, decimal Cost, decimal Price);

public sealed record MaterialList(
    IReadOnlyList<(WindowDesign Design, WindowCalculation Calculation)> Calculated,
    IReadOnlyList<SystemMaterials> Systems,
    IReadOnlyList<GlassTotal> Glass,
    IReadOnlyList<GlassTotal> Panels,
    IReadOnlyList<HardwareTotal> Hardware,
    IReadOnlyList<WindowDesign> WithoutSystem,
    IReadOnlyList<SkippedDrawing> Invalid,
    MaterialCost Cost);

// Listedeki çizimlerin toplam malzemesi: kaç boy profil, hangi cam ve panel, ne kadar donanım. Adetler dahildir.
public static class DrawingMaterials
{
    public static MaterialList Build(IEnumerable<WindowDesign> designs)
    {
        var calculated = new List<(WindowDesign Design, WindowCalculation Calculation)>();
        var withoutSystem = new List<WindowDesign>();
        var invalid = new List<SkippedDrawing>();
        foreach (var design in designs)
        {
            if (design.ProfileSystem is null) { withoutSystem.Add(design); continue; }
            var calculation = DrawingCalculation.Calculate(design, design.ProfileSystem);
            if (calculation is null) invalid.Add(new SkippedDrawing(design, new[] { "Çizim verisi okunamadı." }));
            else if (!calculation.IsValid) invalid.Add(new SkippedDrawing(design, calculation.Errors));
            else calculated.Add((design, calculation));
        }

        // AsNoTracking her satır için ayrı nesne üretebilir; sistemler kimliğe göre gruplanır.
        var systems = calculated.GroupBy(x => x.Design.ProfileSystemId)
            .Select(group => { var system = group.First().Design.ProfileSystem!; return new SystemMaterials(system, Plans(group, system)); })
            .OrderBy(x => x.System.Name).ToList();
        var hardware = calculated.SelectMany(x => x.Calculation.Hardware.Select(h => (h.Name, Count: h.Count * x.Design.Quantity)))
            .GroupBy(x => x.Name).Select(g => new HardwareTotal(g.Key, g.Sum(x => x.Count))).OrderBy(x => x.Name).ToList();
        decimal Sum(Func<WindowCalculation, decimal> unit) => calculated.Sum(x => unit(x.Calculation) * x.Design.Quantity);
        var cost = new MaterialCost(Sum(c => c.ProfileCost), Sum(c => c.PanelCost), Sum(c => c.HardwareCost), Sum(c => c.LaborCost), Sum(c => c.UnitCost), Sum(c => c.UnitPrice));
        return new MaterialList(calculated, systems, Totals(calculated, c => c.Glass), Totals(calculated, c => c.Panels), hardware, withoutSystem, invalid, cost);
    }

    public static List<BarPlan> Plans(IEnumerable<(WindowDesign Design, WindowCalculation Calculation)> items, ProfileSystem system)
    {
        var pieces = new Dictionary<string, List<BarPiece>>();
        foreach (var (design, calculation) in items)
            foreach (var cut in calculation.Cuts)
            {
                if (!pieces.TryGetValue(cut.Profile, out var list)) pieces[cut.Profile] = list = new();
                for (var i = 0; i < cut.Count * design.Quantity; i++) list.Add(new BarPiece($"{design.Name}: {cut.Label}", cut.LengthMm));
            }
        return pieces.OrderBy(x => x.Key).Select(x => BarOptimizer.Optimize(x.Key, x.Value, system.BarLengthMm, system.KerfMm)).ToList();
    }

    private static List<GlassTotal> Totals(IEnumerable<(WindowDesign Design, WindowCalculation Calculation)> items, Func<WindowCalculation, IEnumerable<GlassPiece>> pieces)
        => items.SelectMany(x => pieces(x.Calculation).Select(p => (Piece: p, x.Design.Quantity)))
            .GroupBy(x => (x.Piece.Name, x.Piece.WidthMm, x.Piece.HeightMm, x.Piece.Shaped))
            .Select(g => new GlassTotal(g.Key.Name, g.Key.WidthMm, g.Key.HeightMm, g.Key.Shaped, g.Sum(x => x.Piece.Count * x.Quantity), g.Sum(x => x.Piece.AreaM2 * x.Quantity))) // AreaM2 parçanın tüm adetini kapsar
            .OrderByDescending(x => x.WidthMm).ThenByDescending(x => x.HeightMm).ToList();
}
