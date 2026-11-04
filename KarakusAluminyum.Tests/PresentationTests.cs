using System.Net;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class PresentationSeedTests
{
    [Fact]
    public void Seed_FollowsCorporateOrder_WithCoverFirstAndContactLast()
    {
        var sections = PresentationSeed.Sections();
        Assert.Equal(PresentationKinds.Cover, sections[0].Kind);
        Assert.Equal(PresentationKinds.Contents, sections[1].Kind);
        Assert.Equal(PresentationKinds.Contact, sections[^1].Kind);
        Assert.Equal(sections.Count, sections.Select(s => s.SortOrder).Distinct().Count());
        Assert.All(sections, s => Assert.Contains(s.Kind, PresentationKinds.All));
        Assert.All(sections, s => Assert.True(s.Title.Length <= 160 && (s.Body?.Length ?? 0) <= 1500));
    }

    [Fact]
    public void Contents_UseShortEyebrow_FallingBackToTitle()
    {
        Assert.Equal("Hakkımızda", PresentationBuilder.ContentsTitle(new PresentationSection { Eyebrow = " Hakkımızda ", Title = "Uzun başlık" }));
        Assert.Equal("Uzun başlık", PresentationBuilder.ContentsTitle(new PresentationSection { Eyebrow = " ", Title = "Uzun başlık" }));
    }
}

