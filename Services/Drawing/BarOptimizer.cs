namespace KarakusAluminyum.Web.Services.Drawing;

public sealed record BarPiece(string Label, decimal LengthMm);

public sealed class CutBar
{
    public List<BarPiece> Pieces { get; } = new();
    public decimal UsedMm { get; set; }
    public decimal WasteMm { get; set; }
}

public sealed record BarPlan(string Profile, decimal BarLengthMm, IReadOnlyList<CutBar> Bars)
{
    public decimal TotalWasteMm => Bars.Sum(b => b.WasteMm);
    public decimal UtilisationPercent => Bars.Count == 0 ? 0
        : decimal.Round(Bars.Sum(b => b.Pieces.Sum(p => p.LengthMm)) / (Bars.Count * BarLengthMm) * 100m, 1, MidpointRounding.AwayFromZero);
}

// Tek boyutlu kesim optimizasyonu: uzundan kısaya, sığdığı ilk boya yerleştirir (first-fit decreasing).
public static class BarOptimizer
{
    public static BarPlan Optimize(string profile, IEnumerable<BarPiece> pieces, decimal barLengthMm, decimal kerfMm)
    {
        var bars = new List<CutBar>();
        foreach (var piece in pieces.OrderByDescending(p => p.LengthMm))
        {
            if (piece.LengthMm > barLengthMm)
                throw new ArgumentException($"{piece.Label} parçası boy profilinden uzun.", nameof(pieces));

            var bar = bars.FirstOrDefault(b => b.UsedMm + kerfMm + piece.LengthMm <= barLengthMm);
            if (bar is null)
            {
                bar = new CutBar();
                bars.Add(bar);
                bar.UsedMm = piece.LengthMm;
            }
            else
            {
                bar.UsedMm += kerfMm + piece.LengthMm;
            }
            bar.Pieces.Add(piece);
        }

        foreach (var bar in bars) bar.WasteMm = barLengthMm - bar.UsedMm;
        return new BarPlan(profile, barLengthMm, bars);
    }
}
