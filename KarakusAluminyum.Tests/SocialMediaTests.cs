using System.IO.Compression;
using System.Net;
using SkiaSharp;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class SocialMediaTests(PostgresFixture postgres)
{
    private const string First = "/yonetim/sosyal-medya/gorsel/01-villa-otomatik-giyotin.jpg";

    private static async Task<HttpClient> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail, AppFactory.Password);
        return client;
    }

    [Fact]
    public async Task Panel_ListsTenPosts_WithAudienceAndCaption()
    {
        var html = WebUtility.HtmlDecode(await (await AdminAsync(postgres.Shared)).GetStringAsync("/yonetim/sosyal-medya"));
        Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(html, "class=\"sm-card\"").Count);
        Assert.Contains("Hedef kitle: Kafe, restoran ve otel işletmecileri", html);
        Assert.Contains("#KarakuşAlüminyum", html);
        Assert.Contains(">Sosyal medya<", html);
    }

    [Fact]
    public async Task Images_AreInstagramPortraitSize()
    {
        var client = await AdminAsync(postgres.Shared);
        var html = await client.GetStringAsync("/yonetim/sosyal-medya");
        var urls = System.Text.RegularExpressions.Regex.Matches(html, "<img src=\"(/yonetim/sosyal-medya/gorsel/[^\"]+)\"").Select(x => x.Groups[1].Value).ToList();
        Assert.Equal(10, urls.Count);
        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal((1080, 1350), (bitmap.Width, bitmap.Height));
        }
    }

    [Fact]
    public async Task Images_AreNotPublic_AndOutsideOfWebRoot()
    {
        var anonymous = postgres.Shared.NewClient();
        foreach (var url in new[] { "/yonetim/sosyal-medya", First, "/yonetim/sosyal-medya/tumunu-indir" })
        {
            var response = await anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("yonetim-giris", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/social%20media/01-villa-otomatik-giyotin.jpg")).StatusCode);
    }

    [Theory]
    [InlineData("/yonetim/sosyal-medya/gorsel/yok.jpg")]
    [InlineData("/yonetim/sosyal-medya/gorsel/icerik.json")]
    [InlineData("/yonetim/sosyal-medya/gorsel/..%2F..%2Fappsettings.json")]
    [InlineData("/yonetim/sosyal-medya/gorsel/..%2F..%2F.env")]
    public async Task OnlyListedFiles_CanBeRead(string url)
    {
        var response = await (await AdminAsync(postgres.Shared)).GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Downloads_ReturnAttachment_AndZipHasAllFilesAndCaptions()
    {
        var client = await AdminAsync(postgres.Shared);
        var one = await client.GetAsync(First + "?indir=true");
        Assert.Equal("attachment", one.Content.Headers.ContentDisposition?.DispositionType);

        var zip = await client.GetAsync("/yonetim/sosyal-medya/tumunu-indir");
        Assert.Equal("application/zip", zip.Content.Headers.ContentType?.MediaType);
        using var archive = new ZipArchive(await zip.Content.ReadAsStreamAsync());
        Assert.Equal(10, archive.Entries.Count(x => x.FullName.EndsWith(".jpg") && !x.FullName.Contains('/')));
        Assert.Equal(18, archive.Entries.Count(x => x.FullName.StartsWith("one-cikanlar/")));
        using var reader = new StreamReader(archive.GetEntry("aciklamalar.txt")!.Open());
        Assert.Contains("Hedef kitle: Site yöneticileri", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Highlights_HaveSquareCoverAndStory_WithShortNames()
    {
        var client = await AdminAsync(postgres.Shared);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/sosyal-medya"));
        Assert.Equal(9, System.Text.RegularExpressions.Regex.Matches(html, "class=\"sm-hl\"").Count);
        foreach (var name in System.Text.RegularExpressions.Regex.Matches(html, "<h3>([^<]+)</h3>").Select(x => x.Groups[1].Value))
            Assert.True(name.Length <= 15, name); // Instagram uzun adı keser
        var covers = System.Text.RegularExpressions.Regex.Matches(html, "/yonetim/sosyal-medya/one-cikan/[^\"?]+-kapak\\.jpg").Select(x => x.Value).Distinct().ToList();
        Assert.Equal(9, covers.Count);
        foreach (var cover in covers)
        {
            using var square = SKBitmap.Decode(await client.GetByteArrayAsync(cover));
            Assert.Equal((1080, 1080), (square.Width, square.Height));
            using var story = SKBitmap.Decode(await client.GetByteArrayAsync(cover.Replace("-kapak", "-hikaye")));
            Assert.Equal((1080, 1920), (story.Width, story.Height));
        }
    }

    [Theory]
    [InlineData("/yonetim/sosyal-medya/one-cikan/yok.jpg")]
    [InlineData("/yonetim/sosyal-medya/one-cikan/..%2F01-villa-otomatik-giyotin.jpg")]
    [InlineData("/yonetim/sosyal-medya/one-cikan/..%2F..%2Fappsettings.json")]
    public async Task Highlights_OnlyListedFilesCanBeRead(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await (await AdminAsync(postgres.Shared)).GetAsync(url)).StatusCode);

    [Fact]
    public async Task Highlights_AreNotPublic() =>
        Assert.Equal(HttpStatusCode.Redirect, (await postgres.Shared.NewClient().GetAsync("/yonetim/sosyal-medya/one-cikan/09-teklif-al-kapak.jpg")).StatusCode);

    [Fact]
    public async Task Captions_UseCorrectFoundingYear()
    {
        var html = WebUtility.HtmlDecode(await (await AdminAsync(postgres.Shared)).GetStringAsync("/yonetim/sosyal-medya"));
        Assert.Contains("2001'den bu yana", html);
        Assert.DoesNotContain("1998'den", html);
    }
}
