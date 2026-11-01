using System.Net;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class ClientReferenceTests(PostgresFixture postgres)
{
    private static string Tag() => "R" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail, AppFactory.Password);
        return (client, await client.GetTokenAsync("/yonetim/referanslar/ekle"));
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, string name, string? website, byte[]? logo, bool published = true, int order = 0)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(name), "Name" }, { new StringContent(website ?? ""), "WebsiteUrl" }, { new StringContent(order.ToString()), "SortOrder" }, { new StringContent(published ? "true" : "false"), "IsPublished" } };
        if (logo is not null) form.Add(new ByteArrayContent(logo), "logoFile", "logo.png");
        return client.PostAsync(url, form);
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string token, string name, string? website = "https://www.ornek.com", bool published = true, int order = 0) =>
        SaveAsync(client, token, "/yonetim/referanslar/ekle", name, website, TestImages.Png(800, 400, transparent: true), published, order);

    [Theory]
    [InlineData("ornek.com", "https://ornek.com/")]
    [InlineData("www.ornek.com.tr/hakkimizda", "https://www.ornek.com.tr/hakkimizda")]
    [InlineData("http://ornek.com", "http://ornek.com/")]
    [InlineData(" https://ornek.com/ ", "https://ornek.com/")]
    public void WebsiteUrl_IsNormalized(string input, string expected)
    {
        Assert.True(ClientReference.TryNormalizeUrl(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://ornek.com")]
    [InlineData("https://kullanici@ornek.com")]
    [InlineData("sadecemetin")]
    [InlineData("https://")]
    public void WebsiteUrl_RejectsUnsafeOrInvalid(string input) => Assert.False(ClientReference.TryNormalizeUrl(input, out _));

    [Fact]
    public void WebsiteUrl_IsOptional()
    {
        Assert.True(ClientReference.TryNormalizeUrl("  ", out var url));
        Assert.Null(url);
    }

    [Fact]
    public async Task Section_IsHiddenUntilPublished_ThenLinksOpenInNewTab()
    {
        await using var app = postgres.CreateApp("references_public");
        var (client, token) = await AdminAsync(app);
        var visitor = app.NewClient();
        Assert.DoesNotContain("Referanslarımız", WebUtility.HtmlDecode(await visitor.GetStringAsync("/")));

        var draft = Tag();
        await AddAsync(client, token, draft, published: false);
        Assert.DoesNotContain(draft, await visitor.GetStringAsync("/"));

        var name = Tag();
        await AddAsync(client, token, name, "ornek-firma.com");
        var row = await app.WithDbAsync(db => db.ClientReferences.AsNoTracking().SingleAsync(x => x.Name == name));
        Assert.Equal("https://ornek-firma.com/", row.WebsiteUrl);
        Assert.StartsWith("/uploads/referanslar/", row.LogoPath);
        Assert.EndsWith(".webp", row.LogoPath);

        foreach (var page in new[] { "/", "/kurumsal" })
        {
            var html = WebUtility.HtmlDecode(await visitor.GetStringAsync(page));
            Assert.Contains("Referanslarımız", html);
            Assert.Contains("<a class=\"references__item\" href=\"https://ornek-firma.com/\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
            Assert.Contains($"alt=\"{name}\"", html);
            Assert.DoesNotContain("references__viewport--animated", html); // tek logo kaymaz
        }
    }

    [Fact]
    public async Task HomePage_ShowsReferencesRightAfterHero_BeforeServices()
    {
        await using var app = postgres.CreateApp("references_order");
        var (client, token) = await AdminAsync(app);
        await AddAsync(client, token, Tag());
        var html = await app.NewClient().GetStringAsync("/");
        var hero = html.IndexOf("class=\"hero\"", StringComparison.Ordinal);
        var references = html.IndexOf("class=\"section references\"", StringComparison.Ordinal);
        var services = html.IndexOf("class=\"mosaic", StringComparison.Ordinal);
        Assert.True(hero < references && references < services, $"{hero} {references} {services}");
    }

    [Fact]
    public async Task ManyLogos_Scroll_AndCopiesAreHiddenFromKeyboardAndScreenReaders()
    {
        await using var app = postgres.CreateApp("references_marquee");
        var (client, token) = await AdminAsync(app);
        var names = Enumerable.Range(0, 5).Select(_ => Tag()).ToList();
        for (var i = 0; i < names.Count; i++) await AddAsync(client, token, names[i], order: i);

        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync("/"));
        Assert.Contains("references__viewport--animated", html);
        foreach (var name in names) Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, $"alt=\"{name}\""));
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(html, "<a class=\"references__item\"[^>]*aria-label=").Count);
        // Şerit 12 logoya tamamlanır: 3 tur x 2 kopya = 30 bağlantı, 5'i gerçek.
        Assert.Equal(25, System.Text.RegularExpressions.Regex.Matches(html, "<a class=\"references__item\"[^>]*tabindex=\"-1\"").Count);
    }

    [Fact]
    public async Task LogoWithoutWebsite_IsNotALink()
    {
        await using var app = postgres.CreateApp("references_nolink");
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        await AddAsync(client, token, name, website: "");
        var html = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync("/"));
        Assert.Contains($"<span class=\"references__item\"", html);
        Assert.DoesNotContain("<a class=\"references__item\"", html);
    }

    [Fact]
    public async Task InvalidInput_IsRejected()
    {
        await using var app = postgres.CreateApp("references_invalid");
        var (client, token) = await AdminAsync(app);
        var logo = TestImages.Png(200, 100);
        foreach (var (name, website, file) in new (string, string, byte[]?)[] { ("", "ornek.com", logo), (Tag(), "javascript:alert(1)", logo), (Tag(), "ornek.com", null), (Tag(), "ornek.com", "<svg/>"u8.ToArray()) })
            Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, token, "/yonetim/referanslar/ekle", name, website, file)).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.ClientReferences.AnyAsync()));
    }

    [Fact]
    public async Task EditAndDelete_ReplaceAndRemoveLogoFiles()
    {
        await using var app = postgres.CreateApp("references_edit");
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        await AddAsync(client, token, name);
        var row = await app.WithDbAsync(db => db.ClientReferences.AsNoTracking().SingleAsync(x => x.Name == name));
        var first = Path.Combine(app.WebRoot, row.LogoPath!.TrimStart('/'));
        Assert.True(File.Exists(first));

        // Logo seçilmezse eskisi kalır.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(client, token, $"/yonetim/referanslar/duzenle/{row.Id}", name + "x", "yeni.com", null)).StatusCode);
        var kept = await app.WithDbAsync(db => db.ClientReferences.AsNoTracking().SingleAsync(x => x.Id == row.Id));
        Assert.Equal((row.LogoPath, name + "x", "https://yeni.com/"), (kept.LogoPath, kept.Name, kept.WebsiteUrl));

        await SaveAsync(client, token, $"/yonetim/referanslar/duzenle/{row.Id}", name, "yeni.com", TestImages.Png(300, 100));
        var replaced = await app.WithDbAsync(db => db.ClientReferences.AsNoTracking().SingleAsync(x => x.Id == row.Id));
        Assert.NotEqual(row.LogoPath, replaced.LogoPath);
        Assert.False(File.Exists(first));

        await client.PostFormAsync($"/yonetim/referanslar/sil/{row.Id}", token);
        Assert.False(File.Exists(Path.Combine(app.WebRoot, replaced.LogoPath!.TrimStart('/'))));
        Assert.False(await app.WithDbAsync(db => db.ClientReferences.AnyAsync(x => x.Id == row.Id)));
    }

    [Fact]
    public async Task Logo_IsDownsizedForFastLoading()
    {
        await using var app = postgres.CreateApp("references_size");
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        await SaveAsync(client, token, "/yonetim/referanslar/ekle", name, "ornek.com", TestImages.Png(3000, 1500, transparent: true));
        var path = await app.WithDbAsync(db => db.ClientReferences.Where(x => x.Name == name).Select(x => x.LogoPath!).SingleAsync());
        using var bitmap = SKBitmap.Decode(Path.Combine(app.WebRoot, path.TrimStart('/')));
        Assert.Equal(512, Math.Max(bitmap.Width, bitmap.Height));
    }

    [Fact]
    public async Task Panel_RequiresAdminOrEditor()
    {
        var anonymous = await postgres.Shared.NewClient().GetAsync("/yonetim/referanslar");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("yonetim-giris", anonymous.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingDatabase_GetsReferencesTable()
    {
        await using (var first = postgres.CreateApp("references_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"ClientReferences\";"));
        }
        await using var second = postgres.CreateApp("references_upgrade", schemaMode: "existing");
        Assert.Equal(HttpStatusCode.OK, (await second.NewClient().GetAsync("/")).StatusCode);
        Assert.False(await second.WithDbAsync(db => db.ClientReferences.AnyAsync()));
    }
}
