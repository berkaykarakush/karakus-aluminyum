using System.Net;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class WindowCalculatorTests
{
    private static readonly ProfileSystem System = new()
    {
        Name = "Test", FrameWidthMm = 70, SashWidthMm = 80, MullionWidthMm = 80, SashOverlapMm = 8, GlassClearanceMm = 5,
        MullionFitMm = 2, WeldAllowanceMm = 3, BarLengthMm = 6000, KerfMm = 4,
        FramePricePerM = 100, SashPricePerM = 120, MullionPricePerM = 110, BeadPricePerM = 10, PanelPricePerM2 = 200,
        HardwareOpenPrice = 300, HardwareTiltPrice = 500, HardwareSlidePrice = 700, HardwareDoorPrice = 900,
        WastePercent = 10, LaborPercent = 20, MarginPercent = 50
    };

    private static LayoutNode Leaf(string opening = WindowOpenings.Fixed, string fill = "glass")
        => new() { Opening = opening, Fill = fill };

    private static LayoutNode Split(string direction, decimal[] sizes, params LayoutNode[] children)
        => new() { Split = direction, Sizes = sizes.ToList(), Children = children.ToList() };

    private static WindowCalculation Calc(LayoutNode root, int width = 1000, int height = 1000, string shape = WindowShapes.Rect, int param = 0, int quantity = 1)
        => WindowCalculator.Calculate(new WindowInput(width, height, shape, param, quantity, root), System);

    private static CutPiece Cut(WindowCalculation c, string label) => Assert.Single(c.Cuts, x => x.Label == label);

    [Fact]
    public void FixedWindow_FrameCutsIncludeWeldAllowance_AndGlassUsesClearance()
    {
        var c = Calc(Leaf(), 1200, 1000);

        Assert.True(c.IsValid);
        Assert.Equal(1206m, Cut(c, "Kasa alt").LengthMm);
        Assert.Equal(1206m, Cut(c, "Kasa üst").LengthMm);
        Assert.Equal(1006m, Cut(c, "Kasa yan").LengthMm);
        Assert.Equal(2, Cut(c, "Kasa yan").Count);
        var glass = Assert.Single(c.Glass);
        Assert.Equal(1050m, glass.WidthMm); // 1200 - 2*70 - 2*5
        Assert.Equal(850m, glass.HeightMm);
        Assert.Empty(c.Hardware);
    }

    [Fact]
    public void OpeningLeaf_AddsSashCuts_GlassInsideSash_AndHardware()
    {
        var c = Calc(Leaf(WindowOpenings.TiltRight)); // açıklık 860 x 860

        Assert.Equal(882m, Cut(c, "Kanat yatay").LengthMm); // 860 + 2*8 + 2*3
        Assert.Equal(2, Cut(c, "Kanat dikey").Count);
        var glass = Assert.Single(c.Glass);
        Assert.Equal(706m, glass.WidthMm); // 876 - 2*80 - 2*5
        var hardware = Assert.Single(c.Hardware);
        Assert.Equal(500m, hardware.UnitPrice);
    }

    [Fact]
    public void Mullion_LengthUsesFit()
    {
        var c = Calc(Split("v", new[] { 390m, 390m }, Leaf(), Leaf())); // 860 = 390 + 80 + 390
        Assert.True(c.IsValid);
        Assert.Equal(864m, Cut(c, "Dikey kayıt").LengthMm); // 860 + 2*2
        Assert.Equal(2, Assert.Single(c.Glass).Count);

    }

    // Bölme ölçüleri dış ölçüdür: kayıt merkezi sınırdadır, kasa ve kayıt payı hesapta düşülür.
    [Fact]
    public void PartSizes_AreOuterSizes_AndMullionCentersOnBoundary()
    {
        var c = Calc(Split("v", new[] { 300m, 700m }, Leaf(), Leaf())); // kasa 70, kayıt 80
        Assert.True(c.IsValid);
        // sol açıklık 70..260 = 190, sağ açıklık 340..930 = 590; cam payı 2x5
        Assert.Equal(new[] { 180m, 580m }, c.Glass.Select(g => g.WidthMm).Order());

        // Yalnızca oran önemlidir: aynı oranda eski kayıt aynı sonucu verir.
        var scaled = Calc(Split("v", new[] { 3m, 7m }, Leaf(), Leaf()));
        Assert.Equal(c.Glass.Select(g => g.WidthMm).Order(), scaled.Glass.Select(g => g.WidthMm).Order());
    }

    [Fact]
    public void NestedPartSizes_UseTheirOwnOuterPart()
    {
        // Üstte 500'lük bölme kayıt altında yan yana ikiye ayrılır: iç kayıt 500 sınırındadır.
        var c = Calc(Split("h", new[] { 500m, 500m }, Leaf(), Split("v", new[] { 500m, 500m }, Leaf(), Leaf())));
        Assert.True(c.IsValid);
        Assert.Equal(860m + 2 * 2m, Cut(c, "Yatay kayıt").LengthMm);
        // alt bölme 540..930 = 390 yüksek; iç kayıt tüm bu yüksekliktedir
        Assert.Equal(390m + 2 * 2m, Cut(c, "Dikey kayıt").LengthMm);
    }

    [Fact]
    public void SameDirectionNesting_SplitsItsOwnPart_NotTheWholeWindow()
    {
        // sınırlar 500 ve 750: 70..460, 540..710, 790..930; cam payı 2x5
        var c = Calc(Split("v", new[] { 500m, 500m }, Leaf(), Split("v", new[] { 250m, 250m }, Leaf(), Leaf())));
        Assert.True(c.IsValid);
        Assert.Equal(new[] { 130m, 160m, 380m }, c.Glass.Select(g => g.WidthMm).Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePartSize_IsAnError(int size)
        => Assert.Contains(Calc(Split("v", new[] { (decimal)size, 500m }, Leaf(), Leaf())).Errors, e => e.Contains("tutarsız"));

    [Fact]
    public void SlidingSplit_HasNoMullion_AndRequiresSlidingOrFixedLeaves()
    {
        var root = Split("v", new[] { 430m, 430m }, Leaf(WindowOpenings.SlideRight), Leaf(WindowOpenings.SlideLeft));
        root.Sliding = true;
        var c = Calc(root);
        Assert.True(c.IsValid);
        Assert.DoesNotContain(c.Cuts, x => x.Profile == WindowCalculator.MullionProfile);
        Assert.Equal(2, Assert.Single(c.Hardware).Count);

        var invalid = Split("v", new[] { 430m, 430m }, Leaf(WindowOpenings.Left), Leaf());
        invalid.Sliding = true;
        Assert.False(Calc(invalid).IsValid);
    }

    [Fact]
    public void Cost_AppliesWasteLaborAndMargin()
    {
        var c = Calc(Leaf(WindowOpenings.Left), quantity: 3);
        // Kasa 4 x 1006 mm = 4,024 m x 100 = 402,40
        // Kanat 4 x 882 mm = 3,528 m x 120 = 423,36
        // Çıta 4 x 706 mm = 2,824 m x 10 = 28,24
        Assert.Equal(939.40m, c.ProfileCost); // 854,00 x 1,10
        Assert.Equal(300m, c.HardwareCost);
        Assert.Equal(247.88m, c.LaborCost);   // 1239,40 x 0,20
        Assert.Equal(1487.28m, c.UnitCost);
        Assert.Equal(2230.92m, c.UnitPrice);  // cam fiyatı dahil değildir
        Assert.Equal(6692.76m, c.TotalPrice);
    }

    [Fact]
    public void GlassIsNotPriced_ButSizesAreReported_AndPanelIsPriced()
    {
        var c = Calc(Split("h", new[] { 390m, 390m }, Leaf(), Leaf(fill: "panel")));
        var glass = Assert.Single(c.Glass);
        Assert.Equal(850m, glass.WidthMm);
        var panel = Assert.Single(c.Panels);
        Assert.Equal(decimal.Round(panel.AreaM2 * 200, 2), c.PanelCost);
    }

    [Fact]
    public void ArchedWindow_ComputesArcLength_AndRejectsOpeningInArch()
    {
        // 1000 genişlik, 500 kemer: tam yarım daire, yay = pi * 500
        var c = Calc(Leaf(), 1000, 1500, WindowShapes.Arch, 500);
        Assert.True(c.IsValid);
        Assert.Equal(1570.8m + 6m, Cut(c, "Kasa kemer (bükme)").LengthMm);
        Assert.Equal(1006m, Cut(c, "Kasa yan").LengthMm);
        Assert.True(Assert.Single(c.Glass).Shaped);

        Assert.Contains(Calc(Leaf(WindowOpenings.Left), 1000, 1500, WindowShapes.Arch, 500).Errors, e => e.Contains("açılım kullanılamaz"));
    }

    [Fact]
    public void ArchedWindow_AllowsOpeningBelowTransom_ButNotTransomInsideArch()
    {
        // Açıklık 860 x 1360; yatay kayıt kemerin altına alınır: üst 600 + 80 + alt 680
        var valid = Calc(Split("h", new[] { 600m, 680m }, Leaf(), Leaf(WindowOpenings.Left)), 1000, 1500, WindowShapes.Arch, 500);
        Assert.True(valid.IsValid, string.Join(";", valid.Errors));

        var invalid = Calc(Split("h", new[] { 200m, 1080m }, Leaf(), Leaf()), 1000, 1500, WindowShapes.Arch, 500);
        Assert.Contains(invalid.Errors, e => e.Contains("Yatay kayıt"));
    }

    [Fact]
    public void SlopedWindow_TopLengthIsHypotenuse_AndMullionIsClipped()
    {
        var c = Calc(Split("v", new[] { 390m, 390m }, Leaf(), Leaf()), 1000, 1000, WindowShapes.SlopeLeft, 600);
        Assert.True(c.IsValid, string.Join(";", c.Errors));
        Assert.Equal(1077m + 6m, Math.Round(Cut(c, "Kasa eğik üst").LengthMm)); // kök(1000² + 400²) = 1077,03
        Assert.Equal(1006m, Cut(c, "Kasa uzun yan").LengthMm);
        Assert.Equal(606m, Cut(c, "Kasa kısa yan").LengthMm);
        // Merkez x=500'de üst sınır 200 + kasa 70 = 270; alt 930: 660 + 2*2
        Assert.Equal(664m, Cut(c, "Dikey kayıt (üst açılı)").LengthMm);
    }

    [Theory]
    [InlineData(100, 1000, WindowShapes.Rect, 0)]
    [InlineData(1000, 25000, WindowShapes.Rect, 0)]
    [InlineData(1000, 1500, WindowShapes.Arch, 600)]
    [InlineData(1000, 1500, WindowShapes.Arch, 0)]
    [InlineData(1000, 1000, WindowShapes.SlopeLeft, 1000)]
    [InlineData(1000, 1000, "yildiz", 0)]
    public void InvalidOuterDimensions_AreRejected(int width, int height, string shape, int param)
        => Assert.False(Calc(Leaf(), width, height, shape, param).IsValid);

    [Theory]
    [InlineData("yok", "glass")]
    [InlineData("fixed", "tahta")]
    public void InvalidLeafValues_AreRejected(string opening, string fill)
        => Assert.False(Calc(Leaf(opening, fill)).IsValid);

    [Fact]
    public void TooSmallOpeningLeaf_IsRejected()
        => Assert.Contains(Calc(Split("v", new[] { 100m, 680m }, Leaf(WindowOpenings.Left), Leaf())).Errors, e => e.Contains("çok küçük"));

    [Fact]
    public void PieceLongerThanBar_IsRejected()
        => Assert.Contains(Calc(Leaf(), 7000, 1000).Errors, e => e.Contains("boy profilinden uzun"));

    [Fact]
    public void ZeroPrices_ProduceWarnings()
    {
        var free = new ProfileSystem { Name = "Fiyatsız" };
        var c = WindowCalculator.Calculate(new WindowInput(1000, 1000, WindowShapes.Rect, 0, 1, Leaf(WindowOpenings.Left)), free);
        Assert.True(c.IsValid);
        Assert.Equal(0m, c.UnitPrice);
        Assert.Contains(c.Warnings, w => w.Contains("Kasa"));
        Assert.DoesNotContain(c.Warnings, w => w.Contains("cam"));
    }

    [Fact]
    public void BrokenLayoutJson_ParsesToNull()
    {
        Assert.Null(WindowLayout.Parse("{bozuk"));
        Assert.Null(WindowLayout.Parse(""));
        var nested = string.Concat(Enumerable.Repeat("{\"children\":[", 200)) + string.Concat(Enumerable.Repeat("]}", 200));
        Assert.Null(WindowLayout.Parse(nested));
    }
}

public sealed class BarOptimizerTests
{
    private static BarPiece P(decimal length) => new("Parça", length);

    [Fact]
    public void PlacesPiecesLongestFirst_CountingKerfBetweenPieces()
    {
        var plan = BarOptimizer.Optimize("Kasa", new[] { P(1000), P(4000), P(996), P(2000) }, 6000, 4);

        Assert.Equal(2, plan.Bars.Count);
        // 4000 + 4 + 2000 boyu aşar, 2000 ikinci boya geçer; 996 da ilk boya kerf ile sığmaz (6004).
        Assert.Equal(new[] { 4000m, 1000m }, plan.Bars[0].Pieces.Select(p => p.LengthMm));
        Assert.Equal(new[] { 2000m, 996m }, plan.Bars[1].Pieces.Select(p => p.LengthMm));
    }

    [Fact]
    public void ExactFitIncludingKerf_StaysOnOneBar_OneMillimetreMoreDoesNot()
    {
        Assert.Single(BarOptimizer.Optimize("K", new[] { P(3000), P(2996) }, 6000, 4).Bars);
        Assert.Equal(2, BarOptimizer.Optimize("K", new[] { P(3000), P(2997) }, 6000, 4).Bars.Count);
    }

    [Fact]
    public void WasteAndUtilisation_AreCalculated()
    {
        var plan = BarOptimizer.Optimize("K", new[] { P(3000), P(2000) }, 6000, 4);
        var bar = Assert.Single(plan.Bars);
        Assert.Equal(5004m, bar.UsedMm);
        Assert.Equal(996m, bar.WasteMm);
        Assert.Equal(83.3m, plan.UtilisationPercent);
    }

    [Fact]
    public void PieceLongerThanBar_Throws()
        => Assert.Throws<ArgumentException>(() => BarOptimizer.Optimize("K", new[] { P(6001) }, 6000, 4));

    [Fact]
    public void NoPieces_ProducesEmptyPlan()
        => Assert.Equal(0m, BarOptimizer.Optimize("K", Array.Empty<BarPiece>(), 6000, 4).UtilisationPercent);
}

public sealed class ProductCalculatorTests
{
    private static readonly ProfileSystem System = new() { Name = "Test", SashWidthMm = 30, GlassClearanceMm = 5, BarLengthMm = 6000, WastePercent = 10, LaborPercent = 20, MarginPercent = 50 };
    private static readonly ProductSettings Settings = new()
    {
        BoxHeightMm = 165, LamellaHeightMm = 39, GuideDeductMm = 25, LamellaPricePerM2 = 500, GuidePricePerM = 100, BoxPricePerM = 200, MotorPrice = 3000, ManualPrice = 400,
        PostWidthMm = 40, PostSpacingMm = 1000, RailHeightMm = 40, BarSpacingMm = 110, PostPricePerM = 150, RailPricePerM = 250, BarPricePerM = 50,
        PanelOverlapMm = 20, PanelProfilePricePerM = 120, PanelHardwarePrice = 80
    };

    private static WindowCalculation Calc(string category, int w, int h, ProductParams p, int quantity = 1) => ProductCalculator.Calculate(category, w, h, quantity, p, System, Settings);
    private static CutPiece Cut(WindowCalculation c, string label) => Assert.Single(c.Cuts, x => x.Label == label);

    [Fact]
    public void Shutter_CalculatesLamellasGuidesBoxAndMotor()
    {
        var c = Calc(ProductCategories.Shutter, 1000, 1365, new ProductParams { Drive = "motor" });
        Assert.True(c.IsValid, string.Join(";", c.Errors));
        Assert.Equal(950m, Cut(c, "Lamel").LengthMm);      // 1000 - 2*25
        Assert.Equal(31, Cut(c, "Lamel").Count);           // (1365-165)/39 = 30,77 -> 31
        Assert.Equal(1200m, Cut(c, "Yan dikme").LengthMm);
        Assert.Equal(2, Cut(c, "Yan dikme").Count);
        Assert.Equal(1000m, Cut(c, "Panjur kutusu").LengthMm);
        Assert.Equal("Motor", Assert.Single(c.Hardware).Name);
        Assert.Empty(c.Glass);
        // Lamel 0,95 x 1,2 x 500 = 570; dikme 2,4 x 100 = 240; kutu 1 x 200 = 200; toplam 1010 x 1,10 = 1111
        Assert.Equal(1111m, c.ProfileCost);
        Assert.Equal(3000m, c.HardwareCost);
    }

    [Fact]
    public void Shutter_ManualDrive_UsesManualPrice_AndRejectsTooSmall()
    {
        Assert.Equal(400m, Calc(ProductCategories.Shutter, 1000, 1000, new ProductParams { Drive = "manual" }).HardwareCost);
        Assert.False(Calc(ProductCategories.Shutter, 1000, 1000, new ProductParams { Drive = "x" }).IsValid);
        Assert.Contains(ProductCalculator.Calculate(ProductCategories.Shutter, 1000, 200, 1, new ProductParams { Drive = "motor" }, System, new ProductSettings { BoxHeightMm = 300 }).Errors, e => e.Contains("kutu"));
    }

    [Fact]
    public void GlassRailing_CalculatesPostsRailsAndReportsGlassWithoutPrice()
    {
        var c = Calc(ProductCategories.Railing, 4000, 1100, new ProductParams { Infill = "glass" });
        Assert.True(c.IsValid, string.Join(";", c.Errors));
        Assert.Equal(5, Cut(c, "Dikme").Count);             // 4 bölme + 1
        Assert.Equal(1100m, Cut(c, "Dikme").LengthMm);
        Assert.Equal(4000m, Cut(c, "Küpeşte").LengthMm);
        var glass = Assert.Single(c.Glass);
        // Bölme (4000 - 5*40)/4 = 950; cam 950 - 2*5 = 940 x (1100 - 2*40 - 2*5) = 1010
        Assert.Equal(940m, glass.WidthMm);
        Assert.Equal(1010m, glass.HeightMm);
        Assert.Equal(4, glass.Count);
        Assert.Equal(0m, c.PanelCost);
    }

    [Fact]
    public void BarRailing_HasBarsAndNoGlass()
    {
        var c = Calc(ProductCategories.Railing, 2000, 1000, new ProductParams { Infill = "bars" });
        Assert.Empty(c.Glass);
        // 2 bölme, bölme genişliği (2000-3*40)/2 = 940; 940/110 = 8 çubuk; 16 adet, 1000-80 = 920 mm
        Assert.Equal(16, Cut(c, "Emniyet çubuğu").Count);
        Assert.Equal(920m, Cut(c, "Emniyet çubuğu").LengthMm);
    }

    [Fact]
    public void LongRail_IsSplitIntoBars_WithWarning()
    {
        var c = Calc(ProductCategories.Railing, 7000, 1100, new ProductParams { Infill = "bars" });
        Assert.True(c.IsValid, string.Join(";", c.Errors));
        Assert.Contains(c.Cuts, x => x.Label == "Küpeşte" && x.LengthMm == 6000m);
        Assert.Contains(c.Cuts, x => x.Label == "Küpeşte" && x.LengthMm == 1000m);
        Assert.Contains(c.Warnings, w => w.Contains("ek yeri"));
    }

    [Fact]
    public void SlidingBalcony_UsesOverlap_AndReportsGlass()
    {
        var c = Calc(ProductCategories.GlassBalcony, 4000, 2000, new ProductParams { OpenType = "sliding", PanelCount = 4 });
        Assert.True(c.IsValid, string.Join(";", c.Errors));
        // Panel (4000 + 3*20)/4 = 1015; yükseklik 2000-80 = 1920
        Assert.Equal(1015m, Cut(c, "Kanat yatay").LengthMm);
        Assert.Equal(8, Cut(c, "Kanat yatay").Count);
        Assert.Equal(1920m, Cut(c, "Kanat dikey").LengthMm);
        var glass = Assert.Single(c.Glass);
        Assert.Equal(4, glass.Count);
        Assert.Equal(945m, glass.WidthMm);   // 1015 - 2*30 - 2*5
        Assert.Equal(1850m, glass.HeightMm);
        Assert.Equal(320m, c.HardwareCost);
    }

    [Fact]
    public void FoldingBalcony_HasNoOverlap()
        => Assert.Equal(1000m, Cut(Calc(ProductCategories.GlassBalcony, 4000, 2000, new ProductParams { OpenType = "folding", PanelCount = 4 }), "Kanat yatay").LengthMm);

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Balcony_RejectsInvalidPanelCount(int count)
        => Assert.False(Calc(ProductCategories.GlassBalcony, 4000, 2000, new ProductParams { PanelCount = count }).IsValid);

    [Theory]
    [InlineData("yok", 1000, 1000)]
    [InlineData("shutter", 100, 1000)]
    [InlineData("railing", 1000, 100)]
    public void InvalidInput_IsRejected(string category, int w, int h)
        => Assert.False(Calc(category, w, h, new ProductParams()).IsValid);
}

public sealed class GlassPlateOptimizerTests
{
    private static (string, decimal, decimal) P(decimal w, decimal h) => ("Cam", w, h);

    [Fact]
    public void PiecesFitOnOnePlate_WithoutOverlap()
    {
        var plan = GlassPlateOptimizer.Optimize(new[] { P(1000, 1000), P(1000, 1000), P(1000, 1000), P(500, 500) }, 3210, 2250, 2);
        var plate = Assert.Single(plan.Plates);
        Assert.Equal(4, plate.Pieces.Count);
        foreach (var a in plate.Pieces)
        {
            Assert.True(a.X + a.WidthMm <= 3210 && a.Y + a.HeightMm <= 2250);
            foreach (var b in plate.Pieces.Where(x => x != a))
                Assert.False(a.X < b.X + b.WidthMm && b.X < a.X + a.WidthMm && a.Y < b.Y + b.HeightMm && b.Y < a.Y + a.HeightMm);
        }
    }

    [Fact]
    public void Overflow_OpensNewPlate_AndKeepsEveryPiece()
    {
        var pieces = Enumerable.Range(0, 20).Select(_ => P(1500, 1000)).ToList();
        var plan = GlassPlateOptimizer.Optimize(pieces, 3210, 2250, 2);
        Assert.Equal(20, plan.Plates.Sum(p => p.Pieces.Count));
        Assert.True(plan.Plates.Count > 1);
        Assert.Empty(plan.TooLarge);
    }

    [Fact]
    public void TallPiece_IsRotatedToFit()
    {
        var plan = GlassPlateOptimizer.Optimize(new[] { P(1000, 3000) }, 3210, 2250, 2);
        var piece = Assert.Single(Assert.Single(plan.Plates).Pieces);
        Assert.True(piece.Rotated);
        Assert.Equal(3000m, piece.WidthMm);
        Assert.Equal(1000m, piece.HeightMm);
    }

    [Fact]
    public void TooLargePiece_IsReported_NotDropped()
    {
        var plan = GlassPlateOptimizer.Optimize(new[] { P(4000, 3000), P(500, 500) }, 3210, 2250, 2);
        Assert.Single(plan.TooLarge);
        Assert.Single(Assert.Single(plan.Plates).Pieces);
    }

    [Fact]
    public void Gap_IsRespectedBetweenPieces()
    {
        var plan = GlassPlateOptimizer.Optimize(new[] { P(1000, 500), P(1000, 500) }, 2001, 500, 2);
        Assert.Equal(2, plan.Plates.Count); // 1000 + 2 + 1000 = 2002 > 2001
        Assert.Single(GlassPlateOptimizer.Optimize(new[] { P(1000, 500), P(1000, 500) }, 2002, 500, 2).Plates);
    }

    [Fact]
    public void Utilisation_IsCalculated()
        => Assert.Equal(50m, GlassPlateOptimizer.Optimize(new[] { P(1000, 1000) }, 2000, 1000, 0).UtilisationPercent);
}

public sealed class GlassPlateGapTests
{
    [Fact]
    public void Gap_IsAppliedAfterEveryPieceOnShelf()
    {
        // 3 x 1000 + 2 boşluk x 2 = 3004 > 3003: üçüncü parça aynı rafa sığmaz.
        var pieces = Enumerable.Range(0, 3).Select(_ => ("Cam", 1000m, 500m)).ToList();
        var tight = GlassPlateOptimizer.Optimize(pieces, 3003, 2250, 2);
        Assert.Contains(Assert.Single(tight.Plates).Pieces, p => p.Y > 0);
        var fits = GlassPlateOptimizer.Optimize(pieces, 3004, 2250, 2);
        Assert.All(Assert.Single(fits.Plates).Pieces, p => Assert.Equal(0m, p.Y));
    }
}

public sealed class WindowDrawingsTests
{
    private const string Leaf = "{\"opening\":\"fixed\"}";

    private static List<string> Window(int width, int height, string layout, string shape = "rect", int param = 0)
        => WindowDrawings.Validate(ProductCategories.Aluminum, width, height, shape, param, 1, layout, null);

    private static List<string> Product(string category, string json)
        => WindowDrawings.Validate(category, 1500, 1400, "rect", 0, 1, null, json);

    [Fact]
    public void Parts_MustAddUpToOuterSize()
    {
        Assert.Empty(Window(1800, 1400, $"{{\"split\":\"v\",\"sizes\":[900,900],\"children\":[{Leaf},{Leaf}]}}"));
        Assert.Contains(Window(1800, 1400, $"{{\"split\":\"v\",\"sizes\":[900,800],\"children\":[{Leaf},{Leaf}]}}"), e => e.Contains("toplamı"));
        Assert.Contains(Window(1800, 1400, $"{{\"split\":\"h\",\"sizes\":[900,900],\"children\":[{Leaf},{Leaf}]}}"), e => e.Contains("toplamı"));
    }

    [Fact]
    public void NestedParts_AreCheckedAgainstTheirOwnPart()
    {
        var inner = $"{{\"split\":\"h\",\"sizes\":[400,1000],\"children\":[{Leaf},{Leaf}]}}";
        Assert.Empty(Window(1800, 1400, $"{{\"split\":\"v\",\"sizes\":[900,900],\"children\":[{inner},{Leaf}]}}"));
        var wide = $"{{\"split\":\"v\",\"sizes\":[900,900],\"children\":[{Leaf},{Leaf}]}}";
        Assert.Contains(Window(1800, 1400, $"{{\"split\":\"v\",\"sizes\":[900,900],\"children\":[{wide},{Leaf}]}}"), e => e.Contains("toplamı"));
    }

    [Theory]
    [InlineData("{bozuk")]
    [InlineData("{\"split\":\"x\",\"sizes\":[750,750],\"children\":[{},{}]}")]
    [InlineData("{\"split\":\"v\",\"sizes\":[1500],\"children\":[{}]}")]
    [InlineData("{\"split\":\"v\",\"sizes\":[-100,1600],\"children\":[{},{}]}")]
    [InlineData("{\"split\":\"v\",\"sizes\":[70000000000000000000000000000,70000000000000000000000000000],\"children\":[{},{}]}")]
    [InlineData("{\"opening\":\"ucan\"}")]
    [InlineData("{\"fill\":\"tahta\"}")]
    public void BrokenLayouts_AreRejected(string layout) => Assert.NotEmpty(Window(1500, 1400, layout));

    [Theory]
    [InlineData(100, 1400)]
    [InlineData(1500, 100)]
    [InlineData(100001, 1400)]
    [InlineData(1500, 20001)]
    public void OuterSizes_MustBeDrawable(int width, int height) => Assert.NotEmpty(Window(width, height, Leaf));

    [Theory]
    [InlineData("arch", 400, true)]
    [InlineData("arch", 0, false)]
    [InlineData("arch", 751, false)]
    [InlineData("slopeL", 1000, true)]
    [InlineData("slopeR", 1400, false)]
    [InlineData("oval", 100, false)]
    public void ShapeSizes_AreChecked(string shape, int param, bool valid)
        => Assert.Equal(valid, Window(1500, 1400, Leaf, shape, param).Count == 0);

    [Theory]
    [InlineData("shutter", "{\"drive\":\"manual\"}", true)]
    [InlineData("shutter", "{\"drive\":\"uçan\"}", false)]
    [InlineData("railing", "{\"infill\":\"bars\",\"postSpacingMm\":0}", true)]
    [InlineData("railing", "{\"infill\":\"tahta\"}", false)]
    [InlineData("railing", "{\"postSpacingMm\":50}", false)]
    [InlineData("balcony", "{\"openType\":\"folding\",\"panelCount\":6}", true)]
    [InlineData("balcony", "{\"panelCount\":31}", false)]
    [InlineData("balcony", "{\"openType\":\"uçan\"}", false)]
    [InlineData("shutter", "{bozuk", false)]
    public void ProductOptions_AreChecked(string category, string json, bool valid)
        => Assert.Equal(valid, Product(category, json).Count == 0);

    [Fact]
    public void UnknownCategory_IsRejected()
        => Assert.NotEmpty(WindowDrawings.Validate("masa", 1500, 1400, "rect", 0, 1, Leaf, "{}"));

    [Fact]
    public void Drawing_CarriesOnlyTheShape()
    {
        var json = JsonSerializer.Serialize(WindowDrawings.Drawing(new WindowDesign { WidthMm = 1500, HeightMm = 1400, LayoutJson = Leaf }), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"root\":{", json);
        Assert.Contains("\"widthMm\":1500", json);
        foreach (var removed in new[] { "cut", "price", "cost", "profile", "frameWidth" })
            Assert.DoesNotContain(removed, json, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DrawingLists
{
    public const string Layout = "{\"id\":\"root\",\"opening\":\"tiltRight\",\"fill\":\"glass\"}";
    public const string Url = "/yonetim/cizim/ekle";

    public static object Row(string? name, int width = 1000, int height = 1000, int quantity = 2, string layout = Layout, string category = "aluminum", int? system = null, int? brand = null, int? id = null)
        => new { id, category, name, widthMm = width, heightMm = height, quantity, layoutJson = layout, profileSystemId = system, brandId = brand };

    public static object Product(string? name, string category, string options, int width = 1000, int height = 1400, int? brand = null, int? id = null)
        => new { id, category, name, widthMm = width, heightMm = height, quantity = 1, paramsJson = options, brandId = brand };

    public static string Rows(params object[] rows) => JsonSerializer.Serialize(rows);

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string listName, string rows, params (string, string)[] extra)
        => PostToAsync(client, token, Url, listName, rows, extra);

    public static Task<HttpResponseMessage> PostToAsync(HttpClient client, string token, string url, string listName, string rows, params (string, string)[] extra)
        => client.PostFormAsync(url, token, new[] { ("Name", listName), ("ColorHex", "#3a4046"), ("ItemsJson", rows) }.Concat(extra).ToArray());

    public static Task<int> ListIdAsync(AppFactory app, string name)
        => app.WithDbAsync(db => db.DrawingLists.Where(l => l.Name == name).Select(l => l.Id).SingleAsync());

    public static Task<int> DesignIdAsync(AppFactory app, string name)
        => app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());
}

[Collection("postgres")]
public sealed class DrawingPageTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync(DrawingLists.Url));
    }

    private static string Name() => "Çizim " + Guid.NewGuid().ToString("N")[..8];

    private static Task<int> NewQuoteAsync(AppFactory app, QuoteStatus status) => app.WithDbAsync(async db =>
    {
        var customer = new Customer { Name = "Çizim müşteri " + Guid.NewGuid().ToString("N")[..6] };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var quote = new Quote { QuoteNumber = "C-" + Guid.NewGuid().ToString("N")[..10], Title = "Test", CustomerId = customer.Id, Status = status, TaxRate = 20 };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    });

    [Fact]
    public async Task Seed_CreatesPricelessExamples_AndBrandsWithoutLogos()
    {
        var app = postgres.Shared;
        app.NewClient();
        Assert.Equal(0m, (await app.WithDbAsync(db => db.GlassTypes.FirstAsync(g => g.Name.StartsWith("Örnek")))).PricePerM2);
        var systems = await app.WithDbAsync(db => db.ProfileSystems.Where(p => p.Name.StartsWith("Örnek")).ToListAsync());
        Assert.All(ProductCategories.All, category => Assert.Contains(systems, p => p.Category == category));
        Assert.All(systems, p => Assert.Equal(0m, p.FramePricePerM));

        var brands = await app.WithDbAsync(db => db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Name));
        Assert.Equal(ProductCategories.Aluminum, brands["Linea Rossa"].Category);
        foreach (var name in new[] { "Winer", "Egepen", "Winsa" }) Assert.Equal(ProductCategories.Pvc, brands[name].Category);
        foreach (var name in new[] { "Linea Rossa", "Winer", "Egepen", "Winsa" }) Assert.Null(brands[name].LogoPath);
    }

    [Fact]
    public async Task NewDrawing_IsAlwaysAList_ThereIsNoSingleDrawingPage()
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        var editor = await client.GetStringAsync(DrawingLists.Url);
        Assert.Contains("id=\"dl-rows\"", editor);
        Assert.Contains("drawing-list.js", editor);
        Assert.DoesNotContain("wd-canvas", editor);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/yonetim/cizim/toplu-ekle")).StatusCode);
    }

    [Fact]
    public async Task SingleDrawing_IsSavedAsOneRowList_AndShownWithoutManufacturing()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();

        Assert.Equal(HttpStatusCode.Redirect, (await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name)))).StatusCode);
        var listId = await DrawingLists.ListIdAsync(app, name);
        Assert.Equal(1, await app.WithDbAsync(db => db.WindowDesigns.CountAsync(d => d.DrawingListId == listId)));

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{listId}"));
        Assert.Contains("data-drawing", page);
        Assert.Contains("\"opening\":\"tiltRight\"", page);
        Assert.Contains("100 × 100 cm", page);
        foreach (var removed in new[] { "Kesim", "Maliyet", "Optimiz", "Profil", "İmalat" }) Assert.DoesNotContain(removed, page);
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim")));

        var designId = await DrawingLists.DesignIdAsync(app, name);
        var editor = await client.GetStringAsync($"/yonetim/cizim/poz/{designId}");
        Assert.Contains("id=\"wd-canvas\"", editor);
        Assert.Contains($"/yonetim/cizim/duzenle/{listId}", editor);
        var saved = await client.PostFormAsync($"/yonetim/cizim/poz/{designId}", token, ("Name", name), ("Category", "aluminum"), ("ColorHex", "#3a4046"),
            ("WidthMm", "1500"), ("HeightMm", "1000"), ("Shape", "rect"), ("ShapeParamMm", "0"), ("Quantity", "2"), ("LayoutJson", DrawingLists.Layout));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == designId).Select(d => d.WidthMm).SingleAsync()));
    }

    [Theory]
    [InlineData("/yonetim/cizim/imalat/1")]
    [InlineData("/yonetim/cizim/optimizasyon")]
    [InlineData("/yonetim/cizim/sistemler")]
    public async Task ManufacturingPages_AreClosedByDefault(string url)
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task AddToQuote_AddsPricedRowsOnly_AndRecalculatesDraft_ButNotOtherStatuses()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var list = Name();
        string[] names = [Name(), Name()];
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row(names[0]), DrawingLists.Row(names[1])));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var first = await DrawingLists.DesignIdAsync(app, names[0]);
        var second = await DrawingLists.DesignIdAsync(app, names[1]);

        var draft = await NewQuoteAsync(app, QuoteStatus.Draft);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{listId}", token, ("quoteId", draft.ToString()), ($"prices[{first}]", "1250.50"), ($"prices[{second}]", ""));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == draft));
        var line = Assert.Single(quote.Items);
        Assert.Contains(names[0], line.Description);
        Assert.Contains("100x100 cm", line.Description);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(1250.50m, line.UnitPrice);
        Assert.Equal(line.LineTotal, quote.Subtotal);
        Assert.True(quote.GrandTotal > quote.Subtotal);

        var sent = await NewQuoteAsync(app, QuoteStatus.Sent);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{listId}", token, ("quoteId", sent.ToString()), ($"prices[{first}]", "100"));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == sent)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public async Task AddToQuote_RejectsMissingPrice(string price)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name)));
        var quoteId = await NewQuoteAsync(app, QuoteStatus.Draft);

        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{await DrawingLists.ListIdAsync(app, name)}", token, ("quoteId", quoteId.ToString()), ($"prices[{await DrawingLists.DesignIdAsync(app, name)}]", price));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));
    }

    [Fact]
    public async Task AddToQuote_IgnoresDrawingsOfOtherLists()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        string[] names = [Name(), Name()];
        foreach (var name in names) await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name)));
        var quoteId = await NewQuoteAsync(app, QuoteStatus.Draft);

        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{await DrawingLists.ListIdAsync(app, names[0])}", token, ("quoteId", quoteId.ToString()), ($"prices[{await DrawingLists.DesignIdAsync(app, names[1])}]", "100"));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));
    }

    [Fact]
    public async Task CopyAndDelete_WorkOnTheWholeList()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var list = Name();
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row(Name()), DrawingLists.Row(Name())));
        var id = await DrawingLists.ListIdAsync(app, list);

        await client.PostFormAsync($"/yonetim/cizim/kopyala/{id}", token);
        Assert.Equal(2, await app.WithDbAsync(db => db.WindowDesigns.CountAsync(d => d.DrawingList!.Name == list + " (kopya)")));

        await client.PostFormAsync($"/yonetim/cizim/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.DrawingLists.AnyAsync(l => l.Id == id)));
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.DrawingListId == id)));
        Assert.Equal(2, await app.WithDbAsync(db => db.WindowDesigns.CountAsync(d => d.DrawingList!.Name == list + " (kopya)")));
    }

    [Fact]
    public async Task ExistingDatabaseWithoutDrawingTables_IsUpgradedOnStart()
    {
        await using (var first = postgres.CreateApp("drawing_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"WindowDesigns\"; DROP TABLE \"DrawingLists\"; DROP TABLE \"Brands\"; DROP TABLE \"GlassListItems\"; DROP TABLE \"GlassLists\"; DROP TABLE \"GlassTypes\""));
        }

        await using var second = postgres.CreateApp("drawing_upgrade", schemaMode: "existing");
        second.NewClient();
        Assert.Equal(0, await second.WithDbAsync(db => db.WindowDesigns.CountAsync()));
        Assert.Equal(0, await second.WithDbAsync(db => db.DrawingLists.CountAsync()));
    }

    // Liste öncesi veritabanı: her çizim kendi listesine taşınır, hiçbir veri silinmez.
    [Fact]
    public async Task OldDatabase_MovesEachDrawingToItsOwnList_AndKeepsEverything()
    {
        const string sizes = "{\"split\":\"v\",\"sizes\":[1,1],\"children\":[{},{}]}";
        int systemId, designId, customerId;
        await using (var first = postgres.CreateApp("shape_upgrade"))
        {
            first.NewClient();
            (systemId, designId, customerId) = await first.WithDbAsync(async db =>
            {
                var system = new ProfileSystem { Name = "Eski sistem", Category = ProductCategories.Aluminum };
                var customer = new Customer { Name = "Eski müşteri" };
                db.AddRange(system, customer);
                await db.SaveChangesAsync();
                var design = new WindowDesign { DrawingList = new DrawingList { Name = "x" }, Name = "Eski çizim", CustomerId = customer.Id, ProfileSystemId = system.Id, WidthMm = 1500, HeightMm = 1400, LayoutJson = sizes };
                db.WindowDesigns.Add(design);
                db.WindowDesigns.Add(new WindowDesign { DrawingList = new DrawingList { Name = "y" }, Name = "İkinci eski", WidthMm = 1000, HeightMm = 1000, LayoutJson = "{}" });
                db.GlassTypes.Add(new GlassType { Name = "Eski cam", SheetWidthMm = 3000 });
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "WindowDesigns" ALTER COLUMN "ProfileSystemId" DROP NOT NULL;
                    ALTER TABLE "WindowDesigns" DROP COLUMN "DrawingListId", DROP COLUMN "BrandId", DROP COLUMN "SortOrder";
                    DROP TABLE "DrawingLists"; DROP TABLE "Brands";
                    """);
                return (system.Id, design.Id, customer.Id);
            });
        }

        await using var second = postgres.CreateApp("shape_upgrade", schemaMode: "existing");
        var (client, _) = await AdminAsync(second);
        var row = await second.WithDbAsync(db => db.WindowDesigns.AsNoTracking().Include(d => d.DrawingList).SingleAsync(d => d.Id == designId));
        Assert.Equal(systemId, row.ProfileSystemId);
        Assert.Equal(sizes, row.LayoutJson);
        Assert.Equal("Eski çizim", row.DrawingList!.Name);
        Assert.Equal(customerId, row.DrawingList.CustomerId);
        Assert.Equal(2, await second.WithDbAsync(db => db.DrawingLists.CountAsync()));
        Assert.Equal(3000m, await second.WithDbAsync(db => db.GlassTypes.Where(g => g.Name == "Eski cam").Select(g => g.SheetWidthMm).SingleAsync()));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/detay/{row.DrawingListId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/duzenle/{row.DrawingListId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/poz/{designId}")).StatusCode);

        // İkinci açılışta yeniden taşıma yapılmaz.
        await using var third = postgres.CreateApp("shape_upgrade", schemaMode: "existing");
        third.NewClient();
        Assert.Equal(2, await third.WithDbAsync(db => db.DrawingLists.CountAsync()));
    }

    // Şekil sürümünde (e565411) silinen kolonlar geri gelir; çizimler sistemsiz kalır.
    [Fact]
    public async Task DatabaseWithoutSystemLink_IsRepaired()
    {
        await using (var first = postgres.CreateApp("shape_repair"))
        {
            first.NewClient();
            await first.WithDbAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "WindowDesigns" DROP COLUMN "ProfileSystemId", DROP COLUMN "DrawingListId", DROP COLUMN "BrandId", DROP COLUMN "SortOrder";
                    ALTER TABLE "GlassTypes" DROP COLUMN "SheetWidthMm", DROP COLUMN "SheetHeightMm", DROP COLUMN "CutGapMm";
                    INSERT INTO "WindowDesigns" ("Name","Category","ColorHex","WidthMm","HeightMm","Shape","ShapeParamMm","Quantity","LayoutJson","ParamsJson","CreatedUtc","UpdatedUtc")
                    VALUES ('Şekil çizimi','aluminum','#ffffff',1500,1400,'rect',0,1,'{{}}','{{}}',now(),now());
                    """);
                return 0;
            });
        }

        await using var second = postgres.CreateApp("shape_repair", schemaMode: "existing");
        second.NewClient();
        Assert.Null(await second.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == "Şekil çizimi").Select(d => d.ProfileSystemId).SingleAsync()));
        Assert.Equal(3210m, await second.WithDbAsync(db => db.GlassTypes.Select(g => g.SheetWidthMm).FirstAsync()));
    }
}

