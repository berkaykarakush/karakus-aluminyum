using System.Text.Json;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Services.Drawing;

public sealed record WindowInput(int WidthMm, int HeightMm, string Shape, int ShapeParamMm, int Quantity, LayoutNode Root);

public sealed record CutPiece(string Profile, string Label, decimal LengthMm, int Count, string Ends);
public sealed record GlassPiece(string Name, decimal WidthMm, decimal HeightMm, int Count, bool Shaped, decimal AreaM2);
public sealed record HardwareLine(string Name, int Count, decimal UnitPrice);

public sealed class WindowCalculation
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<CutPiece> Cuts { get; set; } = new();
    public List<GlassPiece> Glass { get; set; } = new();  // yalnızca ölçü: cam listesine aktarılır
    public List<GlassPiece> Panels { get; } = new();
    public List<HardwareLine> Hardware { get; set; } = new();
    public decimal ProfileCost { get; set; }
    public decimal PanelCost { get; set; } // cam fiyatı buraya dahil değildir; camlar cam listelerinde fiyatlanır
    public decimal HardwareCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public int LeafCount { get; set; }
    public bool IsValid => Errors.Count == 0;
}

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

// Kesim ölçüsü, cam ve maliyet hesabının tek kaynağı. Ölçüler mm, yükseklik ekseni yukarıdan aşağıya.
public sealed class WindowCalculator
{
    private const int MaxLeaves = 100;
    private const int MaxDepth = 12;
    public const string FrameProfile = "Kasa";
    public const string SashProfile = "Kanat";
    public const string MullionProfile = "Kayıt";
    public const string BeadProfile = "Çıta";

    private readonly ProfileSystem _system;
    private readonly WindowInput _input;
    private readonly WindowCalculation _result = new();
    private readonly List<CutPiece> _cuts = new();
    private readonly List<GlassPiece> _glass = new();
    private readonly Dictionary<string, HardwareLine> _hardware = new();
    private decimal _panelCost;

    private WindowCalculator(WindowInput input, ProfileSystem system) => (_input, _system) = (input, system);

    public static WindowCalculation Calculate(WindowInput input, ProfileSystem system) => new WindowCalculator(input, system).Run();

    private decimal Frame => _system.FrameWidthMm;

    private WindowCalculation Run()
    {
        ValidateOuter();
        if (!_result.IsValid) return _result;

        AddFrameCuts();
        var clearW = _input.WidthMm - 2 * Frame;
        var clearH = _input.HeightMm - 2 * Frame;
        Walk(_input.Root, Frame, Frame, clearW, clearH, depth: 0);
        if (!_result.IsValid) return _result;

        foreach (var cut in _cuts.Where(c => c.LengthMm > _system.BarLengthMm).Select(c => c.Label).Distinct())
            _result.Errors.Add($"{cut} parçası {_system.BarLengthMm:0} mm boy profilinden uzun.");

        _result.Cuts = _cuts.GroupBy(c => (c.Profile, c.Label, c.LengthMm, c.Ends))
            .Select(g => new CutPiece(g.Key.Profile, g.Key.Label, g.Key.LengthMm, g.Sum(c => c.Count), g.Key.Ends))
            .OrderBy(c => c.Profile).ThenByDescending(c => c.LengthMm).ToList();
        _result.Glass = _glass.GroupBy(g => (g.Name, g.WidthMm, g.HeightMm, g.Shaped))
            .Select(g => new GlassPiece(g.Key.Name, g.Key.WidthMm, g.Key.HeightMm, g.Sum(x => x.Count), g.Key.Shaped, g.Sum(x => x.AreaM2)))
            .ToList();
        _result.Hardware = _hardware.Values.OrderBy(h => h.Name).ToList();
        CalculateCost();
        return _result;
    }

