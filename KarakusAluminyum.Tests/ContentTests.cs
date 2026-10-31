using System.Net;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class SimpleMarkdownTests
{
    [Fact]
    public void Formats_HeadingsListsBoldAndInternalLinks()
    {
        var html = SimpleMarkdown.ToHtml("## Başlık\n\nMetin **kalın** ve [bağlantı](/urunler/x).\n\n- Bir\n- İki");
        Assert.Equal("<h2>Başlık</h2><p>Metin <strong>kalın</strong> ve <a href=\"/urunler/x\">bağlantı</a>.</p><ul><li>Bir</li><li>İki</li></ul>", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("## <b onclick=x>a</b>")]
    public void RawHtml_IsEncoded(string text)
    {
        var html = SimpleMarkdown.ToHtml(text);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b ", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](https://kotu.example)")]
    [InlineData("[x](//kotu.example)")]
    [InlineData("[x](/\\kotu.example)")]
    public void OnlyInternalLinks_BecomeAnchors(string text)
    {
        var html = SimpleMarkdown.ToHtml(text);
        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public void Empty_ReturnsEmpty() => Assert.Equal("", SimpleMarkdown.ToHtml("  "));
}

[Collection("postgres")]
public sealed class ContentTests(PostgresFixture postgres)
{
    // Ziyaretçiye görünmemesi gereken iç notlar ve yer tutucular.
    private static readonly string[] Forbidden = { "doğrulandıktan sonra", "veritabanına", "yönetim paneli", "buraya yazılmalıdır", "hukuk danışmanı", "Bu metin örnektir", "Bu taslak", "taslak olarak", "henüz detay içeriği", "gerçek bilgilerle", "XXX", "DOMAININIZ", "Lorem", "giydirme cephe" };

    [Fact]
    public async Task PublicPages_ContainNoInternalNotesOrPlaceholders()
    {
        await using var app = postgres.CreateApp("content_public");
        var client = app.NewClient();
        var paths = new List<string> { "/", "/kurumsal", "/urunler", "/projeler", "/galeri", "/blog", "/iletisim", "/gizlilik-politikasi", "/kvkk-aydinlatma-metni", "/llms-full.txt", "/llms.txt" };
        paths.AddRange(await app.WithDbAsync(db => db.Services.Where(x => x.IsPublished).Select(x => "/urunler/" + x.Slug).ToListAsync()));
        paths.AddRange(await app.WithDbAsync(db => db.Projects.Where(x => x.IsPublished).Select(x => "/projeler/" + x.Slug).ToListAsync()));
        paths.AddRange(await app.WithDbAsync(db => db.BlogPosts.Where(x => x.IsPublished).Select(x => "/blog/" + x.Slug).ToListAsync()));
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var text = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            problems.AddRange(Forbidden.Where(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)).Select(x => $"{path}: {x}"));
        }
        Assert.Empty(problems);
    }

    [Fact]
    public async Task EveryService_HasUniqueCopyAndItsOwnQuestions()
    {
        await using var app = postgres.CreateApp("content_services");
        app.NewClient();
        var services = await app.WithDbAsync(db => db.Services.AsNoTracking().ToListAsync());
        Assert.Equal(13, services.Count);
        Assert.Equal(13, services.Select(x => x.Intro).Distinct().Count());
        Assert.Equal(13, services.Select(x => x.SeoDescription).Distinct().Count());
        Assert.Equal(13, services.Select(x => x.BenefitsJson).Distinct().Count());
        Assert.All(services, x => Assert.InRange(x.SeoDescription.Length, 80, 165));
        Assert.All(services, x => Assert.InRange(x.SeoTitle.Length, 20, 70));
        Assert.All(services, x => Assert.True(x.Intro.Length > 250));
        var faqs = await app.WithDbAsync(db => db.FaqItems.AsNoTracking().Where(x => x.ServiceId != null && x.IsPublished).ToListAsync());
        Assert.All(services, s => Assert.Equal(3, faqs.Count(f => f.ServiceId == s.Id)));
        Assert.DoesNotContain(faqs, x => x.Question == "Sistem seçimi nasıl yapılır?");
    }

    [Fact]
    public async Task Articles_ArePublishedWithSeoData_AndRendered()
    {
        await using var app = postgres.CreateApp("content_blog");
        var client = app.NewClient();
        var posts = await app.WithDbAsync(db => db.BlogPosts.AsNoTracking().Where(x => x.IsPublished).ToListAsync());
        Assert.True(posts.Count >= 8);
        Assert.False(await app.WithDbAsync(db => db.BlogPosts.AnyAsync(x => x.Slug == "aluminyum-ve-mimari-sistemler")));
        foreach (var post in posts)
        {
            Assert.True(post.Content.Length > 1500, post.Slug);
            Assert.InRange(post.SeoDescription.Length, 80, 170);
            Assert.True(File.Exists(Path.Combine(FindRoot(), "wwwroot", post.Image.TrimStart('/'))), post.Image);
            var html = await client.GetStringAsync("/blog/" + post.Slug);
            Assert.Contains("<h2>", html);
            Assert.DoesNotContain("**", html.Split("application/ld+json")[0][^0..] + Regex.Match(html, "<article.*?</article>", RegexOptions.Singleline).Value);
            foreach (Match link in Regex.Matches(post.Content, @"\]\((/[^)]+)\)"))
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(link.Groups[1].Value)).StatusCode);
        }
        Assert.Contains(posts[0].Title, WebUtility.HtmlDecode(await client.GetStringAsync("/blog")));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KarakusAluminyum.Web.csproj"))) directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public async Task ExistingDatabase_GetsNewCopy_ButAdminEditsAreKept()
    {
        const string old = "Dikey hareketli cam uygulamaları için otomatik giyotin camlama sistemleri.";
        await using (var first = postgres.CreateApp("content_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(async db =>
            {
                // Eski kurulum: varsayılan metinler ve yöneticinin değiştirdiği birkaç alan.
                var generic = await db.Services.SingleAsync(x => x.Slug == "otomatik-giyotin-camlama-sistemleri");
                generic.ShortDescription = old;
                generic.Intro = old + " Uygun sistem seçimi; alanın ölçüleri, kullanım amacı ve uygulama koşulları değerlendirilerek yapılmalıdır. Projeniz için detay ve teklif almak üzere bizimle iletişime geçebilirsiniz.";
                db.FaqItems.RemoveRange(db.FaqItems.Where(x => x.ServiceId == generic.Id));
                db.FaqItems.Add(new FaqItem { ServiceId = generic.Id, Question = "Sistem seçimi nasıl yapılır?", Answer = "Ölçüler, kullanım amacı, uygulama koşulları ve beklentiler değerlendirilerek uygun seçenekler belirlenir." });
                db.FaqItems.Add(new FaqItem { ServiceId = generic.Id, Question = "Yönetici sorusu?", Answer = "Yönetici cevabı." });
                var edited = await db.Services.SingleAsync(x => x.Slug == "pvc-dograma");
                edited.Intro = "Yöneticinin kendi metni.";
                (await db.SiteSettingRecords.SingleAsync(x => x.Key == "HeroText")).Value = "Alüminyum doğrama, sürme sistemler, cephe ve tamamlayıcı mimari çözümler. Projenizin ihtiyaçlarını birlikte değerlendirelim.";
                (await db.SiteSettingRecords.SingleAsync(x => x.Key == "CtaText")).Value = "Yöneticinin çağrısı.";
                (await db.Projects.SingleAsync(x => x.Slug == "nisu-insaat-a-evleri")).Summary = "Nisu İnşaat A Evleri projesi. Uygulanan sistemler ve proje detayları firma tarafından doğrulandıktan sonra bu alana eklenebilir.";
                (await db.Projects.SingleAsync(x => x.Slug == "firat-insaat-b-evleri")).Summary = "Yöneticinin proje özeti.";
                db.BlogPosts.Add(new BlogPost { Title = "Eski taslak", Slug = "aluminyum-ve-mimari-sistemler", Summary = "x", Content = "Bu yazı taslak olarak oluşturulmuştur. Yayına almadan önce.", IsPublished = false });
                (await db.BlogPosts.SingleAsync(x => x.Slug == "dograma-bakimi-rehberi")).Content = "Yöneticinin yazısı.";
                db.BlogPosts.Remove(await db.BlogPosts.SingleAsync(x => x.Slug == "giyotin-cam-sistemleri-rehberi"));
                (await db.SiteSettingRecords.SingleAsync(x => x.Key == "ContentRefreshVersion")).Value = "0";
                return await db.SaveChangesAsync();
            });
        }

        await using (var second = postgres.CreateApp("content_upgrade"))
        {
            second.NewClient();
            string Setting(List<SiteSettingRecord> rows, string key) => rows.Single(x => x.Key == key).Value;
            var settings = await second.WithDbAsync(db => db.SiteSettingRecords.AsNoTracking().ToListAsync());
            Assert.DoesNotContain("cephe ve tamamlayıcı", Setting(settings, "HeroText"));
            Assert.Equal("Yöneticinin çağrısı.", Setting(settings, "CtaText"));

            var services = await second.WithDbAsync(db => db.Services.AsNoTracking().ToDictionaryAsync(x => x.Slug));
            Assert.NotEqual(old, services["otomatik-giyotin-camlama-sistemleri"].ShortDescription);
            Assert.Contains("motor yardımıyla", services["otomatik-giyotin-camlama-sistemleri"].Intro);
            Assert.Equal("Yöneticinin kendi metni.", services["pvc-dograma"].Intro);

            var faqs = await second.WithDbAsync(db => db.FaqItems.AsNoTracking().Where(x => x.ServiceId == services["otomatik-giyotin-camlama-sistemleri"].Id).Select(x => x.Question).ToListAsync());
            Assert.Contains("Yönetici sorusu?", faqs);
            Assert.DoesNotContain("Sistem seçimi nasıl yapılır?", faqs);
            Assert.Equal(4, faqs.Count);

            var projects = await second.WithDbAsync(db => db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Slug));
            Assert.DoesNotContain("doğrulandıktan", projects["nisu-insaat-a-evleri"].Summary);
            Assert.Equal("Yöneticinin proje özeti.", projects["firat-insaat-b-evleri"].Summary);

            var posts = await second.WithDbAsync(db => db.BlogPosts.AsNoTracking().ToDictionaryAsync(x => x.Slug));
            Assert.False(posts.ContainsKey("aluminyum-ve-mimari-sistemler"));
            Assert.Equal("Yöneticinin yazısı.", posts["dograma-bakimi-rehberi"].Content);
            Assert.True(posts.ContainsKey("giyotin-cam-sistemleri-rehberi"));
            await second.WithDbAsync(async db =>
            {
                db.BlogPosts.Remove(await db.BlogPosts.SingleAsync(x => x.Slug == "giyotin-cam-sistemleri-rehberi"));
                return await db.SaveChangesAsync();
            });
        }

        await using var third = postgres.CreateApp("content_upgrade");
        third.NewClient();
        Assert.False(await third.WithDbAsync(db => db.BlogPosts.AnyAsync(x => x.Slug == "giyotin-cam-sistemleri-rehberi"))); // silinen yazı geri gelmez
    }

    [Fact]
    public async Task LegalPages_UseCompanyDetails_AndListRights()
    {
        await using var app = postgres.CreateApp("content_legal");
        var client = app.NewClient();
        await app.WithDbAsync(async db =>
        {
            (await db.SiteSettingRecords.SingleAsync(x => x.Key == "LegalName")).Value = "Örnek Ticaret Unvanı Ltd. Şti.";
            return await db.SaveChangesAsync();
        });
        var kvkk = WebUtility.HtmlDecode(await client.GetStringAsync("/kvkk-aydinlatma-metni"));
        Assert.Contains("Örnek Ticaret Unvanı Ltd. Şti.", kvkk);
        foreach (var text in new[] { "Veri sorumlusu", "11. maddesi", "otuz gün", "Aktarım", "Saklama süresi" }) Assert.Contains(text, kvkk);
        Assert.Contains("Örnek Ticaret Unvanı Ltd. Şti.", WebUtility.HtmlDecode(await client.GetStringAsync("/gizlilik-politikasi")));
    }

    [Fact]
    public async Task ProjectWithoutDetail_ShowsItsSummary()
    {
        var client = postgres.Shared.NewClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/projeler/nisu-insaat-a-evleri"));
        Assert.Contains("Proje kapsamı", html);
        Assert.Contains("uygulama gerçekleştirdik", html);
    }

    [Fact]
    public async Task ServiceDetail_SuggestsOtherServices_NotItself()
    {
        var html = await postgres.Shared.NewClient().GetStringAsync("/urunler/aluminyum-dograma");
        var related = Regex.Matches(html, "<a class=\"service-card\" href=\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToList();
        Assert.Equal(3, related.Count);
        Assert.DoesNotContain("/urunler/aluminyum-dograma", related);
    }

    [Fact]
    public async Task About_ListsPublishedServices()
    {
        var client = postgres.Shared.NewClient();
        var slugs = await postgres.Shared.WithDbAsync(db => db.Services.Where(x => x.IsPublished).Select(x => x.Slug).ToListAsync());
        var html = await client.GetStringAsync("/kurumsal");
        Assert.All(slugs, slug => Assert.Contains($"<a class=\"expertise-row\" href=\"/urunler/{slug}\"", html));
    }

    [Fact]
    public async Task ProjectWithoutImageOrDetail_HasNoPlaceholderHero_AndShowsSummaryOnce()
    {
        await using var app = postgres.CreateApp("content_project_detail");
        var client = app.NewClient();
        const string summary = "Görselsiz proje için tek cümlelik özet metni.";
        await app.WithDbAsync(db =>
        {
            db.Projects.Add(new PortfolioProject { Slug = "gorselsiz-proje", Title = "Görselsiz Proje", Type = "Konut Projesi", Summary = summary, Image = ProjectQueries.PlaceholderImage });
            return db.SaveChangesAsync();
        });

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/projeler/gorselsiz-proje"));
        var body = html[html.IndexOf("<section class=\"page-hero", StringComparison.Ordinal)..html.IndexOf("<footer", StringComparison.Ordinal)];

        Assert.DoesNotContain("page-hero__media", body);
        Assert.Single(Regex.Matches(body, Regex.Escape(summary)));
        Assert.Contains("class=\"next-project\"", body);
    }
}