[Collection("postgres")]
public sealed class GlassListTests(PostgresFixture postgres)
{
    private static string Name() => "Cam Liste " + Guid.NewGuid().ToString("N")[..8];

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/camlar/ekle"));
    }

    private static Task<int> NewTypeAsync(AppFactory app, decimal price = 500) => app.WithDbAsync(async db =>
    {
        var type = new GlassType { Name = "Tip " + Guid.NewGuid().ToString("N")[..6], PricePerM2 = price };
        db.GlassTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    });

    private static string Rows(params object[] rows) => JsonSerializer.Serialize(rows);

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, string name, string rows)
        => client.PostFormAsync(url, token, ("Name", name), ("ItemsJson", rows));

    [Fact]
    public async Task List_StoresMultipleSizes_AndCalculatesAreaAndPrice()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var a = await NewTypeAsync(app, 500);
        var b = await NewTypeAsync(app, 1000);
        var name = Name();

        var saved = await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(
            new { glassTypeId = a, position = "P1", widthMm = 1000, heightMm = 1000, quantity = 3 },
            new { glassTypeId = a, position = "P2", widthMm = 500, heightMm = 800, quantity = 5 },
            new { glassTypeId = b, position = "P3", widthMm = 1000, heightMm = 500, quantity = 2 }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var id = await app.WithDbAsync(db => db.GlassLists.Where(g => g.Name == name).Select(g => g.Id).SingleAsync());
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/camlar/detay/{id}"));
        Assert.Contains("P1", html);
        Assert.Contains("100 × 100", html); // varsayılan birim cm
        Assert.DoesNotContain("Plaka", html); // üretim detayları kapalı
        // 3 m² + 2 m² = 5 m² x 500 = 2500; 1 m² x 1000 = 1000; toplam 3500
        var list = await app.WithDbAsync(db => db.GlassLists.AsNoTracking().Include(g => g.Items).ThenInclude(i => i.GlassType).SingleAsync(g => g.Id == id));
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(3500m, KarakusAluminyum.Web.Areas.Admin.Controllers.GlassListsController.ListTotal(list));
    }

    [Fact]
    public async Task Amounts_UseTurkishNumberFormat()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var type = await NewTypeAsync(app, 1000);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(new { glassTypeId = type, widthMm = 2000, heightMm = 2000, quantity = 1 }));
        var id = await app.WithDbAsync(db => db.GlassLists.Where(g => g.Name == name).Select(g => g.Id).SingleAsync());
        Assert.Contains("4.000,00 ₺", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/camlar/detay/{id}")));
    }

    [Theory]
    [InlineData("{bozuk")]
    [InlineData("[]")]
    public async Task List_RejectsBrokenOrEmptyRows(string rows)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, "/yonetim/camlar/ekle", name, rows)).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.GlassLists.AnyAsync(g => g.Name == name)));
    }

    [Fact]
    public async Task List_RejectsInvalidRowValues()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var type = await NewTypeAsync(app);
        foreach (var row in new object[]
        {
            new { glassTypeId = type, widthMm = 0, heightMm = 100, quantity = 1 },
            new { glassTypeId = type, widthMm = 100, heightMm = 25000, quantity = 1 },
            new { glassTypeId = type, widthMm = 100, heightMm = 100, quantity = 0 },
            new { glassTypeId = 999999, widthMm = 100, heightMm = 100, quantity = 1 }
        })
        {
            var name = Name();
            Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(row))).StatusCode);
            Assert.False(await app.WithDbAsync(db => db.GlassLists.AnyAsync(g => g.Name == name)));
        }
    }

    [Fact]
    public async Task Edit_ReplacesRows_AndDeleteAndCopyWork()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var type = await NewTypeAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(new { glassTypeId = type, widthMm = 100, heightMm = 100, quantity = 1 }));
        var id = await app.WithDbAsync(db => db.GlassLists.Where(g => g.Name == name).Select(g => g.Id).SingleAsync());

        await SaveAsync(client, token, $"/yonetim/camlar/duzenle/{id}", name, Rows(new { glassTypeId = type, widthMm = 200, heightMm = 300, quantity = 4 }, new { glassTypeId = type, widthMm = 250, heightMm = 300, quantity = 1 }));
        Assert.Equal(2, await app.WithDbAsync(db => db.GlassListItems.CountAsync(i => i.GlassListId == id)));
        Assert.Contains("\"widthMm\":200", await client.GetStringAsync($"/yonetim/camlar/duzenle/{id}").ContinueWith(t => WebUtility.HtmlDecode(t.Result)));

        await client.PostFormAsync($"/yonetim/camlar/kopyala/{id}", token);
        Assert.Equal(2, await app.WithDbAsync(db => db.GlassListItems.CountAsync(i => i.GlassList!.Name == name + " (kopya)")));

        await client.PostFormAsync($"/yonetim/camlar/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.GlassListItems.AnyAsync(i => i.GlassListId == id)));
    }

    [Fact]
    public async Task UsedGlassType_CannotBeDeleted_UnusedCan()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var used = await NewTypeAsync(app);
        var free = await NewTypeAsync(app);
        await SaveAsync(client, token, "/yonetim/camlar/ekle", Name(), Rows(new { glassTypeId = used, widthMm = 100, heightMm = 100, quantity = 1 }));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/camlar/tipler/sil/{used}", token)).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.GlassTypes.AnyAsync(g => g.Id == used)));
        await client.PostFormAsync($"/yonetim/camlar/tipler/sil/{free}", token);
        Assert.False(await app.WithDbAsync(db => db.GlassTypes.AnyAsync(g => g.Id == free)));
    }

    [Fact]
    public async Task List_AddsToDraftQuote_OnlyWithPrice()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var type = await NewTypeAsync(app, 1000);
        var free = await NewTypeAsync(app, 0);
        var name = Name();
        var freeName = Name();
        await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(new { glassTypeId = type, widthMm = 1000, heightMm = 1000, quantity = 2 }));
        await SaveAsync(client, token, "/yonetim/camlar/ekle", freeName, Rows(new { glassTypeId = free, widthMm = 1000, heightMm = 1000, quantity = 2 }));
        var ids = await app.WithDbAsync(db => db.GlassLists.Where(g => g.Name == name || g.Name == freeName).ToDictionaryAsync(g => g.Name, g => g.Id));
        var quoteId = await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = "Cam müşteri " + Guid.NewGuid().ToString("N")[..6] };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var quote = new Quote { QuoteNumber = "G-" + Guid.NewGuid().ToString("N")[..10], Title = "Cam", CustomerId = customer.Id, VatMode = VatMode.None };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });

        await client.PostFormAsync($"/yonetim/camlar/teklife-ekle/{ids[freeName]}", token, ("quoteId", quoteId.ToString()));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));

        await client.PostFormAsync($"/yonetim/camlar/teklife-ekle/{ids[name]}", token, ("quoteId", quoteId.ToString()));
        Assert.Equal(2000m, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quoteId).Select(q => q.GrandTotal).SingleAsync()));
    }
}