    private void ValidateOuter()
    {
        if (_input.WidthMm is < 200 or > 20000 || _input.HeightMm is < 200 or > 20000)
            _result.Errors.Add("Genişlik ve yükseklik 200-20000 mm arasında olmalıdır.");
        if (_input.Quantity < 1) _result.Errors.Add("Adet en az 1 olmalıdır.");
        if (_input.WidthMm <= 2 * Frame || _input.HeightMm <= 2 * Frame)
            _result.Errors.Add("Ölçü, kasa genişliğinin iki katından büyük olmalıdır.");
        if (!WindowShapes.All.Contains(_input.Shape)) _result.Errors.Add("Geçersiz şekil.");

        switch (_input.Shape)
        {
            case WindowShapes.Arch when _input.ShapeParamMm <= 0 || _input.ShapeParamMm > _input.WidthMm / 2:
                _result.Errors.Add("Kemer yüksekliği 0 ile genişliğin yarısı arasında olmalıdır.");
                break;
            case WindowShapes.Arch when _input.HeightMm - _input.ShapeParamMm <= 2 * Frame:
                _result.Errors.Add("Kemer altındaki yan kenar kasa genişliğinden kısa kalıyor.");
                break;
            case WindowShapes.SlopeLeft or WindowShapes.SlopeRight when _input.ShapeParamMm <= 2 * Frame || _input.ShapeParamMm >= _input.HeightMm:
                _result.Errors.Add("Alçak kenar yüksekliği kasa genişliğinin iki katından büyük, toplam yükseklikten küçük olmalıdır.");
                break;
        }
    }

    // Dış kenarın x konumundaki üst sınırı (0 = en üst).
    private decimal TopAt(decimal x)
    {
        decimal w = _input.WidthMm, h = _input.HeightMm, p = _input.ShapeParamMm;
        switch (_input.Shape)
        {
            case WindowShapes.Arch:
                var radius = (double)((w * w / 4 + p * p) / (2 * p));
                var dx = (double)(x - w / 2);
                var rise = Math.Sqrt(Math.Max(0, radius * radius - dx * dx)) - (radius - (double)p);
                return p - (decimal)rise;
            case WindowShapes.SlopeLeft: return (h - p) * (1 - x / w);
            case WindowShapes.SlopeRight: return (h - p) * (x / w);
            default: return 0;
        }
    }

    private static decimal Round(decimal value) => decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    private void AddCut(string profile, string label, decimal length, int count, string ends)
        => _cuts.Add(new CutPiece(profile, label, Round(length), count, ends));

    private void AddMitred(string profile, string label, decimal length, int count)
        => AddCut(profile, label, length + 2 * _system.WeldAllowanceMm, count, "45°/45°");

    private void AddFrameCuts()
    {
        decimal w = _input.WidthMm, h = _input.HeightMm, p = _input.ShapeParamMm;
        AddMitred(FrameProfile, "Kasa alt", w, 1);
        switch (_input.Shape)
        {
            case WindowShapes.Arch:
                var radius = (double)((w * w / 4 + p * p) / (2 * p));
                var arc = radius * 2 * Math.Asin(Math.Min(1, (double)w / (2 * radius)));
                AddCut(FrameProfile, "Kasa kemer (bükme)", (decimal)arc + 2 * _system.WeldAllowanceMm, 1, "Şablon");
                AddMitred(FrameProfile, "Kasa yan", h - p, 2);
                _result.Warnings.Add("Kemerli kasa ve camlar için şablon alınmalıdır; ölçüler yaklaşık değerdir.");
                break;
            case WindowShapes.SlopeLeft or WindowShapes.SlopeRight:
                var top = (decimal)Math.Sqrt((double)(w * w + (h - p) * (h - p)));
                AddCut(FrameProfile, "Kasa eğik üst", top + 2 * _system.WeldAllowanceMm, 1, "Açılı");
                AddCut(FrameProfile, "Kasa uzun yan", h + 2 * _system.WeldAllowanceMm, 1, "Açılı");
                AddCut(FrameProfile, "Kasa kısa yan", p + 2 * _system.WeldAllowanceMm, 1, "Açılı");
                _result.Warnings.Add("Eğimli üst kenarda birleşim açıları kesimden önce kontrol edilmelidir.");
                break;
            default:
                AddMitred(FrameProfile, "Kasa üst", w, 1);
                AddMitred(FrameProfile, "Kasa yan", h, 2);
                break;
        }
    }

