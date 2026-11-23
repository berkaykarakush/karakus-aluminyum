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
        // Proforma: kalemler bölüm başlıklarıyla, bölüm toplamları, KDV dökümü ve yazıyla genel toplam.
        Assert.Contains("PROFORMA FATURA", html);
        Assert.Contains("<tr class=\"pf-section\"><td colspan=\"6\">A Blok</td></tr>", html);
        Assert.Contains("1.785.000,00", html);
        Assert.Contains("A Blok toplamı</td><td class=\"pf-num\">3.750.000,00", html);
        Assert.Contains("C Blok toplamı</td><td class=\"pf-num\">3.050.000,00", html);
        Assert.Contains("Birim Fiyat (KDV dahil)", html);
        Assert.Contains("Genel toplam (KDV dahil)</td><td>6.800.000,00 TL", html);
        Assert.Contains("Yalnız: " + TurkishNumbers.MoneyWords(6800000m), html);
        Assert.Contains("fatura yerine geçmez", html);
        Assert.Contains("Alıcı onayı", html);
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
        Assert.Contains("Eski kalem", html); // proforma kalemleri basar
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
        // Kalemsiz ve şartsız sözleşmede fiyat tablosu ve özel şartlar maddeleri basılmaz; numaralar kayar.
        var articles = DocumentSeed.ContractArticles().Count - 2;
        Assert.Contains($"{articles}. Yetkili Mahkeme ve İcra Daireleri", html);
        Assert.DoesNotContain("İş Kalemleri ve Birim Fiyatlar", html);
        Assert.DoesNotContain("Özel Şartlar", html);
        Assert.Contains("Alıcı Firma A.Ş.", html);
        Assert.Contains("<strong>antirasit gri</strong>", html);
        Assert.Contains("96 adet", html);
        Assert.Contains(TemplateText.Blank, html); // bilinmeyen ada numarası elle doldurulur
        Assert.Contains("500.000,00 TL (BeşYüzBinTürkLirası)", html);
        Assert.Contains("peşin olarak ödenecektir", html);
        Assert.Contains($"{articles} ({TurkishNumbers.CountWords(articles)}) maddeden ve 3 (üç) sayfadan", html);
        // Basılan tarih varsayılan olarak bugündür; kayıtlı sözleşme tarihi seçicide önerilir ve ?tarih ile basılır.
        Assert.Contains(PrintDates.Text(PrintDates.Today), html);
        Assert.Contains("Sözleşme tarihi (27.10.2026)", html);
        var id = await IdAsync(app, title);
        var picked = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{id}?tarih=2026-10-27"));
        Assert.Contains("27/10/2026", picked);
        Assert.Equal(new DateTime(2026, 10, 27), await app.WithDbAsync(db => db.Contracts.Where(c => c.Id == id).Select(c => c.DocumentDate.Date).SingleAsync())); // kayıt değişmez
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
            var design = new WindowDesign { DrawingList = new DrawingList { Name = "Test" }, Name = "Sürme Poz", WidthMm = 2200, HeightMm = 2250, Quantity = 2, LayoutJson = "{\"opening\":\"fixed\"}" };
            db.WindowDesigns.Add(design);
            await db.SaveChangesAsync();
            return design.Id;
        });
        var title = Unique("Ekli");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(500000, designId));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{await IdAsync(app, title)}"));
        Assert.Contains("Poz : 1 / Ad : 2 - Sürme Poz", html);
        Assert.Contains("Toplam çevre: 17.8 m", html.Replace(",", "."));
        Assert.Contains("Toplam alan: 9.90 m²", html.Replace(",", "."));
        Assert.Contains("Ünite adedi: 2", html);
        Assert.Contains("data-poz=", html);
        Assert.Contains("data-width-mm=\"160\"", html);
    }

    // Ekler Ercom'daki gibi tek poz listesidir: pozlar sırayla akar, toplamlar bir kez en sonda yazar.
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Attachments_PrintOneOrderedPozList_WithTotalsOnce(int count)
    {
        var (app, client, token, customer) = await SetupAsync();
        var ids = await app.WithDbAsync(async db =>
        {
            var designs = Enumerable.Range(1, count).Select(i => new WindowDesign { DrawingList = new DrawingList { Name = "Ek" }, Name = $"Poz {i}", WidthMm = 1000, HeightMm = 2000, Quantity = 1, LayoutJson = "{\"opening\":\"fixed\"}" }).ToList();
            db.WindowDesigns.AddRange(designs);
            await db.SaveChangesAsync();
            return designs.Select(x => x.Id).ToArray();
        });
        var title = Unique("Sayfa");
        await SaveAsync(client, token, "/yonetim/sozlesmeler/ekle", customer, title, ContentJson(500000, ids));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{await IdAsync(app, title)}"));
        var list = Assert.Single(html.Split("data-poz-list").Skip(1));
        Assert.Equal(count, list.Split("<figure class=\"poz\">").Length - 1);
        for (var i = 1; i <= count; i++) Assert.Contains($"Poz : {i} / Ad : 1 - Sabit", list);
        Assert.Single(html.Split("class=\"poz-totals").Skip(1));
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
    public async Task Save_ChangesTitleAndSignatory_LogoAndContactComeFromBranding()
    {
        await using var app = postgres.CreateApp("doc_template_save");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);

        var saved = await client.PostFormAsync("/yonetim/sablonlar/quote", token, ("Title", "YENİ TEKLİF"), ("Signatory", "Yetkili Kişi"), ("SignOff", "Saygılarımla,"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("+90 000 000 00 00"), "Phone" },
            { new StringContent("Yeni Adres"), "Address" },
            { new StringContent("Karakuş Test"), "CompanyName" },
            { new StringContent("16"), "LogoHeightMm" },
            { new StringContent("true"), "FooterAddress" }, { new StringContent("true"), "FooterPhone" },
            { new StringContent("logo"), "WatermarkMode" }, { new StringContent("6"), "WatermarkOpacity" }, { new StringContent("60"), "WatermarkSize" }, { new StringContent("-30"), "WatermarkAngle" },
            { new StringContent("#b0202a"), "AccentColor" },
            { new ByteArrayContent(TestImages.Png(40, 30)), "logoFile", "logo.png" }
        };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/yonetim/sablonlar/antet", form)).StatusCode);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/quote/onizleme"));
        Assert.Contains("YENİ TEKLİF", html);
        Assert.Contains("Yetkili Kişi", html);
        Assert.Contains("+90 000 000 00 00", html);
        Assert.Contains("Yeni Adres", html);
        Assert.Contains("/uploads/images/", html);
        Assert.DoesNotContain("/images/transparent.webp", html);
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
        Assert.Equal(DocumentSeed.ContractArticles().Count, DocumentJson.ParseArticles(template.ArticlesJson).Count);
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
                ALTER TABLE "Invoices" DROP COLUMN "ProjectId";
                ALTER TABLE "Expenses" DROP COLUMN "ProjectId";
                ALTER TABLE "StockMovements" DROP COLUMN IF EXISTS "ProductionOrderId", DROP COLUMN IF EXISTS "ServiceVisitId"; DROP TABLE IF EXISTS "WorkLogs", "ServiceParts", "ServiceVisits", "ServiceRequests", "ProductionScans", "DeliveryNoteLines", "DeliveryNotes", "OrderEvents", "ProductionOrders", "Installers";
                DROP TABLE "ProjectCostItems"; DROP TABLE "CostProjects";
                DROP TABLE "Contracts"; DROP TABLE "DocumentTemplates"; DROP TABLE "TemplateClauses";
                ALTER TABLE "Quotes" DROP COLUMN "DocumentDate", DROP COLUMN "VatMode", DROP COLUMN "DocumentJson", DROP COLUMN "SnapshotJson",
                  DROP COLUMN "DrawingListId", DROP COLUMN "GlassTypeId", DROP COLUMN "MarginPercent", DROP COLUMN "CostTotal";
                ALTER TABLE "QuoteItems" DROP COLUMN "BlockName", DROP COLUMN "SortOrder", DROP COLUMN "WindowDesignId", DROP COLUMN "UnitCost", DROP COLUMN "ManualBaseCost", DROP COLUMN "ManualGlassCost";
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

