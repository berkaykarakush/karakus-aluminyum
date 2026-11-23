using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Seri kataloğu ve metraj: sistem kurallarıyla stok kartı bazında malzeme ihtiyacı, stoktan düşme ve geri alma.
public sealed class MaterialTakeoffTests
{
    private const string Leaf = DrawingLists.Layout; // tek açılır-devrilir kanat

    private static Product P(int id, string name, string unit) => new() { Id = id, Name = name, Unit = unit };

    [Fact]
    public void Takeoff_MultipliesEachBasisByQuantityAndFactor()
    {
        var system = new ProfileSystem { Id = 1, Name = "Seri", BarLengthMm = 6000, KerfMm = 4, WastePercent = 10 };
        var products = new Dictionary<int, Product>
        {
            [1] = P(1, "Kasa profili", "Boy"), [2] = P(2, "Donanım", "Takım"), [3] = P(3, "Silikon", "Tüp"),
            [4] = P(4, "Conta", "Metre"), [5] = P(5, "Kanat metre", "Metre")
        };
        system.MaterialsJson = MaterialTakeoff.SerializeRules(new[]
        {
            new MaterialRule { ProductId = 1, Basis = MaterialBases.ProfileBars, Key = "kasa" }, // ad büyük/küçük harf duyarsız
            new MaterialRule { ProductId = 2, Basis = MaterialBases.Hardware, Key = "Açılır-devrilir / vasistas donanımı" },
            new MaterialRule { ProductId = 3, Basis = MaterialBases.Perimeter, Factor = 0.1m },
            new MaterialRule { ProductId = 4, Basis = MaterialBases.Perimeter, Factor = 1 },
            new MaterialRule { ProductId = 4, Basis = MaterialBases.Piece, Factor = 0.5m },     // aynı kart iki kuraldan toplanır
            new MaterialRule { ProductId = 5, Basis = MaterialBases.ProfileMetres, Key = "Kanat" }
        });
        var design = new WindowDesign { Name = "Salon", WidthMm = 1000, HeightMm = 1000, Quantity = 3, LayoutJson = Leaf, ProfileSystem = system, ProfileSystemId = 1 };

        var result = MaterialTakeoff.Build(new[] { design }, products);
        decimal Q(int id) => result.Lines.Single(l => l.Product.Id == id).Quantity;

        Assert.Equal(result.Materials.Systems.Single().Plans.Single(p => p.Profile == "Kasa").Bars.Count, Q(1));
        Assert.Equal(3, Q(2));
        Assert.Equal(2, Q(3));              // 3 × 4 m × 0,1 = 1,2 tüp → 2 tüp (yukarı yuvarlanır)
        Assert.Equal(12 + 1.5m, Q(4));      // metre bölünebilir: 12 m çevre + 3 × 0,5
        var sashMm = result.Materials.Calculated.Single().Calculation.Cuts.Where(c => c.Profile == "Kanat").Sum(c => c.LengthMm * c.Count);
        Assert.Equal(decimal.Round(sashMm * 3 / 1000m * 1.1m, 3), Q(5)); // fire dahil
        Assert.Contains(result.Uncovered, x => x.Contains("\"Çıta\" profili"));
        Assert.DoesNotContain(result.Uncovered, x => x.Contains("donanımı için"));
    }

    [Fact]
    public void Takeoff_ReportsSystemsWithoutRules_AndDrawingsWithoutSystem()
    {
        var system = new ProfileSystem { Id = 7, Name = "Kuralsız" };
        var designs = new[]
        {
            new WindowDesign { Name = "A", WidthMm = 1000, HeightMm = 1000, Quantity = 1, LayoutJson = Leaf, ProfileSystem = system, ProfileSystemId = 7 },
            new WindowDesign { Name = "B", WidthMm = 1000, HeightMm = 1000, Quantity = 1, LayoutJson = Leaf }
        };
        var result = MaterialTakeoff.Build(designs, new Dictionary<int, Product>());
        Assert.Empty(result.Lines);
        Assert.Contains(result.Uncovered, x => x.StartsWith("Kuralsız: malzeme kuralı tanımlı değil"));
        Assert.Single(result.Materials.WithoutSystem);
    }

