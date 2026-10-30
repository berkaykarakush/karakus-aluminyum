using System.Net;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

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

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, string name, string layout = Layout, int width = 1000, int height = 1000)
        => client.PostFormAsync(url, token, ("Name", name), ("Category", "aluminum"), ("ColorHex", "#3a4046"),
            ("WidthMm", width.ToString()), ("HeightMm", height.ToString()), ("Shape", "rect"), ("ShapeParamMm", "0"), ("Quantity", "2"), ("LayoutJson", layout));

    private static string Name() => "Çizim " + Guid.NewGuid().ToString("N")[..8];

    private static Task<int> IdAsync(AppFactory app, string name)
        => app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Name == name).Select(d => d.Id).SingleAsync());

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
    public async Task Seed_CreatesPricelessGlassTypeOnly()
    {
        var app = postgres.Shared;
        app.NewClient();
        var glass = await app.WithDbAsync(db => db.GlassTypes.FirstAsync(g => g.Name.StartsWith("Örnek")));
        Assert.Equal(0m, glass.PricePerM2);
    }

    [Fact]
    public async Task Designer_PageRenders()
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        var html = await client.GetStringAsync("/yonetim/cizim/ekle?tur=aluminum");
        Assert.Contains("id=\"wd-canvas\"", html);
        Assert.Contains("window-designer.js", html);
    }

    [Fact]
    public async Task Save_ThenDrawingPageShowsTheShapeWithoutManufacturing()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();

        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token, "/yonetim/cizim/ekle", name)).StatusCode);
        var id = await IdAsync(app, name);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{id}"));
        Assert.Contains("wd-sheet-drawing", page);
        Assert.Contains("\"opening\":\"tiltRight\"", page);
        Assert.Contains("100 × 100 cm", page);
        foreach (var removed in new[] { "Kesim", "Maliyet", "Optimizasyon", "Profil", "İmalat" }) Assert.DoesNotContain(removed, page);
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim")));

        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token, $"/yonetim/cizim/duzenle/{id}", name, width: 1500)).StatusCode);
        Assert.Equal(1500, await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.Id == id).Select(d => d.WidthMm).SingleAsync()));
    }

    [Theory]
    [InlineData("/yonetim/cizim/imalat/1")]
    [InlineData("/yonetim/cizim/optimizasyon")]
    [InlineData("/yonetim/cizim/sistemler")]
    public async Task ManufacturingPages_AreGone(string url)
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Save_RejectsBrokenLayoutsAndPartsThatDoNotAddUp_WithoutSaving()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);

        foreach (var layout in new[] { "{bozuk", "{\"split\":\"v\",\"sizes\":[400,400],\"children\":[{},{}]}" })
        {
            var name = Name();
            Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, "/yonetim/cizim/ekle", name, layout)).StatusCode);
            Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name)));
        }
    }

    [Fact]
    public async Task Save_UnknownCustomer_IsRejected()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        var response = await client.PostFormAsync("/yonetim/cizim/ekle", token, ("Name", name), ("Category", "aluminum"), ("CustomerId", "999999"), ("ColorHex", "#3a4046"),
            ("WidthMm", "1000"), ("HeightMm", "1000"), ("Shape", "rect"), ("Quantity", "1"), ("LayoutJson", Layout));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name)));
    }

    [Fact]
    public async Task AddToQuote_UsesTypedPrice_AndRecalculatesDraft_ButNotOtherStatuses()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name);
        var designId = await IdAsync(app, name);

        var draft = await NewQuoteAsync(app, QuoteStatus.Draft);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", draft.ToString()), ("unitPrice", "1250.50"));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == draft));
        var line = Assert.Single(quote.Items);
        Assert.Contains(name, line.Description);
        Assert.Contains("100x100 cm", line.Description);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(1250.50m, line.UnitPrice);
        Assert.Equal(line.LineTotal, quote.Subtotal);
        Assert.True(quote.GrandTotal > quote.Subtotal);

        var sent = await NewQuoteAsync(app, QuoteStatus.Sent);
        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", sent.ToString()), ("unitPrice", "100"));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == sent)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task AddToQuote_RejectsMissingPrice(string price)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name);
        var designId = await IdAsync(app, name);
        var quoteId = await NewQuoteAsync(app, QuoteStatus.Draft);

        await client.PostFormAsync($"/yonetim/cizim/teklife-ekle/{designId}", token, ("quoteId", quoteId.ToString()), ("unitPrice", price));
        Assert.False(await app.WithDbAsync(db => db.QuoteItems.AnyAsync(i => i.QuoteId == quoteId)));
    }

    [Fact]
    public async Task CopyAndDelete_Work()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Name();
        await SaveAsync(client, token, "/yonetim/cizim/ekle", name);
        var id = await IdAsync(app, name);

        await client.PostFormAsync($"/yonetim/cizim/kopyala/{id}", token);
        Assert.True(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Name == name + " (kopya)")));

        await client.PostFormAsync($"/yonetim/cizim/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.WindowDesigns.AnyAsync(d => d.Id == id)));
    }

    [Fact]
    public async Task ExistingDatabaseWithoutDrawingTables_IsUpgradedOnStart()
    {
        await using (var first = postgres.CreateApp("drawing_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"WindowDesigns\"; DROP TABLE \"GlassListItems\"; DROP TABLE \"GlassLists\"; DROP TABLE \"GlassTypes\""));
        }

        await using var second = postgres.CreateApp("drawing_upgrade", schemaMode: "existing");
        second.NewClient();
        Assert.Equal(0, await second.WithDbAsync(db => db.WindowDesigns.CountAsync()));
    }

    // Üretim dönemindeki veritabanı: çizimler korunur, sistem bağı kalkar, eski tablo silinmez.
    [Fact]
    public async Task ManufacturingDatabase_KeepsDrawings_AndDropsOnlyTheSystemLink()
    {
        const string sizes = "{\"split\":\"v\",\"sizes\":[1,1],\"children\":[{},{}]}";
        await using (var first = postgres.CreateApp("shape_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "ProfileSystems" ("Id" serial PRIMARY KEY, "Name" text NOT NULL, "Material" text NOT NULL);
                INSERT INTO "ProfileSystems" ("Id","Name","Material") VALUES (70,'Eski PVC','PVC'),(71,'Eski Alüminyum','Alüminyum');
                ALTER TABLE "WindowDesigns" ADD COLUMN "ProfileSystemId" integer REFERENCES "ProfileSystems"("Id") ON DELETE RESTRICT;
                ALTER TABLE "GlassTypes" ADD COLUMN "SheetWidthMm" numeric(10,2) NOT NULL DEFAULT 3210, ADD COLUMN "SheetHeightMm" numeric(10,2) NOT NULL DEFAULT 2250, ADD COLUMN "CutGapMm" numeric(10,2) NOT NULL DEFAULT 2;
                INSERT INTO "WindowDesigns" ("Name","Category","ProfileSystemId","ColorHex","WidthMm","HeightMm","Shape","ShapeParamMm","Quantity","LayoutJson","ParamsJson","CreatedUtc","UpdatedUtc")
                VALUES ('Eski plastik','aluminum',70,'#ffffff',1500,1400,'rect',0,1,'LAYOUT','{{}}',now(),now()),('Eski metal','aluminum',71,'#ffffff',1500,1400,'rect',0,1,'LAYOUT','{{}}',now(),now());
                """.Replace("LAYOUT", sizes.Replace("{", "{{").Replace("}", "}}"))));
        }

        await using var second = postgres.CreateApp("shape_upgrade", schemaMode: "existing");
        var (client, _) = await AdminAsync(second);
        var rows = await second.WithDbAsync(db => db.WindowDesigns.AsNoTracking().ToDictionaryAsync(d => d.Name));
        Assert.Equal(ProductCategories.Pvc, rows["Eski plastik"].Category);
        Assert.Equal(ProductCategories.Aluminum, rows["Eski metal"].Category);
        Assert.Equal(sizes, rows["Eski metal"].LayoutJson);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/detay/{rows["Eski metal"].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/yonetim/cizim/duzenle/{rows["Eski metal"].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/camlar/tipler")).StatusCode);

        var columns = await second.WithDbAsync(db => db.Database.SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name IN ('WindowDesigns','GlassTypes')").ToListAsync());
        foreach (var removed in new[] { "ProfileSystemId", "SheetWidthMm", "SheetHeightMm", "CutGapMm" }) Assert.DoesNotContain(removed, columns);
        Assert.Equal(2, await second.WithDbAsync(db => db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"ProfileSystems\"").SingleAsync()));
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
        Assert.DoesNotContain("Plaka", html);
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
    public async Task EachCategory_OpensItsDesigner(string category, string canvas)
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        Assert.Contains(canvas, await client.GetStringAsync($"/yonetim/cizim/ekle?tur={category}"));
    }

    [Fact]
    public async Task Shutter_SavesShowsAndFiltersByCategory()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = "Panjur " + Guid.NewGuid().ToString("N")[..8];

        var saved = await client.PostFormAsync("/yonetim/cizim/ekle", token, ("Name", name), ("Category", "shutter"), ("ColorHex", "#3a4046"),
            ("WidthMm", "1000"), ("HeightMm", "1400"), ("Quantity", "1"), ("ParamsJson", "{\"drive\":\"manual\"}"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var design = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Name == name));
        Assert.Equal(ProductCategories.Shutter, design.Category);
        Assert.Contains("manual", design.ParamsJson);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{design.Id}"));
        Assert.Contains("\"category\":\"shutter\"", page);
        Assert.Contains("\"drive\":\"manual\"", page);

        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=shutter")));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim?tur=railing")));
        Assert.Contains("name=\"Category\"", await client.GetStringAsync($"/yonetim/cizim/duzenle/{design.Id}"));
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
            var design = new WindowDesign { Name = "Birim " + Guid.NewGuid().ToString("N")[..8], WidthMm = 1500, HeightMm = 1400, LayoutJson = "{\"opening\":\"fixed\"}" };
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
        var token = await client.GetTokenAsync("/yonetim/cizim/ekle?tur=aluminum");
        var id = await app.WithDbAsync(async db =>
        {
            var design = new WindowDesign { Name = "Tür " + Guid.NewGuid().ToString("N")[..6], WidthMm = 1000, HeightMm = 1000, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });

        var name = "Değişti " + Guid.NewGuid().ToString("N")[..6];
        await client.PostFormAsync($"/yonetim/cizim/duzenle/{id}", token, ("Name", name), ("Category", "shutter"), ("ColorHex", "#3a4046"), ("WidthMm", "1000"), ("HeightMm", "1000"),
            ("Shape", "rect"), ("Quantity", "1"), ("LayoutJson", "{\"opening\":\"fixed\"}"), ("ParamsJson", "{\"drive\":\"manual\"}"));
        var row = await app.WithDbAsync(db => db.WindowDesigns.AsNoTracking().SingleAsync(d => d.Id == id));
        Assert.Equal(name, row.Name);
        Assert.Equal(ProductCategories.Aluminum, row.Category);
    }
}