// Proforma teklif ve ayrıntılı sözleşme (ERP'lerdeki satış teklifi / proforma düzeni).
[Collection("postgres")]
public sealed class ProformaTests(PostgresFixture postgres)
{
    private static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    private static string Json(params (string Description, decimal Quantity, decimal Price, decimal Discount)[] items) => JsonSerializer.Serialize(new
    {
        terms = new[] { new { title = "Teslim süresi", text = "{{teslim_suresi}} iş günüdür." }, new { title = "", text = "" } },
        variables = new Dictionary<string, string> { ["teslim_suresi"] = "45" },
        blocks = new[] { new { name = "", clauses = Array.Empty<object>(), items = items.Select(i => new { description = i.Description, quantity = i.Quantity, unit = "Adet", unitPrice = i.Price, discountRate = i.Discount }) } }
    });

    [Fact]
    public async Task NewQuote_StartsWithTemplateTermsAndValues()
    {
        var (client, _) = await QuoteDocumentTests.AdminAsync(postgres.Shared);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/crm/teklifler/ekle"));
        Assert.Contains("\"terms\":[{\"title\":\"Fiyatlar\"", html);
        Assert.Contains("\"teslim_suresi\":\"30\"", html);
        Assert.Contains("id=\"doc-terms\"", html);
    }