[Collection("postgres")]
public sealed class CategoryPageTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync(DrawingLists.Url));
    }

    [Theory]
    [InlineData("aluminum")]
    [InlineData("shutter")]
    [InlineData("railing")]
    public async Task NewList_PreselectsCategoryFromTheTab(string category)
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        Assert.Contains($"{DrawingLists.Url}?tur={category}", await client.GetStringAsync($"/yonetim/cizim?tur={category}"));
        Assert.Matches($"<option[^>]*value=\"{category}\"[^>]*selected|<option[^>]*selected[^>]*value=\"{category}\"", await client.GetStringAsync($"{DrawingLists.Url}?tur={category}"));
    }

    [Theory]
    [InlineData("aluminum", "wd-canvas", true)]
    [InlineData("pvc", "wd-canvas", true)]
    [InlineData("shutter", "pd-canvas", false)]
    [InlineData("railing", "pd-canvas", false)]
    [InlineData("balcony", "pd-canvas", false)]
    public async Task EachCategory_OpensItsDrawingEditor(string category, string canvas, bool isWindow)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Tür " + Guid.NewGuid().ToString("N")[..8];
        var row = isWindow ? DrawingLists.Row(name, category: category) : DrawingLists.Product(name, category, "{}");
        await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(row));
        Assert.Contains(canvas, await client.GetStringAsync($"/yonetim/cizim/poz/{await DrawingLists.DesignIdAsync(app, name)}"));
    }

    [Fact]
    public async Task MixedList_SavesEveryCategory_AndTabsFilterLists()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Karışık " + Guid.NewGuid().ToString("N")[..8];

        var saved = await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name + " pencere"), DrawingLists.Product(name + " panjur", "shutter", "{\"drive\":\"manual\"}")));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var shutter = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Name == name + " panjur"));
        Assert.Equal(ProductCategories.Shutter, shutter.Category);
        Assert.Contains("manual", shutter.ParamsJson);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{shutter.DrawingListId}"));
        Assert.Contains("\"category\":\"shutter\"", page);
        Assert.Contains("\"drive\":\"manual\"", page);
        Assert.Contains("\"category\":\"aluminum\"", page);

        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=shutter")));
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=aluminum")));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=railing")));
    }
}