[Collection("postgres")]
public sealed class PresentationTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/sunum"));
    }

    [Fact]
    public async Task DefaultPresentation_IsSeeded_AndRendersCustomerOnCover()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        Assert.True(await app.WithDbAsync(db => db.PresentationSections.AnyAsync(x => x.Kind == PresentationKinds.Cover)));

        var html = await client.GetStringAsync("/yonetim/sunum/goruntule?firma=" + Uri.EscapeDataString("Örnek <Yapı> A.Ş."));
        Assert.Contains("class=\"pres-slide pres-cover\"", html);
        Assert.Contains("Örnek &lt;Yapı&gt; A.Ş.", html);
        Assert.DoesNotContain("<Yapı>", html);
        Assert.Contains(SiteSettings.FoundedYear.ToString(), html);
        Assert.DoesNotContain("{{", html); // {{kurulus_yili}} gibi alanlar doldu
        Assert.DoesNotContain(TemplateText.Blank, html);
        Assert.DoesNotContain("class=\"pres pres--portrait\"", html);
        Assert.Contains("class=\"pres pres--portrait\"", await client.GetStringAsync("/yonetim/sunum/goruntule?yon=dikey"));
    }

    [Fact]
    public async Task AutomaticSections_AreSkipped_WhenTheirListIsEmpty_AndContentsNumberPages()
    {
        await using var app = postgres.CreateApp("pres_empty");
        var (client, _) = await AdminAsync(app);
        await app.WithDbAsync(async db => { await db.ClientReferences.ExecuteDeleteAsync(); return 0; });

        var vm = await app.WithServiceAsync<PresentationBuilder, PresentationVm>(b => b.BuildAsync(new PresentationCover(null, null, null, DateTime.UtcNow)));
        Assert.DoesNotContain(vm.Pages, p => p.Section.Kind == PresentationKinds.References);
        var services = vm.Pages.Where(p => p.Section.Kind == PresentationKinds.Services).ToList();
        Assert.All(services, p => Assert.True(p.Services.Count <= PresentationBuilder.ServicesPerPage));
        Assert.Equal(services.Count, services[0].Parts);
        Assert.Equal(Enumerable.Range(1, vm.PageCount), vm.Pages.Select(p => p.Number));
        // Bölümün ilk sayfası listelenir; kapak ve içindekiler listelenmez.
        Assert.Equal(services[0].Number, vm.Contents.Single(c => c.Title == services[0].Section.Eyebrow).Page);
        Assert.DoesNotContain(vm.Contents, c => c.Page <= 2);

        var html = await client.GetStringAsync("/yonetim/sunum/goruntule");
        Assert.DoesNotContain("pres-logos", html);
    }

    [Fact]
    public async Task Sections_CanBeEdited_Reordered_Toggled_AndValidated()
    {
        await using var app = postgres.CreateApp("pres_edit");
        var (client, token) = await AdminAsync(app);
        var ids = await app.WithDbAsync(db => db.PresentationSections.OrderBy(x => x.SortOrder).Select(x => x.Id).ToListAsync());
        var about = await app.WithDbAsync(db => db.PresentationSections.FirstAsync(x => x.Kind == PresentationKinds.About));

        var saved = await client.PostFormAsync($"/yonetim/sunum/duzenle/{about.Id}", token,
            ("Kind", "cover"), ("Eyebrow", "Biz kimiz"), ("Title", "Yeni başlık"), ("Body", "Metin"), ("IsActive", "true"),
            ("items[0].Title", "Madde"), ("items[0].Text", "Açıklama"), ("items[1].Title", ""), ("items[1].Text", " "));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var row = await app.WithDbAsync(db => db.PresentationSections.AsNoTracking().FirstAsync(x => x.Id == about.Id));
        Assert.Equal(PresentationKinds.About, row.Kind); // tür formdan değiştirilemez
        Assert.Equal("Yeni başlık", row.Title);
        Assert.Single(DocumentJson.ParseItems(row.ItemsJson)); // boş madde atılır

        var tooMany = new List<(string, string)> { ("Title", "Başlık"), ("IsActive", "true") };
        for (var i = 0; i <= PresentationSection.MaxItems; i++) tooMany.Add(($"items[{i}].Title", "M" + i));
        var invalid = await client.PostFormAsync($"/yonetim/sunum/duzenle/{about.Id}", token, tooMany.ToArray());
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains($"En fazla {PresentationSection.MaxItems} madde", await invalid.Content.ReadAsStringAsync());

        await client.PostFormAsync($"/yonetim/sunum/tasi/{ids[1]}", token, ("direction", "-1"));
        var order = await app.WithDbAsync(db => db.PresentationSections.OrderBy(x => x.SortOrder).Select(x => x.Id).ToListAsync());
        Assert.Equal(new[] { ids[1], ids[0] }, order.Take(2));
        Assert.Equal(Enumerable.Range(0, order.Count), await app.WithDbAsync(db => db.PresentationSections.OrderBy(x => x.SortOrder).Select(x => x.SortOrder).ToListAsync()));

        await client.PostFormAsync($"/yonetim/sunum/durum/{about.Id}", token);
        var html = await client.GetStringAsync("/yonetim/sunum/goruntule");
        Assert.DoesNotContain("Yeni başlık", html);

        var created = await client.PostFormAsync("/yonetim/sunum/ekle", token, ("Kind", PresentationKinds.Text), ("Title", "Sertifikalarımız"), ("IsActive", "true"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(PresentationKinds.Contact, await app.WithDbAsync(db => db.PresentationSections.OrderByDescending(x => x.SortOrder).Skip(1).Select(x => x.Kind).FirstAsync()));
        Assert.Contains("Sertifikalarımız", await client.GetStringAsync("/yonetim/sunum/goruntule"));

        var badKind = await client.PostFormAsync("/yonetim/sunum/ekle", token, ("Kind", "script"), ("Title", "X"));
        Assert.Equal(HttpStatusCode.OK, badKind.StatusCode);

        await client.PostFormAsync("/yonetim/sunum/varsayilan", token);
        Assert.Equal(PresentationSeed.Sections().Count, await app.WithDbAsync(db => db.PresentationSections.CountAsync()));
    }

    [Fact]
    public async Task QuotePrint_AddsPresentationOnlyWhenRequested_WithQuoteOnCover()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        var number = "SN-" + Guid.NewGuid().ToString("N")[..8];
        var id = await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = "Sunum Müşteri" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var quote = new Quote { QuoteNumber = number, Title = "Cephe işleri", CustomerId = customer.Id, CreatedUtc = DateTime.UtcNow, DocumentDate = DateTime.UtcNow };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });

        var plain = await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{id}");
        Assert.DoesNotContain("pres-slide", plain);
        Assert.Contains("Kurumsal sunumu ekle", plain);

        var withDeck = await client.GetStringAsync($"/yonetim/crm/teklifler/yazdir/{id}?sunum=1");
        Assert.Contains("class=\"pres pres--portrait\"", withDeck); // teklifle her zaman dikey
        Assert.Contains(number, withDeck);
        Assert.Contains("Cephe işleri", withDeck);
        Assert.True(withDeck.IndexOf("class=\"pres-slide pres-cover\"", StringComparison.Ordinal) < withDeck.IndexOf("<table class=\"doc-sheet\"", StringComparison.Ordinal));

        Assert.Contains("Sunumla PDF", await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{id}"));
    }

    [Fact]
    public async Task Presentation_RequiresStaffRole()
    {
        var anonymous = postgres.Shared.NewClient();
        var response = await anonymous.GetAsync("/yonetim/sunum/goruntule");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