    [Fact]
    public async Task Proforma_PrintsPartiesLinesDiscountsVatTermsAndApproval()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("Proforma");
        var saved = await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, Json(("Poz 1 - Açılır", 2, 1000m, 10m), ("Montaj", 1, 500m, 0m)), vat: "Added");
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().Include(q => q.Items).SingleAsync(q => q.Title == title));
        Assert.Equal(10m, quote.Items.Single(i => i.Description == "Poz 1 - Açılır").DiscountRate);
        Assert.Equal((2300m, 460m, 2760m), (quote.Subtotal, quote.TaxAmount, quote.GrandTotal));
        Assert.Single(DocumentJson.ParseContent(quote.DocumentJson).Terms); // boş şart kaydedilmez

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{quote.Id}"));
        Assert.Contains("PROFORMA FATURA", html);
        Assert.Contains("VD / VKN: Bornova / 1234567890", html); // alıcı vergi bilgisi müşteri kartından
        Assert.Contains(quote.QuoteNumber, html);
        Assert.Contains("<th class=\"pf-num\">İsk.</th>", html);
        Assert.Contains("%10", html);
        Assert.Contains("Brüt toplam</td><td>2.500,00 TL", html);
        Assert.Contains("Satır iskontoları</td><td>-200,00 TL", html);
        Assert.Contains("KDV matrahı</td><td>2.300,00 TL", html);
        Assert.Contains("KDV (%20)</td><td>460,00 TL", html);
        Assert.Contains("Genel toplam (KDV dahil)</td><td>2.760,00 TL", html);
        Assert.Contains("<dt>Teslim süresi</dt><dd>45 iş günüdür.</dd>", html);
        Assert.Contains("fatura yerine geçmez", html);
        Assert.Contains("Kaşe / İmza", html);
        Assert.DoesNotContain("maliyet", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_RejectsLineDiscountOutOfRange()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("İskonto");
        var response = await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, Json(("Kalem", 1, 100m, 120m)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Quotes.AnyAsync(q => q.Title == title)));
    }

    // Tekliften sözleşme: kalemler (iskontolarıyla), KDV, şartlar ve şablonun varsayılan değerleri taşınır; maliyet taşınmaz.
    [Fact]
    public async Task ContractFromQuote_CarriesLinesTermsAndVat_AndPrintsPriceTable()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("Sözleşmeye");
        await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, Json(("Poz 1 - Açılır", 2, 1000m, 10m), ("Montaj", 1, 500m, 0m)), vat: "Added");
        var quoteId = await app.WithDbAsync(db => db.Quotes.Where(q => q.Title == title).Select(q => q.Id).SingleAsync());
        await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quoteId).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuoteStatus.Accepted)));
        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quoteId}", token);

        var contract = await app.WithDbAsync(db => db.Contracts.AsNoTracking().SingleAsync(c => c.QuoteId == quoteId));
        Assert.Equal((VatMode.Added, 20m, 2760m), (contract.VatMode, contract.TaxRate, contract.TotalAmount));
        var content = DocumentJson.ParseContent(contract.DocumentJson);
        var line = content.Blocks.SelectMany(b => b.Items).Single(i => i.Description == "Poz 1 - Açılır");
        Assert.Equal((2m, 1000m, 10m, 0m), (line.Quantity, line.UnitPrice, line.DiscountRate, line.UnitCost));
        Assert.Equal("Teslim süresi", Assert.Single(content.Terms).Title);
        Assert.Equal(title, content.Subject); // konu teklifin başlığından
        Assert.Equal("45", content.Variables["teslim_suresi"]); // tekliften
        Assert.Equal("2", content.Variables["garanti_suresi"]); // sözleşme şablonundan
        // Ödeme planı avans oranından (%50) başlar ve bedele eşittir.
        Assert.Equal(new[] { 1380m, 1380m }, content.Payments.Select(p => p.Amount));
        Assert.Empty(PaymentPlan.Validate(content.Payments, contract.TotalAmount));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{contract.Id}"));
        Assert.Contains("İş Kalemleri ve Birim Fiyatlar", html);
        Assert.Contains("Genel toplam (KDV dahil)</td><td>2.760,00 TL", html);
        Assert.Contains("KDV Dahil 2.760,00 TL", html); // toplam bedel maddesi
        Assert.Contains("Özel Şartlar", html);
        Assert.Contains("<dt>Teslim süresi</dt><dd>45 iş günüdür.</dd>", html);
        Assert.Contains("Teslim ve Kabul", html);
        Assert.Contains("<strong>2 yıl</strong>", html); // garanti maddesi
        var count = DocumentSeed.ContractArticles().Count;
        Assert.Contains($"{count}. Yetkili Mahkeme", html);
        Assert.Contains($"{count} ({TurkishNumbers.CountWords(count)}) maddeden", html);
    }

    // Kalemli sözleşmede bedel kalemlerden hesaplanır; formdan gelen toplam yok sayılır.
    [Fact]
    public async Task ContractWithLines_ComputesTotalFromLines()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("Kalemli");
        var json = JsonSerializer.Serialize(new
        {
            blocks = new[] { new { name = "", clauses = Array.Empty<object>(), items = new[] { new { description = "Doğrama", quantity = 3, unit = "Adet", unitPrice = 1000m, discountRate = 0m, unitCost = 999m } } } },
            payments = new[] { new { kind = "cash", amount = 3000m, count = 1, text = "" } }
        });
        await client.PostFormAsync("/yonetim/sozlesmeler/ekle", token, ("CustomerId", customer.ToString()), ("Title", title), ("DocumentDate", "2026-11-19"), ("BuyerName", "Alıcı"),
            ("TotalAmount", "1"), ("VatMode", "Included"), ("TaxRate", "20"), ("DiscountRate", "0"), ("PageCount", "3"), ("DocumentJson", json));
        var contract = await app.WithDbAsync(db => db.Contracts.AsNoTracking().SingleAsync(c => c.Title == title));
        Assert.Equal(3000m, contract.TotalAmount);
        Assert.Equal(0m, DocumentJson.ParseContent(contract.DocumentJson).Blocks[0].Items[0].UnitCost); // sözleşmede maliyet tutulmaz
    }

    [Fact]
    public async Task InvoiceFromQuote_KeepsLineDiscountInUnitPrice()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("Faturaya");
        await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, Json(("Kalem", 2, 1000m, 10m)), vat: "Added");
        var quoteId = await app.WithDbAsync(db => db.Quotes.Where(q => q.Title == title).Select(q => q.Id).SingleAsync());
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/faturalar/ekle?teklif={quoteId}"));
        Assert.Matches("\"unitPrice\":900(\\.0+)?[,}]", html); // 1000 TL, %10 satır iskontosu
    }

    [Fact]
    public async Task TemplateSettings_SaveTermsAndDefaultValues_AndRejectBrokenValues()
    {
        await using var app = postgres.CreateApp("proforma_template_settings");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var ok = await client.PostFormAsync("/yonetim/sablonlar/quote", token, ("Title", "PROFORMA FATURA"), ("BankInfo", "Banka - IBAN TR00"), ("terms[0].Title", "Ödeme"), ("terms[0].Text", "Peşin"),
            ("terms[1].Title", ""), ("terms[1].Text", ""), ("variablesText", "teslim_suresi = 20\n\ngaranti_suresi = 3"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var template = await app.WithDbAsync(db => db.DocumentTemplates.AsNoTracking().SingleAsync(t => t.Kind == DocumentKinds.Quote));
        Assert.Equal("Ödeme", Assert.Single(DocumentJson.ParseTerms(template.TermsJson)).Title);
        Assert.Equal(new Dictionary<string, string> { ["teslim_suresi"] = "20", ["garanti_suresi"] = "3" }, DocumentJson.ParseVariables(template.VariablesJson));
        Assert.Equal("Banka - IBAN TR00", template.BankInfo);

        var bad = await client.PostFormAsync("/yonetim/sablonlar/quote", token, ("Title", "PROFORMA FATURA"), ("variablesText", "bozuk satır"));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
    }

    // Eski kurulum: şablonu hiç değiştirilmemişse proforma ve ayrıntılı maddelere yükselir; değiştirilmiş metin korunur.
    [Fact]
    public async Task Upgrade_RenewsUntouchedTemplates_KeepsEditedOnes()
    {
        await using var app = postgres.CreateApp("proforma_upgrade");
        app.NewClient();
        await app.WithDbAsync(async db =>
        {
            var quote = await db.DocumentTemplates.SingleAsync(t => t.Kind == DocumentKinds.Quote);
            (quote.Title, quote.Intro, quote.TermsJson, quote.AcceptanceText) = ("TEKLİFTİR", DocumentSeed.FirstQuoteIntro, "[]", null);
            var contract = await db.DocumentTemplates.SingleAsync(t => t.Kind == DocumentKinds.Contract);
            contract.ArticlesJson = DocumentJson.Serialize(DocumentSeed.FirstContractTitles.Select(t => new ContractArticle { Title = t, Kind = t == "Sözleşmenin Konusu" ? ContractArticleKinds.Subject : t == "İşin Toplam Bedeli" ? ContractArticleKinds.Price : t == "Ödeme Şekli" ? ContractArticleKinds.Payment : ContractArticleKinds.Text }).ToList());
            contract.VariablesJson = "{}";
            await db.SiteSettingRecords.Where(s => s.Key == KarakusAluminyum.Web.Data.DocumentTemplateUpgrade.MarkerKey).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
            await KarakusAluminyum.Web.Data.DocumentTemplateUpgrade.ApplyAsync(db);
            return 0;
        });
        var templates = await app.WithDbAsync(db => db.DocumentTemplates.AsNoTracking().ToDictionaryAsync(t => t.Kind));
        Assert.Equal((DocumentSeed.QuoteTitle, DocumentSeed.QuoteIntro), (templates[DocumentKinds.Quote].Title, templates[DocumentKinds.Quote].Intro));
        Assert.NotEmpty(DocumentJson.ParseTerms(templates[DocumentKinds.Quote].TermsJson));
        Assert.Equal(DocumentSeed.ContractArticles().Count, DocumentJson.ParseArticles(templates[DocumentKinds.Contract].ArticlesJson).Count);
        Assert.Equal("30", DocumentJson.ParseVariables(templates[DocumentKinds.Contract].VariablesJson)["teslim_suresi"]);

        // Yönetici başlığı ve maddeleri değiştirdiyse yükseltme dokunmaz.
        await app.WithDbAsync(async db =>
        {
            var quote = await db.DocumentTemplates.SingleAsync(t => t.Kind == DocumentKinds.Quote);
            quote.Title = "FİYAT TEKLİFİ";
            var contract = await db.DocumentTemplates.SingleAsync(t => t.Kind == DocumentKinds.Contract);
            contract.ArticlesJson = DocumentJson.Serialize(new List<ContractArticle> { new() { Title = "Kendi maddem" } });
            await db.SiteSettingRecords.Where(s => s.Key == KarakusAluminyum.Web.Data.DocumentTemplateUpgrade.MarkerKey).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
            await KarakusAluminyum.Web.Data.DocumentTemplateUpgrade.ApplyAsync(db);
            return 0;
        });
        templates = await app.WithDbAsync(db => db.DocumentTemplates.AsNoTracking().ToDictionaryAsync(t => t.Kind));
        Assert.Equal("FİYAT TEKLİFİ", templates[DocumentKinds.Quote].Title);
        Assert.Equal("Kendi maddem", Assert.Single(DocumentJson.ParseArticles(templates[DocumentKinds.Contract].ArticlesJson)).Title);
    }
}

// Belgedeki QR kodu: okutulunca giriş gerekmeden belgenin asıl hali açılır; bağlantı yenilenince eskisi kapanır.
[Collection("postgres")]
public sealed class DocumentQrTests(PostgresFixture postgres)
{
    private static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    private static string Token(string html, string kind)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, $"/belge/{kind}/([A-Za-z0-9_-]{{22}})");
        Assert.True(match.Success, "QR bağlantısı bulunamadı");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task QuoteQr_OpensOriginalWithoutLogin_AndRenewalClosesOldLink()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("QR");
        await QuoteDocumentTests.SaveQuoteAsync(client, token, "/yonetim/crm/teklifler/ekle", customer, title, QuoteDocumentTests.BlocksJson(("", new[] { ("Kalem", 1000m) })));
        var quote = await app.WithDbAsync(db => db.Quotes.AsNoTracking().SingleAsync(q => q.Title == title));

        var print = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{quote.Id}"));
        Assert.Contains("class=\"doc-qr\"", print);
        Assert.Contains("<svg", print);
        var link = Token(print, "teklif");
        Assert.Equal(link, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quote.Id).Select(q => q.PublicToken).SingleAsync()));
        Assert.Equal(link, Token(WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{quote.Id}")), "teklif")); // ikinci basımda aynı QR

        var anonymous = app.NewClient();
        var response = await anonymous.GetAsync($"/belge/teklif/{link}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(quote.QuoteNumber, html);
        Assert.Contains("Asıl belge", html);
        Assert.Contains("Durum: Taslak", html);
        Assert.DoesNotContain("maliyet", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/yonetim/crm/teklifler/yazdir", html); // panel bağlantısı yok

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/belge/teklif/AAAAAAAAAAAAAAAAAAAAAA")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/belge/teklif/kisa")).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/crm/teklifler/baglantiyi-yenile/{quote.Id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/belge/teklif/{link}")).StatusCode);
        var renewed = await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == quote.Id).Select(q => q.PublicToken).SingleAsync());
        Assert.NotEqual(link, renewed);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/belge/teklif/{renewed}")).StatusCode);
    }

    [Fact]
    public async Task ContractQr_OpensOriginalWithoutLogin()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var title = Unique("QR sözleşme");
        var json = JsonSerializer.Serialize(new { subject = "deneme işi", blocks = new[] { new { name = "", clauses = Array.Empty<object>(), items = Array.Empty<object>() } }, payments = Array.Empty<object>() });
        await client.PostFormAsync("/yonetim/sozlesmeler/ekle", token, ("CustomerId", customer.ToString()), ("Title", title), ("DocumentDate", "2026-11-19"), ("BuyerName", "QR Alıcı"),
            ("TotalAmount", "1000"), ("VatMode", "Included"), ("TaxRate", "20"), ("DiscountRate", "0"), ("PageCount", "1"), ("DocumentJson", json));
        var id = await app.WithDbAsync(db => db.Contracts.Where(c => c.Title == title).Select(c => c.Id).SingleAsync());

        Assert.Contains("/belge/sozlesme/", await client.GetStringAsync($"/yonetim/sozlesmeler/detay/{id}"));
        var link = Token(WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/sozlesmeler/yazdir/{id}")), "sozlesme");
        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/belge/sozlesme/{link}"));
        Assert.Contains("SÖZLEŞMEDİR", html);
        Assert.Contains("QR Alıcı", html);
        Assert.Contains("Durum: Taslak", html);
    }

    [Fact]
    public async Task DrawingQr_ShowsPozListOnly_WithoutMaterialsOrPrices()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var list = Unique("QR liste");
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Salon", width: 1200, height: 1400, quantity: 3)));
        var listId = await DrawingLists.ListIdAsync(app, list);

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{listId}"));
        Assert.Contains("class=\"doc-qr\"", details);
        var link = Token(details, "cizim");
        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/belge/cizim/{link}"));
        Assert.Contains("Poz : 1 / Ad : 3 - Salon", html);
        Assert.Contains("data-poz=", html);
        Assert.DoesNotContain("Malzeme listesi", html);
        Assert.DoesNotContain("Maliyet", html);
        Assert.Contains("noindex", html);
    }

    [Fact]
    public async Task Robots_ClosesDocumentLinks()
        => Assert.Contains("Disallow: /belge/", await postgres.Shared.NewClient().GetStringAsync("/robots.txt"));
}