public sealed class LengthUnitTests
{
    [Theory]
    [InlineData(1500, "mm", "1500")]
    [InlineData(1500, "cm", "150")]
    [InlineData(1505, "cm", "150,5")]
    [InlineData(1500, "m", "1,5")]
    [InlineData(1234.5, "mm", "1234,5")]
    [InlineData(12345, "m", "12,345")]
    public void Format_ConvertsFromMillimetres(decimal mm, string code, string expected)
        => Assert.Equal(expected, KarakusAluminyum.Web.Services.LengthUnits.Format(mm, code));

    [Theory]
    [InlineData(null, "cm")]
    [InlineData("", "cm")]
    [InlineData("inch", "cm")]
    [InlineData("<script>", "cm")]
    [InlineData("mm", "mm")]
    [InlineData("m", "m")]
    public void Normalize_FallsBackToCentimetres(string? code, string expected)
        => Assert.Equal(expected, KarakusAluminyum.Web.Services.LengthUnits.Normalize(code));
}

[Collection("postgres")]
public sealed class LengthUnitPageTests(PostgresFixture postgres)
{
    private async Task<(AppFactory App, HttpClient Client, int DesignId)> SetupAsync()
    {
        var app = postgres.Shared;
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        var id = await app.WithDbAsync(async db =>
        {
            var design = new WindowDesign { DrawingList = new DrawingList { Name = "Birim" }, Name = "Birim " + Guid.NewGuid().ToString("N")[..8], WidthMm = 1500, HeightMm = 1400, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.DrawingListId;
        });
        return (app, client, id);
    }

    private static async Task<string> GetWithUnitAsync(HttpClient client, string url, string unit)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"length_unit={unit}");
        return WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DefaultUnit_IsCentimetres()
    {
        var (_, client, id) = await SetupAsync();
        Assert.Contains("150 × 140 cm", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{id}")));
    }

    [Theory]
    [InlineData("mm", "1500 × 1400 mm")]
    [InlineData("m", "1,5 × 1,4 m")]
    [InlineData("cm", "150 × 140 cm")]
    public async Task Cookie_ChangesDisplayedUnit(string unit, string expected)
    {
        var (_, client, id) = await SetupAsync();
        Assert.Contains(expected, await GetWithUnitAsync(client, $"/yonetim/cizim/detay/{id}", unit));
    }

    [Fact]
    public async Task StoredValues_StayInMillimetres()
    {
        var (app, _, id) = await SetupAsync();
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == id).Select(d => d.WidthMm).SingleAsync()));
    }

    [Fact]
    public async Task AdminLayout_LoadsUnitSelector()
    {
        var (_, client, _) = await SetupAsync();
        var html = await client.GetStringAsync("/yonetim/cizim");
        Assert.Contains("id=\"unit-global\"", html);
        Assert.Contains("/js/units.js", html);
    }
}

