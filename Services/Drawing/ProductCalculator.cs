using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

// Panjur, korkuluk ve cam balkon hesabı. Sonuç biçimi doğrama hesabıyla aynıdır;
// cam ölçüleri yalnızca bildirilir, fiyatı cam listelerinde hesaplanır.
public sealed class ProductCalculator
{
    private readonly ProfileSystem _system;
    private readonly ProductSettings _settings;
    private readonly WindowCalculation _result = new();
    private readonly List<CutPiece> _cuts = new();
    private readonly Dictionary<string, decimal> _pricePerM = new();
    private decimal _extraCost;

    private ProductCalculator(ProfileSystem system, ProductSettings settings) => (_system, _settings) = (system, settings);

    public static WindowCalculation Calculate(string category, int widthMm, int heightMm, int quantity, ProductParams input, ProfileSystem system, ProductSettings settings)
    {
        var calculator = new ProductCalculator(system, settings);
        if (widthMm is < 200 or > 100000 || heightMm is < 200 or > 20000) calculator._result.Errors.Add("Genişlik 200-100000 mm, yükseklik 200-20000 mm arasında olmalıdır.");
        if (quantity < 1) calculator._result.Errors.Add("Adet en az 1 olmalıdır.");
        if (!calculator._result.IsValid) return calculator._result;

        switch (category)
        {
            case ProductCategories.Shutter: calculator.Shutter(widthMm, heightMm, input); break;
            case ProductCategories.Railing: calculator.Railing(widthMm, heightMm, input); break;
            case ProductCategories.GlassBalcony: calculator.Balcony(widthMm, heightMm, input); break;
            default: calculator._result.Errors.Add("Geçersiz ürün türü."); break;
        }
        if (calculator._result.IsValid) calculator.Finish(quantity);
        return calculator._result;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    private void AddCut(string profile, string label, decimal length, int count, string ends = "90°/90°")
    {
        if (count > 0) _cuts.Add(new CutPiece(profile, label, Round(length), count, ends));
    }

    // Boy profilden uzun parçalar (ör. uzun korkuluk küpeştesi) ek yerleriyle bölünür.
    private void AddLong(string profile, string label, decimal length, int count = 1)
    {
        var bar = _system.BarLengthMm;
        var whole = (int)decimal.Floor(length / bar);
        var rest = length - whole * bar;
        AddCut(profile, label, bar, whole * count);
        if (rest > 0) AddCut(profile, label, rest, count);
        if (whole > 0 && rest >= 0 && (whole > 1 || rest > 0))
            AddWarning($"{label} boy profilden uzun; ek yeri gerekir.");
    }

    private void AddWarning(string message)
    {
        if (!_result.Warnings.Contains(message)) _result.Warnings.Add(message);
    }

    private void Price(string profile, decimal pricePerM)
    {
        _pricePerM[profile] = pricePerM;
        if (pricePerM == 0) AddWarning($"{profile} fiyatı girilmemiş.");
    }

    private void AddGlass(decimal width, decimal height, int count, bool shaped = false)
    {
        width = Round(width); height = Round(height);
        if (width <= 0 || height <= 0) { _result.Errors.Add("Cam ölçüsü sıfırın altına düştü; ölçüleri veya panel sayısını kontrol edin."); return; }
        _result.Glass.Add(new GlassPiece("Cam", width, height, count, shaped, width * height / 1_000_000m * count));
    }

    private void Shutter(int width, int height, ProductParams input)
    {
        if (input.Drive is not ("motor" or "manual")) { _result.Errors.Add("Geçersiz panjur tahriki."); return; }
        var lamellaLength = width - 2 * _settings.GuideDeductMm;
        if (lamellaLength <= 0) { _result.Errors.Add("Panjur genişliği yan dikme düşümünden küçük."); return; }
        var curtainHeight = height - _settings.BoxHeightMm;
        if (curtainHeight <= 0) { _result.Errors.Add("Panjur yüksekliği kutu yüksekliğinden küçük."); return; }

        var lamellaCount = (int)decimal.Ceiling(curtainHeight / _settings.LamellaHeightMm);
        AddCut("Lamel", "Lamel", lamellaLength, lamellaCount);
        AddCut("Lamel", "Alt parça", lamellaLength, 1);
        AddCut("Yan dikme", "Yan dikme", curtainHeight, 2);
        AddCut("Kutu", "Panjur kutusu", width, 1);
        Price("Yan dikme", _settings.GuidePricePerM);
        Price("Kutu", _settings.BoxPricePerM);

        if (_settings.LamellaPricePerM2 == 0) AddWarning("Lamel fiyatı girilmemiş.");
        var area = lamellaLength * curtainHeight / 1_000_000m;
        _extraCost += area * _settings.LamellaPricePerM2;
        _pricePerM["Lamel"] = 0; // lamel m² üzerinden fiyatlanır

        var drive = input.Drive == "motor" ? ("Motor", _settings.MotorPrice) : ("Manuel kayış / makara", _settings.ManualPrice);
        _result.Hardware.Add(new HardwareLine(drive.Item1, 1, drive.Item2));
        if (drive.Item2 == 0) AddWarning($"{drive.Item1} fiyatı girilmemiş.");
    }

    private void Railing(int length, int height, ProductParams input)
    {
        if (input.Infill is not ("glass" or "bars")) { _result.Errors.Add("Geçersiz korkuluk dolgusu."); return; }
        var spacing = input.PostSpacingMm > 0 ? input.PostSpacingMm : _settings.PostSpacingMm;
        if (spacing < 100) { _result.Errors.Add("Dikme aralığı en az 100 mm olmalıdır."); return; }

        var bays = (int)decimal.Ceiling(length / spacing);
        var posts = bays + 1;
        var bayWidth = (length - posts * _settings.PostWidthMm) / bays;
        if (bayWidth <= 0) { _result.Errors.Add("Korkuluk uzunluğu dikmeler için çok kısa."); return; }

        AddCut("Dikme", "Dikme", height, posts);
        AddLong("Küpeşte", "Küpeşte", length);
        Price("Dikme", _settings.PostPricePerM);
        Price("Küpeşte", _settings.RailPricePerM);

        var infillHeight = height - 2 * _settings.RailHeightMm;
        if (infillHeight <= 0) { _result.Errors.Add("Korkuluk yüksekliği küpeşte için çok az."); return; }
        if (input.Infill == "glass")
        {
            AddLong("Küpeşte", "Alt ray", length);
            AddGlass(bayWidth - 2 * _system.GlassClearanceMm, infillHeight - 2 * _system.GlassClearanceMm, bays);
        }
        else
        {
            AddLong("Küpeşte", "Alt küpeşte", length);
            var barsPerBay = Math.Max(1, (int)decimal.Floor(bayWidth / _settings.BarSpacingMm));
            AddCut("Emniyet çubuğu", "Emniyet çubuğu", infillHeight, barsPerBay * bays);
            Price("Emniyet çubuğu", _settings.BarPricePerM);
        }
    }

    private void Balcony(int width, int height, ProductParams input)
    {
        if (input.OpenType is not ("sliding" or "folding")) { _result.Errors.Add("Geçersiz cam balkon tipi."); return; }
        if (input.PanelCount is < 1 or > 30) { _result.Errors.Add("Panel sayısı 1-30 arasında olmalıdır."); return; }

        var overlap = input.OpenType == "sliding" ? _settings.PanelOverlapMm : 0;
        var panelWidth = (width + overlap * (input.PanelCount - 1)) / input.PanelCount;
        var panelHeight = height - 2 * _settings.RailHeightMm;
        if (panelHeight <= 0) { _result.Errors.Add("Cam balkon yüksekliği ray için çok az."); return; }

        AddLong("Ray", "Üst ray", width);
        AddLong("Ray", "Alt ray", width);
        AddCut("Kanat profili", "Kanat yatay", panelWidth, 2 * input.PanelCount);
        AddCut("Kanat profili", "Kanat dikey", panelHeight, 2 * input.PanelCount);
        Price("Ray", _settings.RailPricePerM);
        Price("Kanat profili", _settings.PanelProfilePricePerM);

        AddGlass(panelWidth - 2 * _system.SashWidthMm - 2 * _system.GlassClearanceMm, panelHeight - 2 * _system.SashWidthMm - 2 * _system.GlassClearanceMm, input.PanelCount);
        _result.Hardware.Add(new HardwareLine(input.OpenType == "sliding" ? "Sürme kanat donanımı" : "Katlanır kanat donanımı", input.PanelCount, _settings.PanelHardwarePrice));
        if (_settings.PanelHardwarePrice == 0) AddWarning("Kanat donanımı fiyatı girilmemiş.");
    }

    private void Finish(int quantity)
    {
        _result.Cuts = _cuts.GroupBy(c => (c.Profile, c.Label, c.LengthMm, c.Ends))
            .Select(g => new CutPiece(g.Key.Profile, g.Key.Label, g.Key.LengthMm, g.Sum(c => c.Count), g.Key.Ends))
            .OrderBy(c => c.Profile).ThenByDescending(c => c.LengthMm).ToList();
        foreach (var cut in _result.Cuts.Where(c => c.LengthMm > _system.BarLengthMm).Select(c => c.Label).Distinct())
            _result.Errors.Add($"{cut} parçası {_system.BarLengthMm:0} mm boy profilinden uzun.");
        if (!_result.IsValid) return;

        var profile = _result.Cuts.Sum(c => c.LengthMm * c.Count / 1000m * _pricePerM.GetValueOrDefault(c.Profile));
        _result.ProfileCost = Money((profile + _extraCost) * (1 + _system.WastePercent / 100m));
        _result.HardwareCost = Money(_result.Hardware.Sum(h => h.Count * h.UnitPrice));
        var material = _result.ProfileCost + _result.HardwareCost;
        _result.LaborCost = Money(material * _system.LaborPercent / 100m);
        _result.UnitCost = material + _result.LaborCost;
        _result.UnitPrice = Money(_result.UnitCost * (1 + _system.MarginPercent / 100m));
        _result.TotalPrice = _result.UnitPrice * quantity;
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
