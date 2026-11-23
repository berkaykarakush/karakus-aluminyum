using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Drawing;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record PozLabel(string Code, int Position, int Unit, int Quantity, WindowDesign Design, string Title, string Size, IReadOnlyList<string> Lines);
public sealed record PartLabel(string Code, int Position, int Unit, int Part, WindowDesign Design, string Profile, string Label, decimal LengthMm, string Ends);
public sealed record LabelCode(int OrderId, int DesignId, int Unit, int? Part);

// Ercom'daki barkodlu üretim etiketleri. Her pozun her adedine bir poz etiketi, her kesilen parçaya bir parça etiketi basılır.
// Barkod: S{sipariş}-{poz kimliği}-{adet}[-{parça}]. Okutulan kod siparişi ve pozu doğrudan bulur.
public static partial class ProductionLabels
{
    [GeneratedRegex(@"^S(\d{1,9})-(\d{1,9})-(\d{1,5})(?:-(\d{1,5}))?$")]
    private static partial Regex CodePattern();

    public static string PozCode(int orderId, int designId, int unit) => $"S{orderId}-{designId}-{unit}";
    public static string PartCode(int orderId, int designId, int unit, int part) => $"S{orderId}-{designId}-{unit}-{part}";

    public static LabelCode? Parse(string? code)
    {
        var match = CodePattern().Match((code ?? "").Trim().ToUpperInvariant());
        if (!match.Success) return null;
        return new LabelCode(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : null);
    }

    public static List<PozLabel> Pozlar(ProductionOrder order, IReadOnlyList<WindowDesign> designs)
    {
        var labels = new List<PozLabel>();
        foreach (var (design, i) in designs.Select((d, i) => (d, i + 1)))
        {
            var view = PozSheet.Build(design, i);
            for (var unit = 1; unit <= design.Quantity; unit++)
                labels.Add(new PozLabel(PozCode(order.Id, design.Id, unit), i, unit, design.Quantity, design, view.Title, $"{design.WidthMm} × {design.HeightMm} mm", view.Lines));
        }
        return labels;
    }

    // Parça etiketleri kesim listesinden gelir; profil sistemi olmayan veya hesaplanamayan poz atlanır.
    public static List<PartLabel> Parts(ProductionOrder order, IReadOnlyList<WindowDesign> designs)
    {
        var labels = new List<PartLabel>();
        foreach (var (design, i) in designs.Select((d, i) => (d, i + 1)))
        {
            if (design.ProfileSystem is null) continue;
            var calculation = DrawingCalculation.Calculate(design, design.ProfileSystem);
            if (calculation is null || !calculation.IsValid) continue;
            for (var unit = 1; unit <= design.Quantity; unit++)
            {
                var part = 0;
                foreach (var cut in calculation.Cuts)
                    for (var n = 0; n < cut.Count; n++)
                        labels.Add(new PartLabel(PartCode(order.Id, design.Id, unit, ++part), i, unit, part, design, cut.Profile, cut.Label, cut.LengthMm, cut.Ends));
            }
        }
        return labels;
    }
}