[Collection("postgres")]
public sealed class DrawingGuardTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GlassList_CannotBeAddedToNonDraftQuote()
    {
        var app = postgres.Shared;
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        var token = await client.GetTokenAsync("/yonetim/camlar/ekle");
        var (listId, quoteId) = await app.WithDbAsync(async db =>
        {
            var type = new GlassType { Name = "Koruma " + Guid.NewGuid().ToString("N")[..6], PricePerM2 = 1000 };
            var customer = new Customer { Name = "Koruma " + Guid.NewGuid().ToString("N")[..6] };
            var list = new GlassList { Name = "Koruma listesi", Items = { new GlassListItem { GlassType = type, WidthMm = 1000, HeightMm = 1000 } } };
            db.GlassLists.Add(list);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var quote = new Quote { QuoteNumber = "K-" + Guid.NewGuid().ToString("N")[..10], Title = "Gönderilmiş", CustomerId = customer.Id, Status = QuoteStatus.Sent };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return (list.Id, quote.Id);
        });

        await client.PostFormAsync($"/yonetim/camlar/teklife-ekle/{listId}", token, ("quoteId", quoteId.ToString()));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));
    }

    // Gönderilen alanlar diğer türe göre de geçerlidir: kayıt reddedildiği için değil, tür korunduğu için geçer.
    [Fact]
    public async Task EditingDesign_CannotChangeItsCategory()
    {
        var app = postgres.Shared;
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        var token = await client.GetTokenAsync(DrawingLists.Url);
        var id = await app.WithDbAsync(async db =>
        {
            var design = new WindowDesign { DrawingList = new DrawingList { Name = "Tür" }, Name = "Tür " + Guid.NewGuid().ToString("N")[..6], WidthMm = 1000, HeightMm = 1000, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });

        var name = "Değişti " + Guid.NewGuid().ToString("N")[..6];
        await client.PostFormAsync($"/yonetim/cizim/poz/{id}", token, ("Name", name), ("Category", "shutter"), ("ColorHex", "#3a4046"), ("WidthMm", "1000"), ("HeightMm", "1000"),
            ("Shape", "rect"), ("Quantity", "1"), ("LayoutJson", "{\"opening\":\"fixed\"}"), ("ParamsJson", "{\"drive\":\"manual\"}"));
        var row = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Id == id));
        Assert.Equal(name, row.Name);
        Assert.Equal(ProductCategories.Aluminum, row.Category);
    }
}