    [Fact]
    public void SampleCatalog_CoversEveryProfileAndHardwareOfTheCategory()
    {
        foreach (var category in ProductCategories.All)
        {
            var samples = SampleCatalog.For(new ProfileSystem { Category = category, BarLengthMm = 6000, FramePricePerM = 100 }).ToList();
            foreach (var profile in MaterialBases.Profiles(category))
                Assert.Contains(samples, s => s.Key == profile && s.Basis is MaterialBases.ProfileBars or MaterialBases.ProfileMetres);
            foreach (var hardware in MaterialBases.HardwareNames(category))
                Assert.Contains(samples, s => s.Key == hardware && s.Basis == MaterialBases.Hardware);
        }
        Assert.Equal(600, SampleCatalog.For(new ProfileSystem { Category = ProductCategories.Aluminum, BarLengthMm = 6000, FramePricePerM = 100 }).First().Price); // 100 ₺/m × 6 m
    }
}

[Collection("postgres")]
public sealed class MaterialOrderTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SystemRules_DriveOrderTakeoff_DeductAndRevertStock()
    {
        await using var app = postgres.CreateApp("material_takeoff");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (systemId, frame, gasket, service) = await app.WithDbAsync(async db =>
        {
            var system = new ProfileSystem { Name = "Test serisi", Category = ProductCategories.Aluminum, BarLengthMm = 6000 };
            var frame = new Product { Code = "K-100", Name = "Kasa profili", Unit = "Boy" };
            var gasket = new Product { Code = "C-1", Name = "Cam contası", Unit = "Metre" };
            var service = new Product { Name = "Nakliye", Unit = "Adet", Kind = ProductKind.Service };
            db.AddRange(system, frame, gasket, service);
            await db.SaveChangesAsync();
            db.StockMovements.Add(new StockMovement { ProductId = frame.Id, Date = new DateOnly(2026, 11, 1), Kind = StockMovementKind.Opening, Quantity = 1 });
            await db.SaveChangesAsync();
            return (system.Id, frame.Id, gasket.Id, service.Id);
        });

        (string, string)[] SystemForm(params (string, string)[] rules) => new[] { ("Name", "Test serisi"), ("Category", "aluminum"), ("FrameWidthMm", "70"), ("SashWidthMm", "80"), ("MullionWidthMm", "80"),
            ("SashOverlapMm", "8"), ("GlassClearanceMm", "5"), ("BarLengthMm", "6000"), ("KerfMm", "4"), ("IsActive", "true") }.Concat(rules).ToArray();

        // Profil esasında ad zorunlu: kayıt reddedilir
        var bad = await client.PostFormAsync($"/yonetim/cizim/sistemler/duzenle/{systemId}", token, SystemForm(("rules[0].ProductId", frame.ToString()), ("rules[0].Basis", MaterialBases.ProfileBars), ("rules[0].Factor", "1")));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("profil veya donanım adını yazın", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));

        var saved = await client.PostFormAsync($"/yonetim/cizim/sistemler/duzenle/{systemId}", token, SystemForm(
            ("rules[0].ProductId", frame.ToString()), ("rules[0].Basis", MaterialBases.ProfileBars), ("rules[0].Key", "Kasa"), ("rules[0].Factor", "1"),
            ("rules[1].ProductId", gasket.ToString()), ("rules[1].Basis", MaterialBases.GlassPerimeter), ("rules[1].Key", "yok sayılır"), ("rules[1].Factor", "2"),
            ("rules[2].ProductId", service.ToString()), ("rules[2].Basis", MaterialBases.Piece), ("rules[2].Factor", "1"),
            ("rules[3].ProductId", "0"), ("rules[3].Basis", MaterialBases.Piece), ("rules[3].Factor", "1"))); // boş satır atlanır
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var rules = MaterialTakeoff.ParseRules(await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Id == systemId).Select(s => s.MaterialsJson).SingleAsync()));
        Assert.Equal(3, rules.Count);
        Assert.Null(rules[1].Key);

        var list = "Metraj listesi";
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 4, system: systemId)));
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Metraj işi"), ("DrawingListId", (await DrawingLists.ListIdAsync(app, list)).ToString()),
            ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Metraj işi").Select(o => o.Id).SingleAsync());

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/metraj/{orderId}"));
        Assert.Contains("K-100", page);
        Assert.Contains("Cam contası", page);
        Assert.Contains("kalem eksik", page);
        Assert.Contains("\"Kanat\" profili için stok kartı yok", page);
        var print = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/malzeme-listesi/{orderId}?tarih=2026-12-01"));
        Assert.Contains("MALZEME SİPARİŞ FORMU", print);
        Assert.Contains("01.12.2026", print);

        await client.PostFormAsync($"/yonetim/siparisler/stok-dus/{orderId}", token);
        await client.PostFormAsync($"/yonetim/siparisler/stok-dus/{orderId}", token); // ikinci kez düşmez
        var movements = await app.WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.ProductionOrderId == orderId).ToListAsync());
        Assert.Equal(2, movements.Count); // hizmet kartı stoktan düşmez
        Assert.All(movements, m => Assert.Equal(StockMovementKind.Production, m.Kind));
        Assert.True(movements.Single(m => m.ProductId == frame).Quantity <= -1);
        var order = await app.WithDbAsync(db => db.ProductionOrders.AsNoTracking().Include(o => o.Events).SingleAsync(o => o.Id == orderId));
        Assert.True(order.StockDeducted);
        Assert.Contains(order.Events, e => e.Text == "Metraj stoktan düşüldü (2 kalem).");

        await client.PostFormAsync($"/yonetim/siparisler/stok-geri-al/{orderId}", token);
        var net = await app.WithDbAsync(db => db.StockMovements.Where(m => m.ProductionOrderId == orderId).SumAsync(m => m.Quantity));
        Assert.Equal(0, net);
        Assert.Equal(4, await app.WithDbAsync(db => db.StockMovements.CountAsync(m => m.ProductionOrderId == orderId))); // geçmiş silinmez
        Assert.False(await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.StockDeducted).SingleAsync()));
    }

    [Fact]
    public async Task SampleMaterials_CreatesStockCardsAndRulesOnce()
    {
        await using var app = postgres.CreateApp("material_samples");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var systemId = await app.WithDbAsync(async db =>
        {
            var system = new ProfileSystem { Name = "Örnekli seri", Category = ProductCategories.Pvc, FramePricePerM = 150 };
            db.ProfileSystems.Add(system);
            await db.SaveChangesAsync();
            return system.Id;
        });
        await client.PostFormAsync($"/yonetim/cizim/sistemler/ornek-malzeme/{systemId}", token);
        await client.PostFormAsync($"/yonetim/cizim/sistemler/ornek-malzeme/{systemId}", token);
        var rules = MaterialTakeoff.ParseRules(await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Id == systemId).Select(s => s.MaterialsJson).SingleAsync()));
        var expected = SampleCatalog.For(new ProfileSystem { Category = ProductCategories.Pvc }).Count();
        Assert.Equal(expected, rules.Count);
        Assert.Equal(expected, await app.WithDbAsync(db => db.Products.CountAsync(p => p.Name.StartsWith("Örnekli seri - "))));
        var frame = await app.WithDbAsync(db => db.Products.SingleAsync(p => p.Name == "Örnekli seri - Kasa profili (örnek)"));
        Assert.Equal((150m * 6m, "Boy"), (frame.PurchasePrice, frame.Unit)); // metre fiyatı × 6 m boy
    }
}
