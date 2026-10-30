using System.Net;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;
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
    public void Mullion_LengthUsesFit_AndChildSizesMustMatchOpening()
    {
        var c = Calc(Split("v", new[] { 390m, 390m }, Leaf(), Leaf())); // 860 = 390 + 80 + 390
        Assert.True(c.IsValid);
        Assert.Equal(864m, Cut(c, "Dikey kayıt").LengthMm); // 860 + 2*2
        Assert.Equal(2, Assert.Single(c.Glass).Count);

        var wrong = Calc(Split("v", new[] { 400m, 400m }, Leaf(), Leaf()));
        Assert.Contains(wrong.Errors, e => e.Contains("eşit değil"));
    }

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

[Collection("postgres")]
public sealed class DrawingPageTests(PostgresFixture postgres)
{
    private const string Layout = "{\"id\":\"root\",\"opening\":\"tiltRight\",\"fill\":\"glass\"}";

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/cizim/ekle?tur=aluminum"));
    }

    private static async Task<(int SystemId, int GlassId)> PricedSystemAsync(AppFactory app) => await app.WithDbAsync(async db =>
    {
        var system = new ProfileSystem { Name = "Fiyatlı " + Guid.NewGuid().ToString("N")[..6], FramePricePerM = 100, SashPricePerM = 100, MullionPricePerM = 100, BeadPricePerM = 10, HardwareTiltPrice = 500, MarginPercent = 10 };
        db.ProfileSystems.Add(system);
        await db.SaveChangesAsync();
        return (system.Id, 0);
    });

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, string name, int systemId, int glassId, string layout = Layout, int width = 1200, int height = 1400)
        => client.PostFormAsync(url, token, ("Name", name), ("Category", "aluminum"), ("ProfileSystemId", systemId.ToString()), ("ColorHex", "#3a4046"),
            ("WidthMm", width.ToString()), ("HeightMm", height.ToString()), ("Shape", "rect"), ("ShapeParamMm", "0"), ("Quantity", "2"), ("LayoutJson", layout));

    private static string Name() => "Çizim " + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Seed_CreatesPricelessExampleOnce()
    {
        var app = postgres.Shared;
        app.NewClient();
        var system = await app.WithDbAsync(db => db.ProfileSystems.FirstAsync(s => s.Name.StartsWith("Örnek")));
        Assert.Equal(0m, system.FramePricePerM);
        Assert.Equal(0m, system.HardwareOpenPrice);
    }

    [Fact]
    public async Task Designer_PageRenders_WithExistingSystems()
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        var html = await client.GetStringAsync("/yonetim/cizim/ekle?tur=aluminum");
        Assert.Contains("id=\"wd-canvas\"", html);
        Assert.Contains("window-designer.js", html);
    }

    [Fact]
    public async Task Save_ThenManufacturingSheetShowsCutsAndOptimization()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);
        var name = Name();

        var saved = await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var id = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());

        var sheet = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{id}"));
        Assert.Contains("Kasa yan", sheet);
        Assert.Contains("Kanat yatay", sheet);
        Assert.Contains("Profil kesim optimizasyonu", sheet);
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim")));

        var edited = await SaveAsync(client, token, $"/yonetim/cizim/duzenle/{id}", name, system, glass, width: 1500);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.WidthMm).SingleAsync()));
    }

    [Fact]
    public async Task Save_RejectsLayoutErrorsAndBrokenJson_WithoutSaving()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);

        foreach (var layout in new[] { "{bozuk", "{\"split\":\"v\",\"sizes\":[1,1],\"children\":[{},{}]}" })
        {
            var name = Name();
            var response = await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass, layout);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name)));
        }
    }

    [Fact]
    public async Task Calculate_ReturnsResults_AndRequiresAntiforgery()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);
        var body = JsonSerializer.Serialize(new { category = "aluminum", profileSystemId = system, widthMm = 1000, heightMm = 1000, shape = "rect", shapeParamMm = 0, quantity = 1, layoutJson = Layout, name = "x" });

        var withoutToken = await client.PostAsync("/yonetim/cizim/hesapla", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/yonetim/cizim/hesapla") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("errors").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("unitPrice").GetDecimal() > 0);
        Assert.NotEqual(0, json.RootElement.GetProperty("cuts").GetArrayLength());
    }

    [Fact]
    public async Task Optimization_CombinesSelectedDesigns()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);
        var names = new[] { Name(), Name() };
        foreach (var name in names) await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass);
        var ids = await app.WithDbAsync(db => db.WindowDesigns.Where(d => names.Contains(d.Name)).Select(d => d.Id).ToListAsync());

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/optimizasyon?ids={ids[0]}&ids={ids[1]}"));
        Assert.Contains(names[0], html);
        Assert.Contains(names[1], html);
        Assert.Contains("verim", html);

        var empty = await client.GetAsync("/yonetim/cizim/optimizasyon");
        Assert.Equal(HttpStatusCode.Redirect, empty.StatusCode);
    }

    [Fact]
    public async Task AddToQuote_AddsLineAndRecalculatesDraft_ButNotOtherStatuses()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass);
        var designId = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());

        async Task<int> NewQuote(QuoteStatus status) => await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = "Çizim müşteri " + Guid.NewGuid().ToString("N")[..6] };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var quote = new Quote { QuoteNumber = "C-" + Guid.NewGuid().ToString("N")[..10], Title = "Test", CustomerId = customer.Id, Status = status, TaxRate = 20 };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });

        var draft = await NewQuote(QuoteStatus.Draft);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", draft.ToString()));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == draft));
        var line = Assert.Single(quote.Items);
        Assert.Contains(name, line.Description);
        Assert.Equal(2m, line.Quantity);
        Assert.True(line.UnitPrice > 0);
        Assert.Equal(line.LineTotal, quote.Subtotal);
        Assert.True(quote.GrandTotal > quote.Subtotal);

        var sent = await NewQuote(QuoteStatus.Sent);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", sent.ToString()));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == sent)));
    }

    [Fact]
    public async Task AddToQuote_RejectsZeroPrice()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var (system, glass) = await app.WithDbAsync(async db =>
        {
            var s = new ProfileSystem { Name = "Sıfır " + Guid.NewGuid().ToString("N")[..6] };
            db.ProfileSystems.Add(s);
            await db.SaveChangesAsync();
            return (s.Id, 0);
        });
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass);
        var designId = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());
        var quoteId = await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = "Sıfır müşteri " + Guid.NewGuid().ToString("N")[..6] };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var quote = new Quote { QuoteNumber = "Z-" + Guid.NewGuid().ToString("N")[..10], Title = "Test", CustomerId = customer.Id };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });

        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", quoteId.ToString()));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));
    }

    [Fact]
    public async Task CopyAndDelete_Work()
    {
        var app = postgres.Shared;
        var (system, glass) = await PricedSystemAsync(app);
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name, system, glass);
        var id = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());

        await client.PostFormAsync($"/yonetim/cizim/kopyala/{id}", token);
        Assert.True(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name + " (kopya)")));

        await client.PostFormAsync($"/yonetim/cizim/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Id == id)));
    }

    [Fact]
    public async Task Systems_CrudWorks_AndUsedSystemCannotBeDeleted()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Sistem " + Guid.NewGuid().ToString("N")[..8];

        var created = await client.PostFormAsync("/yonetim/cizim/sistemler/ekle", token, ("Name", name), ("Category", "pvc"), ("FrameWidthMm", "60"), ("SashWidthMm", "70"), ("MullionWidthMm", "75"),
            ("SashOverlapMm", "8"), ("GlassClearanceMm", "4"), ("MullionFitMm", "-2"), ("WeldAllowanceMm", "3"), ("BarLengthMm", "6500"), ("KerfMm", "5"), ("IsActive", "true"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Name == name).Select(s => s.Id).SingleAsync());
        Assert.Equal(-2m, await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Id == id).Select(s => s.MullionFitMm).SingleAsync()));
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim/sistemler")));

        var (usedSystem, glass) = await PricedSystemAsync(app);
        await SaveAsync(client, token, "/yonetim/cizim/ekle", Name(), usedSystem, glass);
        // Koruma olmasa veritabanı silmeyi engellerdi ama kullanıcı 500 hatası görürdü; yönlendirme beklenir.
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/cizim/sistemler/sil/{usedSystem}", token)).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.ProfileSystems.AnyAsync(s => s.Id == usedSystem)));
        await client.PostFormAsync($"/yonetim/cizim/sistemler/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.ProfileSystems.AnyAsync(s => s.Id == id)));
    }

    [Fact]
    public async Task ExistingDatabaseWithoutDrawingTables_IsUpgradedOnStart()
    {
        await using (var first = postgres.CreateApp("drawing_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"WindowDesigns\"; DROP TABLE \"ProfileSystems\"; DROP TABLE \"GlassListItems\"; DROP TABLE \"GlassLists\"; DROP TABLE \"GlassTypes\""));
        }

        await using var second = postgres.CreateApp("drawing_upgrade", schemaMode: "existing");
        second.NewClient();
        Assert.Equal(0, await second.WithDbAsync(db => db.WindowDesigns.CountAsync()));
    }
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
        Assert.Contains("Plaka yerleşimi", html);
        Assert.Contains("gl-plate", html);
        // 3 m² + 2 m² = 5 m² x 500 = 2500; 1 m² x 1000 = 1000; toplam 3500
        var list = await app.WithDbAsync(db => db.GlassLists.AsNoTracking().Include(g => g.Items).ThenInclude(i => i.GlassType).SingleAsync(g => g.Id == id));
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(3500m, KarakusAluminyum.Web.Areas.Admin.Controllers.GlassListsController.ListTotal(list));
    }

    // Regresyon: aynı cam tipindeki satırlar tek planda birleşmeli, satır başına ayrı plaka açılmamalı.
    [Fact]
    public async Task PlateLayout_GroupsRowsOfSameGlassType()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var a = await NewTypeAsync(app);
        var b = await NewTypeAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/camlar/ekle", name, Rows(
            new { glassTypeId = a, widthMm = 500, heightMm = 500, quantity = 1 }, new { glassTypeId = a, widthMm = 600, heightMm = 500, quantity = 1 }, new { glassTypeId = b, widthMm = 500, heightMm = 500, quantity = 1 }));
        var id = await app.WithDbAsync(db => db.GlassLists.Where(g => g.Name == name).Select(g => g.Id).SingleAsync());

        var html = await client.GetStringAsync($"/yonetim/camlar/detay/{id}");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "class=\"gl-plate\"").Count);
        Assert.Contains("60\u00D750", WebUtility.HtmlDecode(html));
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
    public async Task DesignGlass_IsTransferredAsSizes_AndDesignPriceExcludesGlass()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        await NewTypeAsync(app);
        var name = "Aktarım " + Guid.NewGuid().ToString("N")[..8];
        var designId = await app.WithDbAsync(async db =>
        {
            var system = new ProfileSystem { Name = name, FrameWidthMm = 70, GlassClearanceMm = 5, FramePricePerM = 100 };
            db.ProfileSystems.Add(system);
            await db.SaveChangesAsync();
            var design = new WindowDesign { Name = name, ProfileSystemId = system.Id, WidthMm = 1000, HeightMm = 1000, Quantity = 3, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });

        var sheet = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{designId}"));
        Assert.Contains("camlar hariç", sheet);
        Assert.Contains("3 adet cam var", sheet);

        await client.PostFormAsync($"/yonetim/camlar/cizimden/{designId}", token);
        var item = await app.WithDbAsync(db => db.GlassListItems.AsNoTracking().SingleAsync(i => i.GlassList!.Name == name + " camları"));
        Assert.Equal(850m, item.WidthMm);
        Assert.Equal(850m, item.HeightMm);
        Assert.Equal(3, item.Quantity);
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
        return (client, await client.GetTokenAsync("/yonetim/cizim/ekle?tur=shutter"));
    }

    [Fact]
    public async Task Chooser_ShowsAllCategories_AndGlassListSeparately()
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim/ekle"));
        foreach (var text in new[] { "Alüminyum Doğrama", "Plastik Doğrama", "Panjur", "Korkuluk", "Cam Balkon", "Cam Listesi" }) Assert.Contains(text, html);
    }

    [Theory]
    [InlineData("aluminum", "wd-canvas")]
    [InlineData("pvc", "wd-canvas")]
    [InlineData("shutter", "pd-canvas")]
    [InlineData("railing", "pd-canvas")]
    [InlineData("balcony", "pd-canvas")]
    public async Task EachCategory_OpensItsDesigner_WithSeededSystem(string category, string canvas)
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        Assert.Contains(canvas, await client.GetStringAsync($"/yonetim/cizim/ekle?tur={category}"));
    }

    [Fact]
    public async Task Shutter_SavesPrintsAndFiltersByCategory()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Panjur " + Guid.NewGuid().ToString("N")[..8];
        var systemId = await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Category == ProductCategories.Shutter).Select(s => s.Id).FirstAsync());

        var saved = await client.PostFormAsync("/yonetim/cizim/ekle", token, ("Name", name), ("Category", "shutter"), ("ProfileSystemId", systemId.ToString()), ("ColorHex", "#3a4046"),
            ("WidthMm", "1000"), ("HeightMm", "1400"), ("Quantity", "1"), ("ParamsJson", "{\"drive\":\"manual\"}"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var design = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Name == name));
        Assert.Equal(ProductCategories.Shutter, design.Category);
        Assert.Contains("manual", design.ParamsJson);

        var sheet = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{design.Id}"));
        Assert.Contains("Lamel", sheet);
        Assert.Contains("Manuel kayış", sheet);
        Assert.Contains("\"category\":\"shutter\"", sheet);

        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=shutter")));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=railing")));
        Assert.Contains("name=\"Category\"", await client.GetStringAsync($"/yonetim/cizim/duzenle/{design.Id}"));
    }

    [Fact]
    public async Task SystemFromOtherCategory_IsRejected()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Karışık " + Guid.NewGuid().ToString("N")[..8];
        var aluminum = await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Category == ProductCategories.Aluminum).Select(s => s.Id).FirstAsync());

        var response = await client.PostFormAsync("/yonetim/cizim/ekle", token, ("Name", name), ("Category", "shutter"), ("ProfileSystemId", aluminum.ToString()), ("ColorHex", "#3a4046"),
            ("WidthMm", "1000"), ("HeightMm", "1400"), ("Quantity", "1"), ("ParamsJson", "{}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name)));
    }

    [Fact]
    public async Task SystemSettings_ArePersistedPerCategory()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Ayar " + Guid.NewGuid().ToString("N")[..8];
        await client.PostFormAsync("/yonetim/cizim/sistemler/ekle", token, ("Name", name), ("Category", "shutter"), ("BoxHeightMm", "205"), ("LamellaHeightMm", "55"), ("MotorPrice", "2500"), ("IsActive", "true"));
        var system = await app.WithDbAsync(db => db.ProfileSystems.AsNoTracking().SingleAsync(s => s.Name == name));
        Assert.Equal(ProductCategories.Shutter, system.Category);
        var settings = DrawingCalculation.ParseSettings(system.SettingsJson);
        Assert.Equal(205m, settings.BoxHeightMm);
        Assert.Equal(2500m, settings.MotorPrice);

        var html = await client.GetStringAsync($"/yonetim/cizim/sistemler/duzenle/{system.Id}");
        Assert.Contains("value=\"205\"", html);
        await client.PostFormAsync($"/yonetim/cizim/sistemler/duzenle/{system.Id}", token, ("Name", name), ("Category", "railing"), ("BoxHeightMm", "210"));
        Assert.Equal(ProductCategories.Shutter, await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Id == system.Id).Select(s => s.Category).SingleAsync())); // tür değişmez
    }

    [Fact]
    public async Task ExistingDrawingDatabase_IsUpgraded_AndPvcSystemsAreClassified()
    {
        await using (var first = postgres.CreateApp("category_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
                DROP TABLE "GlassListItems"; DROP TABLE "GlassLists";
                DELETE FROM "SiteSettingRecords" WHERE "Key" = 'ProductSystemsSeeded';
                ALTER TABLE "GlassTypes" DROP COLUMN "SheetWidthMm", DROP COLUMN "SheetHeightMm", DROP COLUMN "CutGapMm";
                ALTER TABLE "WindowDesigns" DROP COLUMN "Category", DROP COLUMN "ParamsJson";
                ALTER TABLE "WindowDesigns" ADD COLUMN "GlassTypeId" integer;
                ALTER TABLE "ProfileSystems" DROP COLUMN "Category", DROP COLUMN "SettingsJson";
                INSERT INTO "ProfileSystems" ("Name","Material","FrameWidthMm","SashWidthMm","MullionWidthMm","SashOverlapMm","GlassClearanceMm","MullionFitMm","WeldAllowanceMm","BarLengthMm","KerfMm","FramePricePerM","SashPricePerM","MullionPricePerM","BeadPricePerM","PanelPricePerM2","HardwareOpenPrice","HardwareTiltPrice","HardwareSlidePrice","HardwareDoorPrice","WastePercent","LaborPercent","MarginPercent","IsActive")
                VALUES ('Eski PVC','PVC',70,80,80,8,5,0,0,6000,4,0,0,0,0,0,0,0,0,0,0,0,0,true);
                """));
        }

        await using var second = postgres.CreateApp("category_upgrade", schemaMode: "existing");
        var (client, _) = await AdminAsync(second);
        Assert.Equal(ProductCategories.Pvc, await second.WithDbAsync(db => db.ProfileSystems.Where(s => s.Name == "Eski PVC").Select(s => s.Category).SingleAsync()));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/camlar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/cizim")).StatusCode);
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
            var system = await db.ProfileSystems.FirstAsync(s => s.Category == ProductCategories.Aluminum);
            var design = new WindowDesign { Name = "Birim " + Guid.NewGuid().ToString("N")[..8], ProfileSystemId = system.Id, WidthMm = 1500, HeightMm = 1400, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
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
        Assert.Contains("150 × 140 cm", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/imalat/{id}")));
    }

    [Theory]
    [InlineData("mm", "1500 × 1400 mm")]
    [InlineData("m", "1,5 × 1,4 m")]
    [InlineData("cm", "150 × 140 cm")]
    public async Task Cookie_ChangesDisplayedUnit(string unit, string expected)
    {
        var (_, client, id) = await SetupAsync();
        Assert.Contains(expected, await GetWithUnitAsync(client, $"/yonetim/cizim/imalat/{id}", unit));
    }

    [Fact]
    public async Task StoredValues_StayInMillimetres()
    {
        var (app, _, id) = await SetupAsync();
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.WidthMm).SingleAsync()));
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

// Önceki doğrulamada kaçan senaryoların testleri
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

    [Fact]
    public async Task EditingDesign_CannotChangeItsCategory()
    {
        var app = postgres.Shared;
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        var token = await client.GetTokenAsync("/yonetim/cizim/ekle?tur=aluminum");
        var (id, systemId) = await app.WithDbAsync(async db =>
        {
            var system = await db.ProfileSystems.FirstAsync(s => s.Category == ProductCategories.Aluminum);
            var design = new WindowDesign { Name = "Tür " + Guid.NewGuid().ToString("N")[..6], ProfileSystemId = system.Id, WidthMm = 1000, HeightMm = 1000, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return (design.Id, system.Id);
        });

        await client.PostFormAsync($"/yonetim/cizim/duzenle/{id}", token, ("Name", "Değişti"), ("Category", "shutter"), ("ProfileSystemId", systemId.ToString()), ("ColorHex", "#3a4046"), ("WidthMm", "1000"), ("HeightMm", "1000"), ("Quantity", "1"), ("Shape", "rect"), ("LayoutJson", "{\"opening\":\"fixed\"}"));
        Assert.Equal(ProductCategories.Aluminum, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.Category).SingleAsync()));
    }
}