[Collection("postgres")]
public sealed class ProductionDetailsTests(PostgresFixture postgres)
{
    private const string Layout = DrawingLists.Layout;

    private static string Name() => "Üretim " + Guid.NewGuid().ToString("N")[..8];

    private async Task<(AppFactory App, HttpClient Client, string Token)> OpenAsync(string database, bool enabled = true)
    {
        var app = postgres.CreateApp(database);
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        await SetAsync(app, enabled);
        return (app, client, await client.GetTokenAsync(DrawingLists.Url));
    }

    private static Task SetAsync(AppFactory app, bool enabled) => app.WithDbAsync(async db =>
    {
        var record = await db.SiteSettingRecords.SingleAsync(s => s.Key == "ProductionDetailsEnabled");
        record.Value = enabled ? "true" : "false";
        return await db.SaveChangesAsync();
    });

    private static Task<int> SystemAsync(AppFactory app, string category = "aluminum") => app.WithDbAsync(async db =>
    {
        var system = new ProfileSystem { Name = Name(), Category = category, FramePricePerM = 100, SashPricePerM = 100, MullionPricePerM = 100, BeadPricePerM = 10, HardwareTiltPrice = 500, MarginPercent = 10 };
        db.ProfileSystems.Add(system);
        await db.SaveChangesAsync();
        return system.Id;
    });

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string name, int? system)
        => DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name, system: system)));

    private static Task<HttpResponseMessage> SaveDrawingAsync(HttpClient client, string token, int id, string name, int? system)
        => client.PostFormAsync($"/yonetim/cizim/poz/{id}", token, ("Name", name), ("Category", "aluminum"), ("ProfileSystemId", system?.ToString() ?? ""), ("ColorHex", "#3a4046"),
            ("WidthMm", "1000"), ("HeightMm", "1000"), ("Shape", "rect"), ("ShapeParamMm", "0"), ("Quantity", "2"), ("LayoutJson", Layout));

    private static Task<int> IdAsync(AppFactory app, string name)
        => app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());

    [Fact]
    public async Task Setting_IsOffByDefault_AndOpensAndClosesEveryProductionPage()
    {
        await using var app = postgres.CreateApp("prod_toggle");
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        Assert.Equal("false", await app.WithDbAsync(db => db.SiteSettingRecords.Where(s => s.Key == "ProductionDetailsEnabled").Select(s => s.Value).SingleAsync()));
        var token = await client.GetTokenAsync("/yonetim/ayarlar");
        var system = await app.WithDbAsync(async db =>
        {
            var row = new ProfileSystem { Name = Name() }; // fiyatsız: panoda eksik olarak listelenir
            db.ProfileSystems.Add(row);
            await db.SaveChangesAsync();
            return row.Id;
        });
        var name = Name();
        await app.WithDbAsync(async db =>
        {
            db.WindowDesigns.Add(new WindowDesign { DrawingList = new DrawingList { Name = name }, Name = name, ProfileSystemId = system, WidthMm = 1000, HeightMm = 1000, LayoutJson = Layout });
            return await db.SaveChangesAsync();
        });
        var id = await IdAsync(app, name);
        var listId = await DrawingLists.ListIdAsync(app, name);
        string[] pages = [$"/yonetim/cizim/imalat/{id}", "/yonetim/cizim/sistemler", $"/yonetim/cizim/optimizasyon?ids={id}"];

        foreach (var page in pages) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(page)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync($"/yonetim/camlar/cizimden/{id}", token)).StatusCode);
        var list = await client.GetStringAsync($"/yonetim/cizim/detay/{listId}");
        Assert.DoesNotContain("/yonetim/cizim/imalat", list);
        Assert.DoesNotContain("/yonetim/cizim/optimizasyon", list);
        Assert.DoesNotContain("/yonetim/cizim/sistemler", await client.GetStringAsync("/yonetim/cizim"));
        Assert.DoesNotContain("drawing-production.js", await client.GetStringAsync($"/yonetim/cizim/poz/{id}"));
        Assert.DoesNotContain("Doğrama sistemi fiyatları", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim")));

        await client.PostFormAsync("/yonetim/ayarlar/kaydet", token, ("values[ProductionDetailsEnabled]", "true"));
        foreach (var page in pages) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode);
        list = await client.GetStringAsync($"/yonetim/cizim/detay/{listId}");
        Assert.Contains($"/yonetim/cizim/imalat/{id}", list);
        Assert.Contains($"/yonetim/cizim/optimizasyon?ids={id}", list);
        Assert.Contains("/yonetim/cizim/sistemler", await client.GetStringAsync("/yonetim/cizim"));
        var designer = await client.GetStringAsync($"/yonetim/cizim/poz/{id}");
        Assert.Contains("drawing-production.js", designer);
        Assert.Contains("name=\"ProfileSystemId\"", designer);
        Assert.Contains("Doğrama sistemi fiyatları", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim")));

        await client.PostFormAsync("/yonetim/ayarlar/kaydet", token, ("values[SiteName]", "Karakuş Alüminyum")); // onay kutusu gönderilmedi
        foreach (var page in pages) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(page)).StatusCode);
        Assert.Equal(system, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.ProfileSystemId).SingleAsync())); // kapatmak veri silmez
    }

    [Fact]
    public async Task ManufacturingSheet_ShowsCutsCostAndSuggestedPrice()
    {
        var (app, client, token) = await OpenAsync("prod_sheet");
        await using var _ = app;
        var system = await SystemAsync(app);
        var name = Name();

        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token, name, system)).StatusCode);
        var id = await IdAsync(app, name);
        var sheet = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{id}"));
        foreach (var text in new[] { "Kasa yan", "Kanat yatay", "Profil kesim optimizasyonu", "Birim satış" }) Assert.Contains(text, sheet);

        var design = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().Include(d => d.ProfileSystem).SingleAsync(d => d.Id == id));
        var price = DrawingCalculation.Calculate(design, design.ProfileSystem!)!.UnitPrice;
        Assert.True(price > 0);
        await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = Name() };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            db.Quotes.Add(new Quote { QuoteNumber = "P-" + Guid.NewGuid().ToString("N")[..10], Title = "Taslak", CustomerId = customer.Id });
            return await db.SaveChangesAsync();
        });
        Assert.Contains($"value=\"{price.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"", await client.GetStringAsync($"/yonetim/cizim/detay/{await DrawingLists.ListIdAsync(app, name)}"));
    }

    [Fact]
    public async Task DesignWithoutSystem_IsSaved_ButSheetAsksForSystem()
    {
        var (app, client, token) = await OpenAsync("prod_nosystem");
        await using var _ = app;
        var name = Name();
        await SaveAsync(client, token, name, null);
        var id = await IdAsync(app, name);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveDrawingAsync(client, token, id, name, null)).StatusCode);
        Assert.Null(await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.ProfileSystemId).SingleAsync()));

        var response = await client.GetAsync($"/yonetim/cizim/imalat/{id}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/yonetim/cizim/poz/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/yonetim/cizim/optimizasyon", await client.GetStringAsync($"/yonetim/cizim/detay/{await DrawingLists.ListIdAsync(app, name)}"));
    }

    [Fact]
    public async Task SystemRules_ApplyOnlyWhileEnabled_AndSelectionSurvivesOffEdits()
    {
        var (app, client, token) = await OpenAsync("prod_rules");
        await using var _ = app;
        var aluminum = await SystemAsync(app);
        var shutter = await SystemAsync(app, "shutter");

        var wrong = Name();
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, wrong, shutter)).StatusCode); // başka türün sistemi
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == wrong)));

        var name = Name();
        await SaveAsync(client, token, name, aluminum);
        var id = await IdAsync(app, name);
        var listId = await DrawingLists.ListIdAsync(app, name);

        await SetAsync(app, false);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveDrawingAsync(client, token, id, name, null)).StatusCode);
        Assert.Equal(aluminum, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.ProfileSystemId).SingleAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await DrawingLists.PostToAsync(client, token, $"/yonetim/cizim/duzenle/{listId}", name, DrawingLists.Rows(DrawingLists.Row(name, id: id)))).StatusCode);
        Assert.Equal(aluminum, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.ProfileSystemId).SingleAsync()));
    }

    [Fact]
    public async Task Calculate_RequiresEnabledSetting_AndAntiforgery()
    {
        var (app, client, token) = await OpenAsync("prod_calc");
        await using var _ = app;
        var system = await SystemAsync(app);
        var body = JsonSerializer.Serialize(new { category = "aluminum", profileSystemId = system, widthMm = 1000, heightMm = 1000, shape = "rect", shapeParamMm = 0, quantity = 1, layoutJson = Layout, name = "x" });
        HttpRequestMessage Request()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/yonetim/cizim/hesapla") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("RequestVerificationToken", token);
            return request;
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/yonetim/cizim/hesapla", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        var response = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("unitPrice").GetDecimal() > 0);
        Assert.NotEqual(0, json.RootElement.GetProperty("cuts").GetArrayLength());

        await SetAsync(app, false);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request())).StatusCode);
    }

    [Fact]
    public async Task Optimization_CombinesSelected_AndSkipsDesignsWithoutSystem()
    {
        var (app, client, token) = await OpenAsync("prod_opt");
        await using var _ = app;
        var system = await SystemAsync(app);
        string[] names = [Name(), Name()];
        foreach (var name in names) await SaveAsync(client, token, name, system);
        var without = Name();
        await SaveAsync(client, token, without, null);
        await SaveDrawingAsync(client, token, await IdAsync(app, without), without, null);
        var ids = await app.WithDbAsync(db => db.WindowDesigns.Where(d => names.Contains(d.Name) || d.Name == without).Select(d => d.Id).ToListAsync());

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim/optimizasyon?" + string.Join("&", ids.Select(i => $"ids={i}"))));
        Assert.Contains(names[0], html);
        Assert.Contains(names[1], html);
        Assert.Contains("verim", html);
        Assert.DoesNotContain(without, html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/yonetim/cizim/optimizasyon")).StatusCode);
    }

    [Fact]
    public async Task GlassPlates_FollowTheSetting_AndValuesSurviveOffEdits()
    {
        var (app, client, token) = await OpenAsync("prod_glass");
        await using var _ = app;
        var (typeId, listId) = await app.WithDbAsync(async db =>
        {
            var type = new GlassType { Name = "Plakalı", SheetWidthMm = 3000, SheetHeightMm = 2000, CutGapMm = 3 };
            var list = new GlassList { Name = "Liste", Items = { new GlassListItem { GlassType = type, WidthMm = 500, HeightMm = 500, Quantity = 2 }, new GlassListItem { GlassType = type, WidthMm = 600, HeightMm = 500 } } };
            db.GlassLists.Add(list);
            await db.SaveChangesAsync();
            return (type.Id, list.Id);
        });

        // Regresyon: aynı cam tipindeki satırlar tek planda birleşmeli.
        var html = await client.GetStringAsync($"/yonetim/camlar/detay/{listId}");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"gl-plate\""));
        Assert.Contains("name=\"SheetWidthMm\"", await client.GetStringAsync($"/yonetim/camlar/tipler/duzenle/{typeId}"));
        await client.PostFormAsync($"/yonetim/camlar/tipler/duzenle/{typeId}", token, ("Name", "Plakalı"), ("SheetWidthMm", "2800"), ("SheetHeightMm", "2000"), ("CutGapMm", "3"), ("IsActive", "true"));
        Assert.Equal(2800m, await app.WithDbAsync(db => db.GlassTypes.Where(g => g.Id == typeId).Select(g => g.SheetWidthMm).SingleAsync()));

        await SetAsync(app, false);
        Assert.DoesNotContain("gl-plate", await client.GetStringAsync($"/yonetim/camlar/detay/{listId}"));
        Assert.DoesNotContain("SheetWidthMm", await client.GetStringAsync($"/yonetim/camlar/tipler/duzenle/{typeId}"));
        await client.PostFormAsync($"/yonetim/camlar/tipler/duzenle/{typeId}", token, ("Name", "Yeni ad"), ("PricePerM2", "750"), ("IsActive", "true"));
        var type = await app.WithDbAsync(db => db.GlassTypes.AsNoTracking().SingleAsync(g => g.Id == typeId));
        Assert.Equal(("Yeni ad", 750m), (type.Name, type.PricePerM2));
        Assert.Equal((2800m, 2000m, 3m), (type.SheetWidthMm, type.SheetHeightMm, type.CutGapMm));
    }

    [Fact]
    public async Task DesignGlass_IsTransferredAsSizes_OnlyWhileEnabled()
    {
        var (app, client, token) = await OpenAsync("prod_transfer");
        await using var _ = app;
        var name = Name();
        var designId = await app.WithDbAsync(async db =>
        {
            var system = new ProfileSystem { Name = name, FrameWidthMm = 70, GlassClearanceMm = 5, FramePricePerM = 100 };
            db.GlassTypes.Add(new GlassType { Name = "Aktarım camı" });
            var design = new WindowDesign { DrawingList = new DrawingList { Name = name }, Name = name, ProfileSystem = system, WidthMm = 1000, HeightMm = 1000, Quantity = 3, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });

        var sheet = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{designId}"));
        Assert.Contains("camlar hariç", sheet);
        Assert.Contains("3 adet cam var", sheet);

        await client.PostFormAsync($"/yonetim/camlar/cizimden/{designId}", token);
        var item = await app.WithDbAsync(db => db.GlassListItems.AsNoTracking().SingleAsync(i => i.GlassList!.Name == name + " camları"));
        Assert.Equal((850m, 850m, 3), (item.WidthMm, item.HeightMm, item.Quantity));
    }
}