    private void Walk(LayoutNode node, decimal x, decimal y, decimal w, decimal h, int depth)
    {
        if (depth > MaxDepth) { _result.Errors.Add("Bölme derinliği çok fazla."); return; }
        if (w <= 0 || h <= 0) { _result.Errors.Add("Bir bölmenin ölçüsü sıfırın altına düştü."); return; }
        if (node.Split is null) { AddLeaf(node, x, y, w, h); return; }
        if (node.Split is not ("v" or "h")) { _result.Errors.Add("Geçersiz bölme yönü."); return; }

        var children = node.Children ?? new();
        var sizes = node.Sizes ?? new();
        if (children.Count < 2 || sizes.Count != children.Count)
        {
            _result.Errors.Add("Bölme verisi tutarsız.");
            return;
        }
        if (node.Sliding && (node.Split != "v" || children.Any(c => c.Split is not null || !(WindowOpenings.IsSlide(c.Opening) || c.Opening == WindowOpenings.Fixed))))
        {
            _result.Errors.Add("Sürme bölme yan yana bölünmeli, kanatları sürme veya sabit olmalıdır.");
            return;
        }

        var bar = node.Sliding ? 0 : _system.MullionWidthMm;
        var total = node.Split == "v" ? w : h;
        var used = sizes.Sum() + bar * (children.Count - 1);
        if (Math.Abs(used - total) > 1)
        {
            _result.Errors.Add($"Bölme ölçüleri toplamı ({used:0.#} mm) açıklığa ({total:0.#} mm) eşit değil.");
            return;
        }

        var offset = 0m;
        for (var i = 0; i < children.Count; i++)
        {
            var size = sizes[i];
            if (node.Split == "v")
            {
                Walk(children[i], x + offset, y, size, h, depth + 1);
                if (i < children.Count - 1 && !node.Sliding) AddVerticalMullion(x + offset + size + bar / 2, y, h);
            }
            else
            {
                Walk(children[i], x, y + offset, w, size, depth + 1);
                if (i < children.Count - 1 && !node.Sliding) AddHorizontalMullion(x, y + offset + size + bar / 2, w);
            }
            offset += size + bar;
        }
    }

    private void AddVerticalMullion(decimal centerX, decimal y, decimal h)
    {
        var top = Math.Max(y, TopAt(centerX) + Frame);
        var length = y + h - top;
        if (length <= 0) { _result.Errors.Add("Dikey kayıt şeklin dışında kalıyor."); return; }
        var clipped = top > y;
        AddCut(MullionProfile, clipped ? "Dikey kayıt (üst açılı)" : "Dikey kayıt", length + 2 * _system.MullionFitMm, 1, clipped ? "Açılı/90°" : "90°/90°");
    }

    private void AddHorizontalMullion(decimal x, decimal centerY, decimal w)
    {
        if (TopAt(x) + Frame > centerY || TopAt(x + w) + Frame > centerY)
        {
            _result.Errors.Add("Yatay kayıt kemer veya eğim bölgesine giremez; daha aşağıya alın.");
            return;
        }
        AddCut(MullionProfile, "Yatay kayıt", w + 2 * _system.MullionFitMm, 1, "90°/90°");
    }

