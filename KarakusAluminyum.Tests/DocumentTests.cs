using System.Net;
using System.Text.Json;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class DocumentTextTests
{
    // Tutarlar ve yazılışları örnek teklif ve sözleşmelerden alındı.
    [Theory]
    [InlineData(3750000, "ÜçMilyonYediYüzElliBinTürkLirası")]
    [InlineData(4250000, "DörtMilyonİkiYüzElliBinTürkLirası")]
    [InlineData(3050000, "ÜçMilyonElliBinTürkLirası")]
    [InlineData(950000, "DokuzYüzElliBinTürkLirası")]
    [InlineData(11100000, "OnBirMilyonYüzBinTürkLirası")]
    [InlineData(750000, "YediYüzElliBinTürkLirası")]
    [InlineData(4350000, "DörtMilyonÜçYüzElliBinTürkLirası")]
    [InlineData(870000, "SekizYüzYetmişBinTürkLirası")]
    [InlineData(500000, "BeşYüzBinTürkLirası")]
    [InlineData(200000, "İkiYüzBinTürkLirası")]
    [InlineData(100000, "YüzBinTürkLirası")]
    [InlineData(400000, "DörtYüzBinTürkLirası")]
    public void MoneyWords_MatchSampleDocuments(int amount, string expected)
        => Assert.Equal(expected, TurkishNumbers.MoneyWords(amount));

    [Theory]
    [InlineData(1000, "BinTürkLirası")]
    [InlineData(1001, "BinBirTürkLirası")]
    [InlineData(101, "YüzBirTürkLirası")]
    [InlineData(21000, "YirmiBirBinTürkLirası")]
    [InlineData(0, "SıfırTürkLirası")]
    public void MoneyWords_HandleEdgeCases(int amount, string expected)
        => Assert.Equal(expected, TurkishNumbers.MoneyWords(amount));

    [Fact]
    public void MoneyWords_IncludeKurus()
        => Assert.Equal("BinTürkLirasıYirmiBeşKuruş", TurkishNumbers.MoneyWords(1000.25m));

    [Fact]
    public void Money_UsesTurkishFormat()
        => Assert.Equal("1.785.000,00 TL", TurkishNumbers.Money(1785000m));

    [Theory]
    [InlineData(7, "yedi")]
    [InlineData(4, "dört")]
    [InlineData(3, "üç")]
    public void CountWords_AreLowercase(int value, string expected)
        => Assert.Equal(expected, TurkishNumbers.CountWords(value));

    [Fact]
    public void Render_UsesFirstScopeWithValue_AndBlankForMissing()
    {
        var block = new Dictionary<string, string> { ["adet"] = "96", ["renk"] = " " };
        var document = new Dictionary<string, string> { ["adet"] = "1", ["renk"] = "vizon" };
        Assert.Equal($"96 vizon {TemplateText.Blank}", TemplateText.Render("{{adet}} {{ renk }} {{ada}}", block, document));
    }

    [Fact]
    public void Placeholders_FindTurkishNamesOnce()
        => Assert.Equal(new[] { "marka", "dükkan_adedi" }, TemplateText.Placeholders("{{marka}} {{dükkan_adedi}} {{marka}}"));

    [Fact]
    public void ToHtml_EscapesUserHtml_AndOnlyAllowsBoldAndLineBreaks()
    {
        var html = TemplateText.ToHtml("**kalın** <script>x</script>\n{{a}}", new Dictionary<string, string> { ["a"] = "<img src=x onerror=y>" }).ToString()!;
        Assert.Contains("<strong>kalın</strong>", html);
        Assert.Contains("<br>", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void PaymentPlan_DescribesLinesLikeSampleContracts()
    {
        Assert.Equal("**750.000,00 TL (YediYüzElliBinTürkLirası)** peşin olarak ödenecektir.", PaymentPlan.Describe(new PaymentLine { Kind = PaymentKinds.Cash, Amount = 750000 }));
        Assert.Equal("**4.350.000,00 TL (DörtMilyonÜçYüzElliBinTürkLirası)** tutar, her biri **870.000,00 TL (SekizYüzYetmişBinTürkLirası)** değerinde olmak üzere 5 adet çekle tahsil edilecektir.",
            PaymentPlan.Describe(new PaymentLine { Kind = PaymentKinds.Check, Amount = 4350000, Count = 5 }));
        Assert.Equal("Kalan **4.350.000,00 TL (DörtMilyonÜçYüzElliBinTürkLirası)** tutar, her biri **870.000,00 TL (SekizYüzYetmişBinTürkLirası)** değerinde olmak üzere sıralı 5 ay vadeli çeklerle tahsil edilecektir.",
            PaymentPlan.Describe(new PaymentLine { Kind = PaymentKinds.Check, Amount = 4350000, Count = 5, Text = "sıralı 5 ay vadeli çeklerle" }, isRemainder: true));
        Assert.Equal("**100.000,00 TL (YüzBinTürkLirası)** tutar, 1 adet çekle tahsil edilecektir.", PaymentPlan.Describe(new PaymentLine { Kind = PaymentKinds.Check, Amount = 100000 }));
        var barter = PaymentPlan.Describe(new PaymentLine { Kind = PaymentKinds.Barter, Amount = 6000000, Text = "4 numaralı daire" });
        Assert.Contains("**Barter:** 4 numaralı daire", barter);
        Assert.Contains("bedelle barter olarak verilecektir.", barter);
    }

    [Fact]
    public void PaymentPlan_ValidatesSumAndValues()
    {
        // Örnek sözleşme: 6.000.000 barter + 750.000 peşin + 4.350.000 çek = 11.100.000
        var lines = new List<PaymentLine> { new() { Kind = PaymentKinds.Barter, Amount = 6000000 }, new() { Amount = 750000 }, new() { Kind = PaymentKinds.Check, Amount = 4350000, Count = 5 } };
        Assert.Empty(PaymentPlan.Validate(lines, 11100000));
        Assert.Contains(PaymentPlan.Validate(lines, 11050000), e => e.Contains("eşit değil"));
        Assert.Contains(PaymentPlan.Validate(new[] { new PaymentLine { Kind = "yok", Amount = 1 } }, 1), e => e.Contains("Geçersiz"));
        Assert.Contains(PaymentPlan.Validate(new[] { new PaymentLine { Kind = PaymentKinds.Check, Amount = 1, Count = 0 } }, 1), e => e.Contains("Çek adedi"));
    }

    // Depo herkese açık: başlangıç şablonları kişisel bilgi ve vergi numarası içermemeli.
    [Fact]
    public void Seed_ContainsNoPersonalData()
    {
        foreach (var template in new[] { DocumentSeed.QuoteTemplate(), DocumentSeed.ContractTemplate() })
        {
            Assert.Null(template.Signatory);
            Assert.Null(template.SellerTaxInfo);
        }
        var all = JsonSerializer.Serialize(new object[] { DocumentSeed.QuoteTemplate(), DocumentSeed.ContractTemplate(), DocumentSeed.Clauses().ToList() });
        Assert.DoesNotMatch(@"(?<!\d)\d{10,11}(?!\d)", all); // vergi veya T.C. kimlik numarası biçimi
    }
}

[Collection("postgres")]
public sealed class QuoteDocumentTests(PostgresFixture postgres)
{
    private static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    internal static async Task<int> NewCustomerAsync(AppFactory app, string? name = null) => await app.WithDbAsync(async db =>
    {
        var customer = new Customer { Name = name ?? Unique("Müşteri"), Address = "Test Mah. 1", TaxOffice = "Bornova", TaxNumber = "1234567890" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    });

    internal static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/crm/teklifler/ekle"));
    }

    internal static string BlocksJson(params (string Name, (string Description, decimal Price)[] Items)[] blocks) => JsonSerializer.Serialize(new
    {
        intro = "Yapımı devam eden {{proje}} inşaatlarınız için teklifimizdir.",
        closing = "Fiyatlar aşağıdadır.",
        note = "Bodrum kat dahil değildir.",
        variables = new Dictionary<string, string> { ["proje"] = "3 blok", ["renk"] = "vizon" },
        blocks = blocks.Select(b => new
        {
            name = b.Name, showLines = true, variables = new Dictionary<string, string>(),
            clauses = new[] { new { title = "Panjur", text = "Panjurlar {{renk}} renkte olacaktır." } },
            items = b.Items.Select(i => new { description = i.Description, quantity = 1, unit = "Adet", unitPrice = i.Price })
        })
    });

    internal static Task<HttpResponseMessage> SaveQuoteAsync(HttpClient client, string token, string url, int customerId, string title, string json, string vat = "Included")
        => client.PostFormAsync(url, token, ("CustomerId", customerId.ToString()), ("Title", title), ("DocumentDate", "2026-09-22"), ("VatMode", vat), ("TaxRate", "20"), ("DiscountRate", "0"), ("DocumentJson", json));

    [Fact]
    public async Task Create_StoresBlocksAndTotals_LikeSampleQuote()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await NewCustomerAsync(app);
        var title = Unique("Teklif");

        var response = await SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, BlocksJson(
            ("A Blok", new[] { ("Doğrama ve Camlar", 1785000m), ("Korkuluklar", 850000m), ("Dükkan", 150000m), ("Panjurlar", 965000m) }),
            ("C Blok", new[] { ("Doğrama ve Camlar", 1540000m), ("Korkuluklar", 640000m), ("Panjurlar", 870000m) })));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Title == title));
        Assert.Equal(6800000m, quote.GrandTotal);
        Assert.Equal(VatMode.Included, quote.VatMode);
        Assert.Equal(new DateTime(2026, 9, 22), quote.DocumentDate.Date);
        Assert.Equal(7, quote.Items.Count);
        Assert.Equal(4, quote.Items.Count(i => i.BlockName == "A Blok"));
        Assert.NotEqual("{}", quote.SnapshotJson);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{quote.Id}"));
        Assert.Contains("TEKLİFTİR", html);
        Assert.Contains("A Blok", html);
        Assert.Contains("1.785.000,00 TL", html);
        Assert.Contains("3.750.000,00 TL", html);
        Assert.Contains("ÜçMilyonYediYüzElliBinTürkLirası", html);
        Assert.Contains("3.050.000,00 TL", html);
        Assert.Contains("(KDV Dahil)", html);
        Assert.Contains("Yapımı devam eden 3 blok inşaatlarınız", html);
        Assert.Contains("Panjurlar vizon renkte", html);
        Assert.Contains("Bodrum kat dahil değildir.", html);
        Assert.DoesNotContain("{{", html);
    }

    [Theory]
    [InlineData("{bozuk")]
    [InlineData("{\"blocks\":[{\"items\":[]}]}")]
    [InlineData("{\"blocks\":[{\"items\":[{\"description\":\"x\",\"quantity\":0,\"unitPrice\":5}]}]}")]
    [InlineData("{\"blocks\":[{\"items\":[{\"description\":\"x\",\"quantity\":1,\"unitPrice\":-5}]}]}")]
    [InlineData("{\"blocks\":[{\"name\":\"A\",\"items\":[{\"description\":\"x\",\"quantity\":1,\"unitPrice\":5}]},{\"name\":\"A\",\"items\":[]}]}")]
    public async Task Create_RejectsInvalidContent_WithoutCrashing(string json)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await NewCustomerAsync(app);
        var title = Unique("Geçersiz");

        var response = await SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Quotes.AnyAsync(q => q.Title == title)));
    }

    [Fact]
    public async Task Edit_ReplacesItems_OnlyWhileDraft()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await NewCustomerAsync(app);
        var title = Unique("Düzenleme");
        await SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, BlocksJson(("", new[] { ("Eski", 100m) })));
        var id = await app.WithDbAsync(db => db.Quotes.Where(q => q.Title == title).Select(q => q.Id).SingleAsync());

        Assert.Contains("Eski", await client.GetStringAsync($"/yonetim/crm/teklifler/duzenle/{id}"));
        await SaveQuoteAsync(client, token, $"/yonetim/crm/teklifler/duzenle/{id}", customer, title, BlocksJson(("", new[] { ("Yeni", 250m) })));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Id == id));
        Assert.Equal("Yeni", Assert.Single(quote.Items).Description);
        Assert.Equal(250m, quote.GrandTotal);

        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{id}", token, ("status", ((int)QuoteStatus.Sent).ToString()));
        var blocked = await SaveQuoteAsync(client, token, $"/yonetim/crm/teklifler/duzenle/{id}", customer, title, BlocksJson(("", new[] { ("Değişmemeli", 999m) })));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal(250m, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == id).Select(q => q.GrandTotal).SingleAsync()));
    }

    [Fact]
    public async Task OldQuoteWithoutDocument_StillPrints()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        var customer = await NewCustomerAsync(app);
        var id = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "E-" + Guid.NewGuid().ToString("N")[..10], Title = "Eski", CustomerId = customer, GrandTotal = 120, Subtotal = 100, TaxAmount = 20, Items = { new QuoteItem { Description = "Eski kalem", Quantity = 1, UnitPrice = 100, LineTotal = 100 } } };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{id}"));
        Assert.Contains("Eski kalem", html);
        Assert.Contains("120,00 TL", html);
    }
}