[Collection("postgres")]
public sealed class DrawingListTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync(DrawingLists.Url));
    }

    private static string Tag() => "T" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task AllRows_AreSaved_WithSharedCustomerColorOrderAndAutomaticNames()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var tag = Tag();
        var customer = await app.WithDbAsync(async db =>
        {
            var row = new Customer { Name = "Liste " + tag };
            db.Customers.Add(row);
            await db.SaveChangesAsync();
            return row.Id;
        });
        var split = "{\"split\":\"v\",\"sizes\":[900,900],\"children\":[{},{}]}";
        var rows = DrawingLists.Rows(DrawingLists.Row(tag + " salon", 1800, 1400, 2, split), DrawingLists.Row("Poz 1"),
            DrawingLists.Product(tag + " panjur", "shutter", "{\"drive\":\"manual\"}"),
            DrawingLists.Product("", "balcony", "{\"openType\":\"folding\",\"panelCount\":4}", 3000, 2200));

        Assert.Equal(HttpStatusCode.Redirect, (await DrawingLists.PostAsync(client, token, tag, rows, ("CustomerId", customer.ToString()))).StatusCode);

        var list = await app.WithDbAsync(db => db.DrawingLists.AsNoTracking().Include(l => l.Items).SingleAsync(l => l.Name == tag));
        Assert.Equal(customer, list.CustomerId);
        var saved = list.Items.OrderBy(d => d.SortOrder).ToList();
        Assert.Equal(new[] { 0, 1, 2, 3 }, saved.Select(d => d.SortOrder));
        Assert.All(saved, d => Assert.Equal("#3a4046", d.ColorHex));
        Assert.All(saved, d => Assert.Equal(customer, d.CustomerId));
        Assert.All(saved, d => Assert.Null(d.ProfileSystemId));
        Assert.Equal((1800, 2), (saved[0].WidthMm, saved[0].Quantity));
        Assert.Contains("\"split\":\"v\"", saved[0].LayoutJson);
        Assert.Contains("manual", saved[2].ParamsJson);
        Assert.Equal("Poz 2", saved[3].Name); // "Poz 1" kullanıldığı için atlanır
    }

    [Fact]
    public async Task OneBadRow_SavesNothing_NamesTheRow_AndKeepsTheInput()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var list = Tag();
        var good = Tag();
        var rows = DrawingLists.Rows(DrawingLists.Row(good), DrawingLists.Row(Tag(), width: 50), DrawingLists.Row(Tag(), category: "masa"),
            DrawingLists.Row(Tag(), layout: "{\"split\":\"v\",\"sizes\":[100,100],\"children\":[{},{}]}"), DrawingLists.Row(Tag(), quantity: 0));

        var response = await DrawingLists.PostAsync(client, token, list, rows);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach (var row in new[] { "2. satır", "3. satır", "4. satır", "5. satır" }) Assert.Contains(row, html);
        Assert.DoesNotContain("1. satır", html);
        Assert.Contains(good, html);
        Assert.False(await app.WithDbAsync(db => db.DrawingLists.AnyAsync(l => l.Name == list)));
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == good)));
    }

    [Fact]
    public async Task BrokenEmptyOrTooManyRows_AndUnknownCustomer_AreRejected()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var many = DrawingLists.Rows(Enumerable.Range(0, 201).Select(_ => DrawingLists.Row(null)).ToArray());
        foreach (var rows in new[] { "{bozuk", "[]", many })
        {
            var name = Tag();
            Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostAsync(client, token, name, rows)).StatusCode);
            Assert.False(await app.WithDbAsync(db => db.DrawingLists.AnyAsync(l => l.Name == name)));
        }

        var unknown = Tag();
        Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostAsync(client, token, unknown, DrawingLists.Rows(DrawingLists.Row(null)), ("CustomerId", "999999"))).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.DrawingLists.AnyAsync(l => l.Name == unknown)));
    }

    [Fact]
    public async Task EditingList_UpdatesKeepsAddsAndRemovesRows()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var list = Tag();
        string[] names = [Tag(), Tag(), Tag()];
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(names.Select(n => DrawingLists.Row(n)).ToArray()));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var keep = await DrawingLists.DesignIdAsync(app, names[0]);
        var drop = await DrawingLists.DesignIdAsync(app, names[1]);
        var moved = await DrawingLists.DesignIdAsync(app, names[2]);

        // Editör satırların kayıtlı kimliklerini ve ölçülerini sayfaya koyar.
        var editor = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/duzenle/{listId}"));
        Assert.Contains($"\"id\":{keep}", editor);

        var added = Tag();
        var response = await DrawingLists.PostToAsync(client, token, $"/yonetim/cizim/duzenle/{listId}", list + " yeni", DrawingLists.Rows(
            DrawingLists.Row(names[2], id: moved), DrawingLists.Row(names[0], 1200, id: keep), DrawingLists.Row(added)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var rows = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).ToListAsync());
        Assert.Equal(new[] { names[2], names[0], added }, rows.Select(d => d.Name));
        Assert.Equal(moved, rows[0].Id);
        Assert.Equal((keep, 1200), (rows[1].Id, rows[1].WidthMm));
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Id == drop)));
        Assert.Equal(list + " yeni", await app.WithDbAsync(db => db.DrawingLists.Where(l => l.Id == listId).Select(l => l.Name).SingleAsync()));
    }

    [Fact]
    public async Task EditingList_KeepsCategoryShapeAndEditorDetailsOfSavedRows()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        var (listId, id) = await app.WithDbAsync(async db =>
        {
            var design = new WindowDesign { DrawingList = new DrawingList { Name = name }, Name = name, WidthMm = 1500, HeightMm = 1400, Shape = "arch", ShapeParamMm = 400, ColorHex = "#111111", LayoutJson = "{\"opening\":\"tiltLeft\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return (design.DrawingListId, design.Id);
        });

        // Kemer ölçüsü listeden gelmez: satır şekli korunur, kemer kuralı yine uygulanır.
        var rows = JsonSerializer.Serialize(new[] { new { id, category = "shutter", name, widthMm = 1500, heightMm = 1400, quantity = 3, shape = "rect", layoutJson = "{\"opening\":\"tiltLeft\"}", paramsJson = "{\"drive\":\"manual\"}" } });
        Assert.Equal(HttpStatusCode.Redirect, (await DrawingLists.PostToAsync(client, token, $"/yonetim/cizim/duzenle/{listId}", name, rows)).StatusCode);
        var row = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Id == id));
        Assert.Equal((ProductCategories.Aluminum, "arch", 400, 3, "#3a4046".Length), (row.Category, row.Shape, row.ShapeParamMm, row.Quantity, row.ColorHex.Length));
        Assert.Contains("tiltLeft", row.LayoutJson);

        var narrow = JsonSerializer.Serialize(new[] { new { id, name, widthMm = 700, heightMm = 1400, quantity = 3, layoutJson = "{\"opening\":\"fixed\"}" } });
        Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostToAsync(client, token, $"/yonetim/cizim/duzenle/{listId}", name, narrow)).StatusCode);
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.WidthMm).SingleAsync()));
    }

    [Fact]
    public async Task RowOfAnotherList_IsRejected()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        string[] lists = [Tag(), Tag()];
        foreach (var list in lists) await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row(list)));
        var stolen = await DrawingLists.DesignIdAsync(app, lists[1]);

        var response = await DrawingLists.PostToAsync(client, token, $"/yonetim/cizim/duzenle/{await DrawingLists.ListIdAsync(app, lists[0])}", lists[0], DrawingLists.Rows(DrawingLists.Row("Çalındı", 1200, id: stolen)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((lists[1], 1000), await app.WithDbAsync(async db =>
        {
            var row = await db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Id == stolen);
            return (row.Name, row.WidthMm);
        }));
    }

    [Fact]
    public async Task WithProductionDetails_NewRowsGetTheFirstActiveSystem_AndItsRules()
    {
        await using var app = postgres.CreateApp("list_production");
        var (client, token) = await AdminAsync(app);
        await app.WithDbAsync(async db =>
        {
            (await db.SiteSettingRecords.SingleAsync(s => s.Key == "ProductionDetailsEnabled")).Value = "true";
            return await db.SaveChangesAsync();
        });
        var system = await app.WithDbAsync(db => db.ProfileSystems.Where(p => p.IsActive && p.Category == "aluminum").OrderBy(p => p.Name).Select(p => p.Id).FirstAsync());

        var tag = Tag();
        await DrawingLists.PostAsync(client, token, tag, DrawingLists.Rows(DrawingLists.Row(tag)));
        Assert.Equal(system, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == tag).Select(d => d.ProfileSystemId).SingleAsync()));

        await app.WithDbAsync(async db =>
        {
            (await db.ProfileSystems.SingleAsync(p => p.Id == system)).FrameWidthMm = 200;
            return await db.SaveChangesAsync();
        });
        var bad = Tag(); // şekil geçerli, kasa payından küçük açıklığı imalat kuralı reddeder
        Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostAsync(client, token, bad, DrawingLists.Rows(DrawingLists.Row(bad, 300, 300)))).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == bad)));
    }
}

