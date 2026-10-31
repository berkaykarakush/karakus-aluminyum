using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Seo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class SeoTests(PostgresFixture postgres)
{
    private const string Domain = "https://www.karakusaluminyum.com";
    private static string Tag() => "s" + Guid.NewGuid().ToString("N")[..8];
    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static async Task<Dictionary<string, XElement>> SitemapAsync(HttpClient client)
    {
        var xml = XDocument.Parse(await client.GetStringAsync("/sitemap.xml"));
        return xml.Root!.Elements(SitemapNs + "url").ToDictionary(x => x.Element(SitemapNs + "loc")!.Value);
    }

    private static List<JsonElement> Graph(string html)
    {
        var scripts = Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline);
        Assert.Single(scripts);
        using var json = JsonDocument.Parse(scripts[0].Groups[1].Value);
        Assert.Equal("https://schema.org", json.RootElement.GetProperty("@context").GetString());
        return json.RootElement.GetProperty("@graph").EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static JsonElement Node(IEnumerable<JsonElement> graph, string type) => Assert.Single(graph, x => x.GetProperty("@type").GetString() == type);

    private static Task<int> AddAsync<T>(AppFactory app, T row, Func<T, int> id) where T : class => app.WithDbAsync(async db =>
    {
        db.Add(row);
        await db.SaveChangesAsync();
        return id(row);
    });

    // Sitede bağlantı verilen her sayfa dolaşılır. Yeni bir sayfa türü eklenip dizine girmezse bu test başarısız olur.
    [Fact]
    public async Task EveryLinkedPublicPage_IsInSitemap_AndHasCompleteMetadata()
    {
        await using var app = postgres.CreateApp("seo_crawl");
        var client = app.NewClient();
        await AddAsync(app, new BlogPost { Title = "Tarama yazısı", Slug = "tarama-yazisi", Summary = "Tarama testi için yeterince uzun özet", Content = "İçerik", IsPublished = true, PublishedUtc = DateTime.UtcNow }, x => x.Id);
        var sitemap = await SitemapAsync(client);
        Assert.True(sitemap.Count > 10);

        var queue = new Queue<string>(sitemap.Keys.Select(x => x[Domain.Length..] is "" ? "/" : x[Domain.Length..]));
        queue.Enqueue("/");
        var seen = new HashSet<string>();
        var problems = new List<string>();
        while (queue.TryDequeue(out var path))
        {
            if (!seen.Add(path)) continue;
            var response = await client.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.OK) { problems.Add($"{path}: {(int)response.StatusCode}"); continue; }
            var html = await response.Content.ReadAsStringAsync();
            var noindex = Regex.IsMatch(html, "<meta name=\"robots\" content=\"[^\"]*noindex");
            foreach (Match link in Regex.Matches(html, "href=\"(/[^\"#?]*)"))
            {
                var target = link.Groups[1].Value;
                if (!Regex.IsMatch(target, "\\.[a-z0-9]{2,5}$", RegexOptions.IgnoreCase) && !target.StartsWith("/yonetim")) queue.Enqueue(target);
            }
            if (noindex) { if (sitemap.ContainsKey(Domain + path)) problems.Add($"{path}: noindex ama site haritasında"); continue; }

            if (!sitemap.ContainsKey(Domain + (path == "/" ? "/" : path))) problems.Add($"{path}: site haritasında yok");
            if (Regex.Matches(html, "<h1[ >]").Count != 1) problems.Add($"{path}: tek h1 yok");
            if (!Regex.IsMatch(html, "<title>[^<]{5,}</title>")) problems.Add($"{path}: başlık yok");
            if (!Regex.IsMatch(html, "<meta name=\"description\" content=\"[^\"]{10,}\"")) problems.Add($"{path}: açıklama yok");
            if (!html.Contains($"<link rel=\"canonical\" href=\"{Domain}{(path == "/" ? "/" : path)}\"")) problems.Add($"{path}: canonical yanlış");
            if (!html.Contains("property=\"og:image\" content=\"https://")) problems.Add($"{path}: og:image yok");
            if (Regex.IsMatch(html, "<img(?![^>]*\\balt=\")[^>]*>")) problems.Add($"{path}: alt metinsiz görsel");
            if (Graph(html).All(x => x.GetProperty("@id").GetString() != Domain + "/#organization")) problems.Add($"{path}: işletme şeması yok");
            var markdown = Regex.Match(html, "<link rel=\"alternate\" type=\"text/markdown\" href=\"([^\"]+)\"").Groups[1].Value;
            if (markdown.Length == 0 || (await client.GetAsync(markdown)).StatusCode != HttpStatusCode.OK) problems.Add($"{path}: Markdown yok ({markdown})");
        }
        Assert.True(seen.Count > 10);
        Assert.Empty(problems);
    }

    [Fact]
    public async Task NewContent_AppearsEverywhereAutomatically_AndLeavesWhenUnpublished()
    {
        await using var app = postgres.CreateApp("seo_dynamic");
        var client = app.NewClient();
        var slug = Tag();
        var secret = Tag();
        var id = await AddAsync(app, new BlogPost { Title = "Yeni yazı " + slug, Slug = slug, Summary = "Kısa özet", Content = "Yazının gövdesi " + slug, IsPublished = true, PublishedUtc = DateTime.UtcNow, UpdatedUtc = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc) }, x => x.Id);
        await AddAsync(app, new BlogPost { Title = "Taslak " + secret, Slug = secret, Summary = "x", Content = "Gizli " + secret, IsPublished = false }, x => x.Id);

        var url = await SitemapAsync(client);
        Assert.Equal("2026-05-06T07:08:09Z", url[$"{Domain}/blog/{slug}"].Element(SitemapNs + "lastmod")!.Value);
        Assert.Contains($"{Domain}/blog/{slug}.md", await client.GetStringAsync("/llms.txt"));
        var full = await client.GetStringAsync("/llms-full.txt");
        Assert.Contains("Yazının gövdesi " + slug, full);
        Assert.Contains("Yazının gövdesi " + slug, await client.GetStringAsync($"/blog/{slug}.md"));
        Assert.Contains($"/blog/{slug}", await client.GetStringAsync("/blog.md"));
        Assert.Contains(Domain + $"/blog/{slug}", await client.GetStringAsync("/blog.md")); // bağlantılar tam adrestir

        foreach (var text in new[] { await client.GetStringAsync("/llms.txt"), full, await client.GetStringAsync("/sitemap.xml") }) Assert.DoesNotContain(secret, text);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/blog/{secret}.md")).StatusCode);

        await app.WithDbAsync(async db =>
        {
            (await db.BlogPosts.SingleAsync(x => x.Id == id)).IsPublished = false;
            return await db.SaveChangesAsync();
        });
        Assert.False((await SitemapAsync(client)).ContainsKey($"{Domain}/blog/{slug}"));

        var hidden = await app.WithDbAsync(async db =>
        {
            var service = await db.Services.FirstAsync(x => x.IsPublished);
            service.IsPublished = false;
            await db.SaveChangesAsync();
            return service.Slug;
        });
        Assert.False((await SitemapAsync(client)).ContainsKey($"{Domain}/urunler/{hidden}"));
        Assert.DoesNotContain($"/urunler/{hidden}", await client.GetStringAsync("/llms.txt"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/urunler/{hidden}.md")).StatusCode);
        Assert.DoesNotContain(slug, await client.GetStringAsync("/llms-full.txt"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/blog/{slug}.md")).StatusCode);
    }

    [Fact]
    public async Task ContentCreatedInThePanel_IsPublishedToDiscoveryFiles()
    {
        await using var app = postgres.CreateApp("seo_panel");
        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        var token = await admin.GetTokenAsync("/yonetim/projeler/ekle");
        var slug = Tag();
        await admin.PostFormAsync("/yonetim/projeler/ekle", token, ("Title", "Panel projesi"), ("Slug", slug), ("Type", "Konut"), ("Summary", "Panelden eklendi"), ("Location", "Bornova"), ("IsPublished", "true"));

        var client = app.NewClient();
        Assert.True((await SitemapAsync(client)).ContainsKey($"{Domain}/projeler/{slug}"));
        Assert.Contains("Panel projesi", await client.GetStringAsync("/llms.txt"));
        var node = Node(Graph(await client.GetStringAsync($"/projeler/{slug}")), "CreativeWork");
        Assert.Equal("Panel projesi", node.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Schema_MatchesEachPageType_WithAbsoluteUrls()
    {
        await using var app = postgres.CreateApp("seo_schema");
        var client = app.NewClient();
        var service = await app.WithDbAsync(db => db.Services.FirstAsync(x => x.IsPublished));
        await AddAsync(app, new FaqItem { ServiceId = service.Id, Question = "Hizmet sorusu?", Answer = "Hizmet cevabı." }, x => x.Id);
        var post = Tag();
        var published = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await AddAsync(app, new BlogPost { Title = "Başlık", Slug = post, Summary = "Özet", Content = "Gövde", IsPublished = true, PublishedUtc = published }, x => x.Id);
        await AddAsync(app, new GalleryImage { ImagePath = "/images/sema-testi.png", Title = "Görsel adı" }, x => x.Id);

        var home = await client.GetStringAsync("/");
        var graph = Graph(home);
        Assert.Equal(Domain + "/", Node(graph, "WebSite").GetProperty("url").GetString());
        var business = Node(graph, "HomeAndConstructionBusiness");
        Assert.Equal(Domain + "/images/transparent.png", business.GetProperty("logo").GetString());
        Assert.False(business.TryGetProperty("@context", out _));
        Assert.DoesNotContain(graph, x => x.GetProperty("@type").GetString() == "BreadcrumbList");

        var detail = Graph(await client.GetStringAsync($"/urunler/{service.Slug}"));
        Assert.Equal(Domain + "/#organization", Node(detail, "Service").GetProperty("provider").GetProperty("@id").GetString());
        var question = Assert.Single(Node(detail, "FAQPage").GetProperty("mainEntity").EnumerateArray(), x => x.GetProperty("name").GetString() == "Hizmet sorusu?");
        Assert.Equal("Hizmet cevabı.", question.GetProperty("acceptedAnswer").GetProperty("text").GetString());
        var crumbs = Node(detail, "BreadcrumbList").GetProperty("itemListElement");
        Assert.Equal(3, crumbs.GetArrayLength());
        Assert.Equal((3, Domain + "/urunler/" + service.Slug), (crumbs[2].GetProperty("position").GetInt32(), crumbs[2].GetProperty("item").GetString()));

        var article = Node(Graph(await client.GetStringAsync($"/blog/{post}")), "BlogPosting");
        Assert.Equal("2026-01-02T03:04:05Z", article.GetProperty("datePublished").GetString());
        Assert.Equal("Gövde", article.GetProperty("articleBody").GetString());
        Assert.Contains("article:published_time\" content=\"2026-01-02T03:04:05Z", await client.GetStringAsync($"/blog/{post}"));

        var list = Graph(await client.GetStringAsync("/urunler"));
        Assert.Contains(Node(list, "ItemList").GetProperty("itemListElement").EnumerateArray(), x => x.GetProperty("url").GetString() == Domain + "/urunler/" + service.Slug);
        Assert.Equal("CollectionPage", list.Single(x => x.GetProperty("@id").GetString()!.EndsWith("#webpage")).GetProperty("@type").GetString());
        Assert.Contains(Node(Graph(await client.GetStringAsync("/galeri")), "ImageGallery").GetProperty("image").EnumerateArray(), x => x.TryGetProperty("name", out var name) && name.GetString() == "Görsel adı");
        Assert.Equal("ContactPage", Graph(await client.GetStringAsync("/iletisim")).Single(x => x.GetProperty("@id").GetString()!.EndsWith("#webpage")).GetProperty("@type").GetString());

        foreach (var path in new[] { "/", "/urunler", $"/urunler/{service.Slug}", $"/blog/{post}", "/galeri" }) Assert.DoesNotContain("\"~/", await client.GetStringAsync(path));
    }

    [Fact]
    public async Task HostileContent_CannotBreakSchemaOrFiles()
    {
        await using var app = postgres.CreateApp("seo_hostile");
        var client = app.NewClient();
        var slug = Tag();
        const string attack = "</script><script>alert(1)</script> & \"x\"";
        await AddAsync(app, new BlogPost { Title = attack, Slug = slug, Summary = attack, Content = attack, Image = "/images/a&b.png", IsPublished = true, PublishedUtc = DateTime.UtcNow }, x => x.Id);

        var html = await client.GetStringAsync($"/blog/{slug}");
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.DoesNotContain("&#x2B;json", html);
        Assert.Contains("Karakuş Alüminyum</title>", await client.GetStringAsync("/urunler")); // Türkçe harf ham kaynakta okunur
        Assert.Equal(attack, Node(Graph(html), "BlogPosting").GetProperty("headline").GetString());
        var sitemap = await client.GetStringAsync("/sitemap.xml");
        XDocument.Parse(sitemap); // & kaçırılmazsa XML geçersiz olur
        Assert.Contains("a&amp;b.png", sitemap);
    }

    [Fact]
    public async Task Sitemap_ListsImages_AndOnlyIndexablePages()
    {
        await using var app = postgres.CreateApp("seo_images");
        var client = app.NewClient();
        await AddAsync(app, new GalleryImage { ImagePath = "/images/galeri-testi.png", Title = "T" }, x => x.Id);
        await AddAsync(app, new GalleryImage { ImagePath = "/images/yayinda-degil.png", IsPublished = false }, x => x.Id);
        var text = await client.GetStringAsync("/sitemap.xml");
        Assert.Contains($"{Domain}/images/galeri-testi.png", text);
        Assert.DoesNotContain("yayinda-degil", text);
        foreach (var hidden in new[] { "/yonetim", "/gizlilik-politikasi", "/kvkk-aydinlatma-metni", "/iletisim/tesekkurler", "/hata", ".md" }) Assert.DoesNotContain(Domain + hidden, text);
    }

    [Fact]
    public async Task Robots_AllowsAiCrawlers_ButKeepsPanelClosed_AndCanBeTurnedOff()
    {
        await using var app = postgres.CreateApp("seo_robots");
        var client = app.NewClient();
        var text = await client.GetStringAsync("/robots.txt");
        foreach (var bot in new[] { "GPTBot", "OAI-SearchBot", "ClaudeBot", "PerplexityBot", "Google-Extended" }) Assert.Contains("User-agent: " + bot, text);
        var groups = text.Split("\n\n");
        Assert.All(groups.Where(x => x.StartsWith("User-agent")), g => Assert.Contains("Disallow: /yonetim\n", g));
        Assert.Contains("Disallow: /t/", text);
        Assert.DoesNotContain("Disallow: /\n", text);
        Assert.Contains($"Sitemap: {Domain}/sitemap.xml", text);

        await app.WithDbAsync(async db =>
        {
            (await db.SiteSettingRecords.SingleAsync(x => x.Key == "AiCrawlersAllowed")).Value = "false";
            return await db.SaveChangesAsync();
        });
        var closed = await client.GetStringAsync("/robots.txt");
        Assert.Matches("User-agent: GPTBot[\\s\\S]*Disallow: /\\n", closed);
        Assert.StartsWith("User-agent: *\nAllow: /\n", closed); // normal arama motorları etkilenmez
    }

    [Fact]
    public async Task Markdown_HasRightHeaders_AndNoindexPagesHaveNone()
    {
        var client = postgres.Shared.NewClient();
        var response = await client.GetAsync("/urunler/aluminyum-dograma.md");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"<{Domain}/urunler/aluminyum-dograma>; rel=\"canonical\"", response.Headers.GetValues("Link"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("# Alüminyum Doğrama", text);
        Assert.Contains($"Kaynak: {Domain}/urunler/aluminyum-dograma", text);

        foreach (var path in new[] { "/gizlilik-politikasi.md", "/yonetim.md", "/urunler/yok.md", "/sitemap.md", "/yonetim/ayarlar.md" }) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/index.md", "/kurumsal.md", "/urunler.md", "/projeler.md", "/galeri.md", "/blog.md", "/iletisim.md" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Contains("Telefon:", await client.GetStringAsync("/iletisim.md"));
        Assert.DoesNotContain("text/markdown", await client.GetStringAsync("/gizlilik-politikasi"));
    }

    [Fact]
    public async Task LlmsTxt_FollowsTheFormat()
    {
        var text = await postgres.Shared.NewClient().GetStringAsync("/llms.txt");
        var lines = text.Split('\n');
        Assert.StartsWith("# ", lines[0]);
        Assert.StartsWith("> ", lines[2]);
        Assert.Contains("## Ürün ve hizmetler", text);
        Assert.Matches("- \\[[^\\]]+\\]\\(https://[^)]+\\.md\\): .+", text);
        Assert.Contains("/llms-full.txt", text);
    }

    [Fact]
    public async Task PrivateAddresses_AreMarkedNoindex_PublicOnesAreNot()
    {
        var client = postgres.Shared.NewClient();
        foreach (var path in new[] { "/yonetim", "/yonetim-giris", "/health", "/yonetim/muhasebe" })
            Assert.Contains("noindex", string.Join(',', (await client.GetAsync(path)).Headers.GetValues("X-Robots-Tag")));
        Assert.False((await client.GetAsync("/urunler")).Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task MissingPage_Returns404WithPage_NotSoft200()
    {
        var client = postgres.Shared.NewClient();
        var response = await client.GetAsync("/bu-sayfa-yok");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bu sayfayı bulamadık", html);
        Assert.Contains("noindex", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/urunler/bu-urun-yok")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/hata/500")).StatusCode);
        Assert.DoesNotContain("application/ld+json", html);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<Uri> Targets { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            Targets.Add(request.RequestUri!);
            return respond(request, body);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private static async Task<int> RunAsync(AppFactory app, HttpMessageHandler handler)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var publisher = new IndexNowPublisher(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<SiteContentIndex>(), services.GetRequiredService<SiteSettingsProvider>(), new FakeFactory(handler), NullLogger<IndexNowPublisher>.Instance);
        return await publisher.RunOnceAsync();
    }

    private static Task SetAsync(AppFactory app, string key, string value) => app.WithDbAsync(async db =>
    {
        (await db.SiteSettingRecords.SingleAsync(x => x.Key == key)).Value = value;
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task IndexNow_SendsAllOnce_ThenOnlyChanges()
    {
        await using var app = postgres.CreateApp("seo_indexnow");
        app.NewClient();
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        var first = await RunAsync(app, handler);
        Assert.True(first > 10);
        Assert.Equal("https://api.indexnow.org/indexnow", handler.Targets[0].ToString());
        using (var json = JsonDocument.Parse(handler.Bodies[0]))
        {
            Assert.Equal("www.karakusaluminyum.com", json.RootElement.GetProperty("host").GetString());
            var key = json.RootElement.GetProperty("key").GetString()!;
            Assert.Equal($"{Domain}/indexnow-{key}.txt", json.RootElement.GetProperty("keyLocation").GetString());
            Assert.Contains(json.RootElement.GetProperty("urlList").EnumerateArray(), x => x.GetString() == Domain + "/urunler/aluminyum-dograma");

            var client = app.NewClient();
            Assert.Equal(key, await client.GetStringAsync($"/indexnow-{key}.txt"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/indexnow-yanlis.txt")).StatusCode);
        }

        Assert.Equal(0, await RunAsync(app, handler));
        Assert.Single(handler.Bodies);

        var slug = Tag();
        await AddAsync(app, new BlogPost { Title = "Yeni", Slug = slug, Summary = "x", Content = "y", IsPublished = true, PublishedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow.AddMinutes(1) }, x => x.Id);
        Assert.True(await RunAsync(app, handler) > 0);
        Assert.Contains($"{Domain}/blog/{slug}", handler.Bodies[1]);
        Assert.DoesNotContain("/urunler/aluminyum-dograma", handler.Bodies[1]);
    }

    [Fact]
    public async Task IndexNow_FailureIsRetried()
    {
        await using var app = postgres.CreateApp("seo_indexnow_retry");
        app.NewClient();
        var refused = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.Equal(0, await RunAsync(app, refused));
        Assert.Single(refused.Bodies);

        var accepted = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        Assert.True(await RunAsync(app, accepted) > 10); // reddedilen bildirim kaybolmaz
    }

    [Theory]
    [InlineData("IndexNowEnabled", "false")]
    [InlineData("BaseUrl", "https://www.DOMAININIZ.com")]
    [InlineData("BaseUrl", "http://www.karakusaluminyum.com")]
    [InlineData("BaseUrl", "https://localhost")]
    public async Task IndexNow_StaysSilent_WhenOffOrNotARealAddress(string key, string value)
    {
        await using var app = postgres.CreateApp("seo_indexnow_off");
        app.NewClient();
        await SetAsync(app, "IndexNowEnabled", "true");
        await SetAsync(app, "BaseUrl", Domain);
        await SetAsync(app, key, value);
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(0, await RunAsync(app, handler));
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task SettingsPage_HidesIndexNowKey_AndOffersSeoSwitches()
    {
        await using var app = postgres.CreateApp("seo_settings");
        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        await admin.GetAsync("/");
        var key = await app.WithServiceAsync<IndexNowPublisher, string>(x => x.KeyAsync());
        var html = await admin.GetStringAsync("/yonetim/ayarlar");
        Assert.DoesNotContain(key, html);
        foreach (var name in new[] { "SiteDescription", "IndexNowEnabled", "AiCrawlersAllowed" }) Assert.Contains($"values[{name}]", html);

        var token = await admin.GetTokenAsync("/yonetim/ayarlar");
        await admin.PostFormAsync("/yonetim/ayarlar/kaydet", token, ("values[SiteDescription]", "Özel açıklama metni"));
        var client = app.NewClient();
        Assert.Contains("> Özel açıklama metni", await client.GetStringAsync("/llms.txt"));
        Assert.Equal("Özel açıklama metni", Node(Graph(await client.GetStringAsync("/")), "HomeAndConstructionBusiness").GetProperty("description").GetString());
    }
}
