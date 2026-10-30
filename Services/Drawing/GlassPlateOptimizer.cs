namespace KarakusAluminyum.Web.Services.Drawing;

public sealed record PlacedGlass(string Label, decimal X, decimal Y, decimal WidthMm, decimal HeightMm, bool Rotated);

public sealed class GlassPlate
{
    public List<PlacedGlass> Pieces { get; } = new();
    public decimal ShelfY { get; set; }
    public decimal ShelfHeight { get; set; }
    public decimal CursorX { get; set; }
    public decimal UsedAreaM2 => Pieces.Sum(p => p.WidthMm * p.HeightMm) / 1_000_000m;
}

public sealed record GlassPlatePlan(decimal SheetWidthMm, decimal SheetHeightMm, IReadOnlyList<GlassPlate> Plates, IReadOnlyList<string> TooLarge)
{
    public decimal SheetAreaM2 => SheetWidthMm * SheetHeightMm / 1_000_000m;
    public decimal UtilisationPercent => Plates.Count == 0 ? 0
        : decimal.Round(Plates.Sum(p => p.UsedAreaM2) / (Plates.Count * SheetAreaM2) * 100m, 1, MidpointRounding.AwayFromZero);
}

// Camları plakaya raf mantığıyla yerleştirir (giyotin kesime uygun): uzun kenarı büyük olan önce gelir,
// sığdığı rafa konur, gerekirse döndürülür. En iyi sonucu garanti etmez; plaka sayısı için güvenli tahmindir.
public static class GlassPlateOptimizer
{
    public static GlassPlatePlan Optimize(IEnumerable<(string Label, decimal Width, decimal Height)> pieces, decimal sheetWidth, decimal sheetHeight, decimal gap)
    {
        var plates = new List<GlassPlate>();
        var tooLarge = new List<string>();
        var ordered = pieces.OrderByDescending(p => Math.Max(p.Width, p.Height)).ThenByDescending(p => Math.Min(p.Width, p.Height));

        foreach (var (label, width, height) in ordered)
        {
            // Raf yüksekliği az kalsın diye önce uzun kenar yatay denenir.
            var longSide = Math.Max(width, height);
            var shortSide = Math.Min(width, height);
            var options = new[] { (W: longSide, H: shortSide), (W: shortSide, H: longSide) };
            if (!options.Any(o => o.W <= sheetWidth && o.H <= sheetHeight))
            {
                tooLarge.Add($"{label}: {width:0.#} x {height:0.#} mm");
                continue;
            }

            var placed = false;
            foreach (var plate in plates)
            {
                foreach (var (w, h) in options)
                    if (TryPlace(plate, label, w, h, width, sheetWidth, sheetHeight, gap)) { placed = true; break; }
                if (placed) break;
            }
            if (placed) continue;

            var fresh = new GlassPlate();
            plates.Add(fresh);
            foreach (var (w, h) in options)
                if (TryPlace(fresh, label, w, h, width, sheetWidth, sheetHeight, gap)) break;
        }
        return new GlassPlatePlan(sheetWidth, sheetHeight, plates, tooLarge);
    }

    private static bool TryPlace(GlassPlate plate, string label, decimal w, decimal h, decimal originalWidth, decimal sheetW, decimal sheetH, decimal gap)
    {
        if (w > sheetW || h > sheetH) return false;
        var rotated = w != originalWidth;

        // Geçerli rafta yer varsa oraya koy.
        if (plate.Pieces.Count > 0 && plate.CursorX + w <= sheetW && h <= plate.ShelfHeight)
        {
            plate.Pieces.Add(new PlacedGlass(label, plate.CursorX, plate.ShelfY, w, h, rotated));
            plate.CursorX += w + gap;
            return true;
        }

        // Yeni raf aç.
        var nextY = plate.Pieces.Count == 0 ? 0 : plate.ShelfY + plate.ShelfHeight + gap;
        if (nextY + h > sheetH) return false;
        plate.ShelfY = nextY;
        plate.ShelfHeight = h;
        plate.Pieces.Add(new PlacedGlass(label, 0, nextY, w, h, rotated));
        plate.CursorX = w + gap;
        return true;
    }
}