// Ortak antet: logo, iletişim, alt bilgi, sayfa numarası ve filigran tüm belgelerde aynı ayardan gelir.
[Collection("postgres")]
public sealed class BrandingTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, params (string, string)[] extra)
    {
        var fields = new Dictionary<string, string>
        {
            ["CompanyName"] = "Karakuş Alüminyum", ["Slogan"] = "Doğrama sistemleri", ["Phone"] = "+90 553 000 00 01", ["Phone2"] = "0232 000 00 02", ["WhatsApp"] = "+90 553 000 00 03",
            ["Email"] = "info@ornek.test", ["Website"] = "www.ornek.test", ["Instagram"] = "@ornek", ["TaxInfo"] = "Bornova VD 123", ["Address"] = "Karacaoğlan Mah. Bornova",
            ["FooterNote"] = "TSE belgeli üretim", ["LogoHeightMm"] = "18", ["AccentColor"] = "#123456", ["HeaderContact"] = "true", ["FooterAddress"] = "true", ["FooterPhone"] = "true",
            ["FooterEmail"] = "true", ["FooterWebsite"] = "true", ["FooterSocial"] = "true", ["FooterTaxInfo"] = "true", ["PageNumbers"] = "true",
            ["WatermarkMode"] = "text", ["WatermarkText"] = "ASIL NÜSHA", ["WatermarkOpacity"] = "7", ["WatermarkSize"] = "50", ["WatermarkAngle"] = "-25"
        };
        foreach (var (key, value) in extra) fields[key] = value;
        return client.PostFormAsync("/yonetim/sablonlar/antet", token, fields.Select(f => (f.Key, f.Value)).ToArray());
    }

    [Fact]
    public async Task Branding_AppearsOnQuoteContractDrawingAndOrderPrints()
    {
        await using var app = postgres.CreateApp("branding_everywhere");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        Assert.Contains("Antet ve filigran", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar")));
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token)).StatusCode);

        var list = "Antet listesi";
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Salon")));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Antet siparişi"), ("DrawingListId", listId.ToString()), ("OrderDate", "2026-11-19"));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Antet siparişi").Select(o => o.Id).SingleAsync());

        foreach (var url in new[] { "/yonetim/sablonlar/quote/onizleme", "/yonetim/sablonlar/contract/onizleme", $"/yonetim/cizim/detay/{listId}", $"/yonetim/siparisler/is-emri/{orderId}", $"/yonetim/siparisler/teslim-tutanagi/{orderId}" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
            Assert.Contains("class=\"doc-head\"", html);
            Assert.Contains("Doğrama sistemleri", html); // slogan
            Assert.Contains("<div>+90 553 000 00 01</div>", html); // üst bilgide iletişim
            Assert.Contains("<span>Tel: 0232 000 00 02</span>", html); // alt bilgi
            Assert.Contains("<span>WhatsApp: +90 553 000 00 03</span>", html);
            Assert.Contains("<span>Instagram: @ornek</span>", html);
            Assert.Contains("VD / VKN: Bornova VD 123", html);
            Assert.Contains("TSE belgeli üretim", html);
            Assert.Contains("class=\"doc-watermark\"", html);
            Assert.Contains("ASIL NÜSHA", html);
            Assert.Contains("rotate(-25deg)", html);
            Assert.Contains("counter(pages)", html); // sayfa numarası
            Assert.Contains("--doc-accent:#123456", html);
        }
    }

    [Fact]
    public async Task Branding_CanHideParts_AndRejectsBadValues()
    {
        await using var app = postgres.CreateApp("branding_options");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        await SaveAsync(client, token, ("HeaderContact", "false"), ("FooterSocial", "false"), ("PageNumbers", "false"), ("WatermarkMode", "none"));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/quote/onizleme"));
        Assert.DoesNotContain("doc-brand__contact", html);
        Assert.DoesNotContain("Instagram:", html);
        Assert.DoesNotContain("counter(pages)", html);
        Assert.DoesNotContain("class=\"doc-watermark\"", html);

        foreach (var bad in new[] { ("WatermarkOpacity", "80"), ("WatermarkMode", "resim"), ("AccentColor", "kırmızı"), ("LogoHeightMm", "3") })
            Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, bad)).StatusCode);
        // Logo kaldırılırsa logo filigranı seçilemez; yerine firma adı yazılır.
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, ("removeLogo", "true"), ("WatermarkMode", "logo"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token, ("removeLogo", "true"), ("WatermarkMode", "none"))).StatusCode);
        Assert.Contains("class=\"doc-brand__name\">Karakuş Alüminyum", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/quote/onizleme")));
    }

    // Kaydedilmemiş kurulum: mevcut şablondaki logo ve telefonla başlar, belgeler boş antetle basılmaz.
    [Fact]
    public async Task UnsavedBranding_StartsFromExistingTemplate()
    {
        await using var app = postgres.CreateApp("branding_defaults");
        var (client, _) = await QuoteDocumentTests.AdminAsync(app);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sablonlar/quote/onizleme"));
        Assert.Contains("/images/transparent.webp", html);
        Assert.Contains("+90 553 835 48 72", html);
        Assert.Contains("class=\"doc-watermark\"", html); // varsayılan: logo filigranı
    }
}