[Collection("postgres")]
public sealed class ContractTests(PostgresFixture postgres)
{
    private static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    private static string ContentJson(decimal paid, params int[] designIds) => JsonSerializer.Serialize(new
    {
        subject = "1 adet villanın doğrama işleri.",
        closing = "Bedel aşağıdadır.",
        priceNote = "Faturalandırma notu.",
        variables = new Dictionary<string, string> { ["renk"] = "antirasit gri" },
        blocks = new[]
        {
            new { name = "A Blok", footer = "A blok eksiksiz yapılacaktır.", variables = new Dictionary<string, string> { ["adet"] = "96" }, clauses = new[] { new { title = "PVC Doğramalar", text = "{{adet}} adet **{{renk}}** doğrama, ada {{ada}}." } } }
        },
        payments = new object[] { new { kind = "cash", amount = paid, count = 1, text = "" } },
        designIds
    });

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, int customer, string title, string json, string total = "500000", string pages = "3")
        => client.PostFormAsync(url, token, ("CustomerId", customer.ToString()), ("Title", title), ("DocumentDate", "2026-10-27"), ("BuyerName", "Alıcı Firma A.Ş."), ("BuyerAddress", "Bornova"), ("BuyerTaxInfo", "VD 111"),
            ("TotalAmount", total), ("VatMode", "None"), ("PageCount", pages), ("DocumentJson", json));

    private async Task<(AppFactory App, HttpClient Client, string Token, int Customer)> SetupAsync()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        return (app, client, token, await QuoteDocumentTests.NewCustomerAsync(app));
    }

    private static Task<int> IdAsync(AppFactory app, string title) => app.WithDbAsync(db => db.Contracts.Where(c => c.Title == title).Select(c => c.Id).SingleAsync());

    [Fact]
    public async Task Create_PrintsArticlesFromTemplate_WithVariablesAndTotals()
    {
        var (app, client, token, customer) = await SetupAsync();
        var title = Unique("Sözleşme");
        var response = await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(500000));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{await IdAsync(app, title)}"));
        Assert.Contains("SÖZLEŞMEDİR", html);
        Assert.Contains("1. Taraflar", html);
        Assert.Contains("7. Yetkili Mahkeme ve İcra Daireleri", html);
        Assert.Contains("Alıcı Firma A.Ş.", html);
        Assert.Contains("<strong>antirasit gri</strong>", html);
        Assert.Contains("96 adet", html);
        Assert.Contains(TemplateText.Blank, html); // bilinmeyen ada numarası elle doldurulur
        Assert.Contains("500.000,00 TL (BeşYüzBinTürkLirası)", html);
        Assert.Contains("peşin olarak ödenecektir", html);
        Assert.Contains("7 (yedi) maddeden ve 3 (üç) sayfadan", html);
        Assert.Contains("27/10/2026", html);
        Assert.Contains("A blok eksiksiz yapılacaktır.", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task SubjectWithBoldMarkers_DoesNotPrintStrayAsterisks()
    {
        var (app, client, token, customer) = await SetupAsync();
        var title = Unique("Kalın");
        var json = ContentJson(500000).Replace("1 adet", "**1 adet**");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, json);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{await IdAsync(app, title)}"));
        Assert.Contains("<strong>1 adet villanın doğrama işleri.</strong>", html);
        Assert.DoesNotContain("**", html);
    }

    [Fact]
    public async Task Sign_RequiresMatchingPaymentPlanAndSignatory_ThenFreezesContract()
    {
        var (app, client, token, customer) = await SetupAsync();
        var title = Unique("İmza");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(100000));
        var id = await IdAsync(app, title);

        await client.PostFormAsync($"/yonetim/sozlesmeler/imzala/{id}", token);
        Assert.Equal(ContractStatus.Draft, await app.WithDbAsync(db => db.Contracts.Where(c => c.Id == id).Select(c => c.Status).SingleAsync()));

        await SaveAsync(client, token, $"/yonetim/sozlesmeler/duzenle/{id}", customer, title, ContentJson(500000));
        await SetSignatoryInSnapshot(app, id);
        await client.PostFormAsync($"/yonetim/sozlesmeler/imzala/{id}", token);
        Assert.Equal(ContractStatus.Signed, await app.WithDbAsync(db => db.Contracts.Where(c => c.Id == id).Select(c => c.Status).SingleAsync()));

        await SaveAsync(client, token, $"/yonetim/sozlesmeler/duzenle/{id}", customer, title, ContentJson(1), total: "1");
        Assert.Equal(500000m, await app.WithDbAsync(db => db.Contracts.Where(c => c.Id == id).Select(c => c.TotalAmount).SingleAsync()));
        await client.PostFormAsync($"/yonetim/sozlesmeler/sil/{id}", token);
        Assert.True(await app.WithDbAsync(db => db.Contracts.AnyAsync(c => c.Id == id)));

        await client.PostFormAsync($"/yonetim/sozlesmeler/iptal/{id}", token);
        Assert.Equal(ContractStatus.Cancelled, await app.WithDbAsync(db => db.Contracts.Where(c => c.Id == id).Select(c => c.Status).SingleAsync()));
    }

    private static Task SetSignatoryInSnapshot(AppFactory app, int id) => app.WithDbAsync(async db =>
    {
        var contract = await db.Contracts.SingleAsync(c => c.Id == id);
        var template = DocumentJson.ParseTemplate(contract.SnapshotJson)!;
        template.Signatory = "Test Yetkili";
        contract.SnapshotJson = DocumentJson.Serialize(template);
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task SnapshotKeepsContractUnchanged_WhenTemplateChanges_UntilRefreshed()
    {
        await using var app = postgres.CreateApp("doc_snapshot");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("Kopya");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(500000));
        var id = await IdAsync(app, title);

        await app.WithDbAsync(db => db.DocumentTemplates.Where(t => t.Kind == DocumentKinds.Contract).ExecuteUpdateAsync(t => t.SetProperty(x => x.Title, "YENİ BAŞLIK")));
        var before = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{id}"));
        Assert.Contains("SÖZLEŞMEDİR", before);
        Assert.DoesNotContain("YENİ BAŞLIK", before);

        await client.PostFormAsync($"/yonetim/sozlesmeler/sablonu-yenile/{id}", token);
        Assert.Contains("YENİ BAŞLIK", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{id}")));
    }

    [Fact]
    public async Task FromQuote_OnlyWhenAccepted_CopiesData_AndConvertsQuote()
    {
        var (app, client, token, customer) = await SetupAsync();
        var title = Unique("Dönüşüm");
        var clauseName = await app.WithDbAsync(db => db.TemplateClauses.OrderBy(c => c.SortOrder).Select(c => c.Name).FirstAsync());
        var clauseText = await app.WithDbAsync(db => db.TemplateClauses.OrderBy(c => c.SortOrder).Select(c => c.ContractText).FirstAsync());
        var json = JsonSerializer.Serialize(new
        {
            variables = new Dictionary<string, string> { ["marka"] = "Test Marka" },
            blocks = new[] { new { name = "A Blok", showLines = true, variables = new Dictionary<string, string> { ["adet"] = "96" }, clauses = new[] { new { title = clauseName, text = "Teklif metni" } }, items = new[] { new { description = "İş", quantity = 1, unit = "Adet", unitPrice = 750000m } } } }
        });
        await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, json);
        var quoteId = await app.WithDbAsync(db => db.Quotes.Where(q => q.Title == title).Select(q => q.Id).SingleAsync());

        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quoteId}", token);
        Assert.False(await app.WithDbAsync(db => db.Contracts.AnyAsync(c => c.QuoteId == quoteId)));

        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{quoteId}", token, ("status", ((int)QuoteStatus.Sent).ToString()));
        await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{quoteId}", token, ("status", ((int)QuoteStatus.Accepted).ToString()));
        Assert.Contains("Sözleşme oluştur", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quoteId}")));

        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quoteId}", token);
        var contract = await app.WithDbAsync(db => db.Contracts.AsNoTracking().SingleAsync(c => c.QuoteId == quoteId));
        Assert.Equal(750000m, contract.TotalAmount);
        Assert.Equal("Test Mah. 1", contract.BuyerAddress);
        Assert.Equal("Bornova 1234567890", contract.BuyerTaxInfo);
        var content = DocumentJson.ParseContent(contract.DocumentJson);
        Assert.Equal("A Blok", Assert.Single(content.Blocks).Name);
        Assert.Equal(clauseText, Assert.Single(content.Blocks[0].Clauses).Text); // sözleşme metni kullanılır
        Assert.Equal("96", content.Blocks[0].Variables["adet"]);
        Assert.Equal("Test Marka", content.Variables["marka"]);
        Assert.Equal(QuoteStatus.Converted, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quoteId).Select(q => q.Status).SingleAsync()));

        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quoteId}", token);
        Assert.Equal(1, await app.WithDbAsync(db => db.Contracts.CountAsync(c => c.QuoteId == quoteId)));
    }

    [Fact]
    public async Task Attachments_PrintPositionsWithTotals()
    {
        var (app, client, token, customer) = await SetupAsync();
        var designId = await app.WithDbAsync(async db =>
        {
            var system = await db.ProfileSystems.FirstAsync();
            var design = new WindowDesign { Name = "Sürme Poz", ProfileSystemId = system.Id, WidthMm = 2200, HeightMm = 2250, Quantity = 2, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });
        var title = Unique("Ekli");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(500000, designId));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{await IdAsync(app, title)}"));
        Assert.Contains("Poz: 001", html);
        Assert.Contains("Adt: 2", html);
        Assert.Contains("Çevre: 8.9m", html.Replace(",", "."));
        Assert.Contains("Alan: 5.0m²", html.Replace(",", "."));
        Assert.Contains("Ünite Adedi: 2", html);
        Assert.Contains("data-doc-drawing", html);
    }

    [Fact]
    public async Task Create_RejectsNegativePaymentAndBrokenJson()
    {
        var (app, client, token, customer) = await SetupAsync();
        foreach (var json in new[] { "{bozuk", ContentJson(-5) })
        {
            var title = Unique("Hatalı");
            var response = await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.Contracts.AnyAsync(c => c.Title == title)));
        }
    }
}