    private void AddLeaf(LayoutNode node, decimal x, decimal y, decimal w, decimal h)
    {
        if (++_result.LeafCount > MaxLeaves) { _result.Errors.Add("Çok fazla bölme var."); return; }
        if (!WindowOpenings.All.Contains(node.Opening)) { _result.Errors.Add("Geçersiz açılım tipi."); return; }
        if (node.Fill is not ("glass" or "panel")) { _result.Errors.Add("Geçersiz dolgu tipi."); return; }

        var boundaryTop = new[] { TopAt(x), TopAt(x + w), TopAt(x + w / 2) }.Min() + Frame;
        var clipped = TopAt(x) + Frame > y || TopAt(x + w) + Frame > y;
        var isOpening = node.Opening != WindowOpenings.Fixed;
        if (clipped && isOpening)
        {
            _result.Errors.Add("Kemerli veya eğimli bölmede açılım kullanılamaz; sabit seçin ya da yatay kayıtla ayırın.");
            return;
        }

        var fillW = w;
        var fillH = clipped ? y + h - Math.Max(y, boundaryTop) : h;
        if (isOpening)
        {
            var sashW = w + 2 * _system.SashOverlapMm;
            var sashH = h + 2 * _system.SashOverlapMm;
            AddMitred(SashProfile, "Kanat yatay", sashW, 2);
            AddMitred(SashProfile, "Kanat dikey", sashH, 2);
            fillW = sashW - 2 * _system.SashWidthMm;
            fillH = sashH - 2 * _system.SashWidthMm;
            AddHardware(node.Opening);
        }

        fillW = Round(fillW - 2 * _system.GlassClearanceMm);
        fillH = Round(fillH - 2 * _system.GlassClearanceMm);
        if (fillW <= 0 || fillH <= 0)
        {
            _result.Errors.Add("Bölme, kanat ve cam için çok küçük.");
            return;
        }

        AddCut(BeadProfile, "Çıta yatay", fillW, 2, "90°/90°");
        AddCut(BeadProfile, "Çıta dikey", fillH, 2, "90°/90°");

        var area = fillW * fillH / 1_000_000m;
        if (node.Fill == "panel")
        {
            _result.Panels.Add(new GlassPiece("Panel", fillW, fillH, 1, clipped, area));
            _panelCost += area * _system.PanelPricePerM2;
            return;
        }

        _glass.Add(new GlassPiece("Cam", fillW, fillH, 1, clipped, area));
    }

    private void AddHardware(string opening)
    {
        var (name, price) = opening switch
        {
            WindowOpenings.Left or WindowOpenings.Right => ("Açılır kanat donanımı", _system.HardwareOpenPrice),
            WindowOpenings.Tilt or WindowOpenings.TiltLeft or WindowOpenings.TiltRight => ("Açılır-devrilir / vasistas donanımı", _system.HardwareTiltPrice),
            WindowOpenings.SlideLeft or WindowOpenings.SlideRight => ("Sürme donanımı", _system.HardwareSlidePrice),
            _ => ("Kapı donanımı", _system.HardwareDoorPrice)
        };
        _hardware[name] = new HardwareLine(name, (_hardware.TryGetValue(name, out var line) ? line.Count : 0) + 1, price);
        if (price == 0) AddPriceWarning($"{name} fiyatı girilmemiş.");
    }

    private void AddPriceWarning(string message)
    {
        if (!_result.Warnings.Contains(message)) _result.Warnings.Add(message);
    }

    private void CalculateCost()
    {
        var profile = 0m;
        foreach (var group in _result.Cuts.GroupBy(c => c.Profile))
        {
            var price = group.Key switch
            {
                FrameProfile => _system.FramePricePerM,
                SashProfile => _system.SashPricePerM,
                MullionProfile => _system.MullionPricePerM,
                _ => _system.BeadPricePerM
            };
            if (price == 0) AddPriceWarning($"{group.Key} profil fiyatı girilmemiş.");
            profile += group.Sum(c => c.LengthMm * c.Count) / 1000m * price;
        }

        _result.ProfileCost = Money(profile * (1 + _system.WastePercent / 100m));
        _result.PanelCost = Money(_panelCost);
        _result.HardwareCost = Money(_result.Hardware.Sum(h => h.Count * h.UnitPrice));
        var material = _result.ProfileCost + _result.PanelCost + _result.HardwareCost;
        _result.LaborCost = Money(material * _system.LaborPercent / 100m);
        _result.UnitCost = material + _result.LaborCost;
        _result.UnitPrice = Money(_result.UnitCost * (1 + _system.MarginPercent / 100m));
        _result.TotalPrice = _result.UnitPrice * _input.Quantity;
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