[Collection("postgres")]
public sealed class BrandTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/markalar/ekle"));
    }

    private static string Tag() => "M" + Guid.NewGuid().ToString("N")[..8];

    private static Task<int> NewBrandAsync(AppFactory app, string name, string category = "aluminum", string? logo = null, bool active = true) => app.WithDbAsync(async db =>
    {
        var brand = new Brand { Name = name, Category = category, LogoPath = logo, IsActive = active };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        return brand.Id;
    });

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string url, string name, string category, string fileName, params (string, string)[] extra)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(name), "Name" }, { new StringContent(category), "Category" }, { new StringContent("true"), "IsActive" },
            { new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "logoFile", fileName }
        };
        foreach (var (key, value) in extra) form.Add(new StringContent(value), key);
        return client.PostAsync(url, form);
    }

    [Fact]
    public async Task Brand_IsSavedWithLogo_AndLogoCanBeReplacedOrRemoved()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();

        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(client, token, "/yonetim/markalar/ekle", name, "pvc", "logo.png")).StatusCode);
        var brand = await app.WithDbAsync(db => db.Brands.AsNoTracking().SingleAsync(b => b.Name == name));
        Assert.Equal(ProductCategories.Pvc, brand.Category);
        Assert.StartsWith("/uploads/images/", brand.LogoPath);
        Assert.Contains(brand.LogoPath!, await client.GetStringAsync("/yonetim/markalar"));

        await client.PostFormAsync($"/yonetim/markalar/duzenle/{brand.Id}", token, ("Name", name), ("Category", "pvc"), ("IsActive", "true"));
        Assert.Equal(brand.LogoPath, await app.WithDbAsync(db => db.Brands.Where(b => b.Id == brand.Id).Select(b => b.LogoPath).SingleAsync())); // dosya seçilmezse korunur

        await client.PostFormAsync($"/yonetim/markalar/duzenle/{brand.Id}", token, ("Name", name), ("Category", "pvc"), ("IsActive", "true"), ("removeLogo", "true"));
        Assert.Null(await app.WithDbAsync(db => db.Brands.Where(b => b.Id == brand.Id).Select(b => b.LogoPath).SingleAsync()));
    }

    [Theory]
    [InlineData("logo.svg")]
    [InlineData("logo.exe")]
    public async Task UnsafeLogoFile_IsRejected(string fileName)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, token, "/yonetim/markalar/ekle", name, "pvc", fileName)).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Brands.AnyAsync(b => b.Name == name)));
    }

    [Fact]
    public async Task UsedBrand_CannotBeDeletedOrMoved_UnusedCan()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        var used = await NewBrandAsync(app, name);
        var free = await NewBrandAsync(app, Tag());
        var row = Tag();
        await DrawingLists.PostAsync(client, token, row, DrawingLists.Rows(DrawingLists.Row(row, brand: used)));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/markalar/sil/{used}", token)).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Brands.AnyAsync(b => b.Id == used)));
        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync($"/yonetim/markalar/duzenle/{used}", token, ("Name", name), ("Category", "pvc"))).StatusCode);
        Assert.Equal(ProductCategories.Aluminum, await app.WithDbAsync(db => db.Brands.Where(b => b.Id == used).Select(b => b.Category).SingleAsync()));

        await client.PostFormAsync($"/yonetim/markalar/sil/{free}", token);
        Assert.False(await app.WithDbAsync(db => db.Brands.AnyAsync(b => b.Id == free)));
    }

    [Fact]
    public async Task BrandOfAnotherCategory_IsRejected_InListAndEditor()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var pvc = await NewBrandAsync(app, Tag(), "pvc");
        var aluminum = await NewBrandAsync(app, Tag());
        var bad = Tag();
        Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostAsync(client, token, bad, DrawingLists.Rows(DrawingLists.Row(bad, brand: pvc)))).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.DrawingLists.AnyAsync(l => l.Name == bad)));
        var missing = Tag();
        Assert.Equal(HttpStatusCode.OK, (await DrawingLists.PostAsync(client, token, missing, DrawingLists.Rows(DrawingLists.Row(missing, brand: 999999)))).StatusCode);

        var name = Tag();
        await DrawingLists.PostAsync(client, token, name, DrawingLists.Rows(DrawingLists.Row(name, brand: aluminum)));
        var id = await DrawingLists.DesignIdAsync(app, name);
        Task<HttpResponseMessage> Post(int brand) => client.PostFormAsync($"/yonetim/cizim/poz/{id}", token, ("Name", name), ("ColorHex", "#3a4046"), ("WidthMm", "1000"), ("HeightMm", "1000"), ("Shape", "rect"), ("Quantity", "1"), ("LayoutJson", DrawingLists.Layout), ("BrandId", brand.ToString()));
        Assert.Equal(HttpStatusCode.OK, (await Post(pvc)).StatusCode);
        Assert.Equal(aluminum, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.BrandId).SingleAsync()));
    }

    [Fact]
    public async Task EveryDrawing_ShowsItsBrand_AsLogoOrName_OnAllPages()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        const string logo = "/uploads/images/test-logo.png";
        var withLogo = await NewBrandAsync(app, Tag(), logo: logo);
        var textName = Tag();
        var withoutLogo = await NewBrandAsync(app, textName, "pvc");
        var plain = Tag();
        var list = Tag();
        var first = Tag();
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row(first, brand: withLogo), DrawingLists.Row(Tag(), category: "pvc", brand: withoutLogo), DrawingLists.Row(plain)));
        var listId = await DrawingLists.ListIdAsync(app, list);

        var page = await client.GetStringAsync($"/yonetim/cizim/detay/{listId}");
        Assert.Contains($"<img src=\"{logo}\"", page);
        Assert.Contains(textName, page);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, "class=\"wd-brand\"").Count); // markasız çizimde boş alan yok

        var editor = await client.GetStringAsync($"/yonetim/cizim/poz/{await DrawingLists.DesignIdAsync(app, first)}");
        Assert.Contains("id=\"wd-brand-mark\"", editor);
        Assert.Contains($"data-logo=\"{logo}\"", editor);
        Assert.DoesNotContain(textName, editor); // başka türün markası seçilemez

        var design = await DrawingLists.DesignIdAsync(app, first);
        var attachments = await KarakusAluminyum.Web.Services.Documents.DocumentViews.LoadAttachmentsAsync(await Task.FromResult(app.Services.CreateScope().ServiceProvider.GetRequiredService<KarakusAluminyum.Web.Data.AppDbContext>()), new[] { design });
        Assert.Equal(logo, Assert.Single(attachments.Items).Design.Brand!.LogoPath);
    }

    [Fact]
    public async Task InactiveBrand_IsHiddenForNewRows_ButKeptOnItsDrawing()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        var brand = await NewBrandAsync(app, name, active: false);
        Assert.Contains($"\"active\":false", await client.GetStringAsync(DrawingLists.Url));
        var row = Tag();
        await DrawingLists.PostAsync(client, token, row, DrawingLists.Rows(DrawingLists.Row(row, brand: brand)));
        Assert.Contains(name, await client.GetStringAsync($"/yonetim/cizim/poz/{await DrawingLists.DesignIdAsync(app, row)}"));
        var other = Tag();
        await DrawingLists.PostAsync(client, token, other, DrawingLists.Rows(DrawingLists.Row(other)));
        Assert.DoesNotContain(name, await client.GetStringAsync($"/yonetim/cizim/poz/{await DrawingLists.DesignIdAsync(app, other)}"));
    }
}