[Collection("postgres")]
public sealed class DocumentTemplateTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/yonetim/sablonlar")]
    [InlineData("/yonetim/sablonlar/quote")]
    [InlineData("/yonetim/sablonlar/contract")]
    [InlineData("/yonetim/sablonlar/quote/onizleme")]
    [InlineData("/yonetim/sablonlar/contract/onizleme")]
    [InlineData("/yonetim/sablonlar/maddeler/ekle")]
    public async Task Pages_Render(string url)
    {
        var (client, _) = await QuoteDocumentTests.AdminAsync(postgres.Shared);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Save_ChangesLogoAndFirmDetails_ForNewDocuments()
    {
        await using var app = postgres.CreateApp("doc_template_save");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);

        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("YENİ TEKLİF"), "Title" },
            { new StringContent("Yetkili Kişi"), "Signatory" },
            { new StringContent("+90 000 000 00 00"), "Phone" },
            { new StringContent("Yeni Adres"), "FooterAddress" },
            { new StringContent("Saygılarımla,"), "SignOff" },
            { new ByteArrayContent(new byte[] { 0x89, 0x50 }), "logoFile", "logo.png" }
        };
        var saved = await client.PostAsync("/yonetim/sablonlar/quote", form);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/quote/onizleme"));
        Assert.Contains("YENİ TEKLİF", html);
        Assert.Contains("Yetkili Kişi", html);
        Assert.Contains("+90 000 000 00 00", html);
        Assert.Contains("Yeni Adres", html);
        Assert.Contains("/uploads/images/", html);
        Assert.DoesNotContain("/images/transparent.png", html);
    }

    // Tarayıcı boş alanları da gönderir: bedel maddesinin metni ve fazladan boş satırlar kaydı engellememeli.
    [Fact]
    public async Task ContractTemplate_SavesWithEmptyArticleFields_LikeRealForm()
    {
        await using var app = postgres.CreateApp("doc_template_real_form");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var response = await client.PostFormAsync("/yonetim/sablonlar/contract", token, ("Title", "SÖZLEŞMEDİR"), ("Signatory", "Form Yetkili"), ("SellerTaxInfo", "Örnek V.D. 1"),
            ("articles[0].Title", "Konu"), ("articles[0].Kind", "subject"), ("articles[0].Body", "x"),
            ("articles[1].Title", "Bedel"), ("articles[1].Kind", "price"), ("articles[1].Body", ""),
            ("articles[2].Title", "Ödeme"), ("articles[2].Kind", "payment"), ("articles[2].Body", ""),
            ("articles[3].Title", ""), ("articles[3].Kind", "text"), ("articles[3].Body", ""));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/contract/onizleme"));
        Assert.Contains("Form Yetkili", html);
        Assert.Contains("Örnek V.D. 1", html);
        Assert.DoesNotContain("4. ", html.Split("doc-signatures")[0]);
    }

    [Fact]
    public async Task ContractTemplate_RequiresDataArticles()
    {
        await using var app = postgres.CreateApp("doc_template_articles");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);

        var response = await client.PostFormAsync("/yonetim/sablonlar/contract", token, ("Title", "S"), ("articles[0].Title", "Yalnız metin"), ("articles[0].Kind", "text"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var template = await app.WithDbAsync(db => db.DocumentTemplates.AsNoTracking().SingleAsync(t => t.Kind == DocumentKinds.Contract));
        Assert.Equal(7, DocumentJson.ParseArticles(template.ArticlesJson).Count);
    }

    [Fact]
    public async Task ContractTemplate_ArticleChanges_RenumberAutomatically()
    {
        await using var app = postgres.CreateApp("doc_template_renumber");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var fields = new List<(string, string)> { ("Title", "SÖZLEŞME"), ("SignOff", "{{madde_sayisi}} ({{madde_sayisi_yazi}}) maddedir.") };
        foreach (var (i, title, kind) in new[] { (0, "Konu", "subject"), (1, "Bedel", "price"), (2, "Ödeme", "payment"), (3, "Ek Madde", "text") })
        {
            fields.Add(($"articles[{i}].Title", title));
            fields.Add(($"articles[{i}].Kind", kind));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/yonetim/sablonlar/contract", token, fields.ToArray())).StatusCode);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/contract/onizleme"));
        Assert.Contains("4. Ek Madde", html);
        Assert.Contains("4 (dört) maddedir.", html);
    }

    [Fact]
    public async Task Clauses_CrudWorks()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var name = "Madde " + Guid.NewGuid().ToString("N")[..8];

        await client.PostFormAsync("/yonetim/sablonlar/maddeler/ekle", token, ("Name", name), ("QuoteText", "T {{a}}"), ("ContractText", "S {{a}}"), ("SortOrder", "50"), ("IsActive", "true"));
        var id = await app.WithDbAsync(db => db.TemplateClauses.Where(c => c.Name == name).Select(c => c.Id).SingleAsync());
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/teklifler/ekle")));

        await client.PostFormAsync($"/yonetim/sablonlar/maddeler/duzenle/{id}", token, ("Name", name), ("QuoteText", "Yeni"), ("SortOrder", "1"), ("IsActive", "false"));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/teklifler/ekle")));

        await client.PostFormAsync($"/yonetim/sablonlar/maddeler/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.TemplateClauses.AnyAsync(c => c.Id == id)));
    }

    [Fact]
    public async Task ExistingDatabase_IsUpgradedWithNewColumnsAndTables()
    {
        await using (var first = postgres.CreateApp("doc_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
                DROP TABLE "Contracts"; DROP TABLE "DocumentTemplates"; DROP TABLE "TemplateClauses";
                ALTER TABLE "Quotes" DROP COLUMN "DocumentDate", DROP COLUMN "VatMode", DROP COLUMN "DocumentJson", DROP COLUMN "SnapshotJson";
                ALTER TABLE "QuoteItems" DROP COLUMN "BlockName", DROP COLUMN "SortOrder";
                """));
        }

        await using var second = postgres.CreateApp("doc_upgrade", schemaMode: "existing");
        var (client, _) = await QuoteDocumentTests.AdminAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/crm/teklifler")).StatusCode);
        Assert.Equal(2, await second.WithDbAsync(db => db.DocumentTemplates.CountAsync()));
        // Mevcut teklifler için KDV davranışı değişmez.
        var customer = await QuoteDocumentTests.NewCustomerAsync(second);
        var id = await second.WithDbAsync(async db =>
        {
            db.Database.ExecuteSqlRaw($"INSERT INTO \"Quotes\" (\"QuoteNumber\",\"CustomerId\",\"Title\",\"Status\",\"CreatedUtc\",\"TaxRate\",\"DiscountRate\",\"Subtotal\",\"DiscountAmount\",\"TaxAmount\",\"GrandTotal\") VALUES ('ESKI-1',{customer},'Eski',0,now(),20,0,100,0,20,120)");
            return await db.Quotes.Where(q => q.QuoteNumber == "ESKI-1").Select(q => q.Id).SingleAsync();
        });
        Assert.Equal(VatMode.Added, await second.WithDbAsync(db => db.Quotes.Where(q => q.Id == id).Select(q => q.VatMode).SingleAsync()));
    }
}
