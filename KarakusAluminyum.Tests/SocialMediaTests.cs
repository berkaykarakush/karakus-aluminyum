using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class SocialMediaTests(PostgresFixture postgres)
{
    private const string Root = "/yonetim/sosyal-medya";
    private const string Posts = Root + "/bilgi-icerikleri";
    private const string Highlights = Root + "/one-cikanlar";
    private const string Projects = Root + "/referans-projeler";
    private const string Ongoing = Root + "/devam-eden-projeler";
    private const string First = Root + "/gorsel/01-villa-otomatik-giyotin.jpg";
    private const int PostCount = 13;

    private static async Task<HttpClient> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail, AppFactory.Password);
        return client;
    }

    private static byte[] Jpeg(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    private static MultipartFormDataContent ProjectForm(string token, string name, byte[]? photo, byte[]? image, params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(name), "Name" } };
        foreach (var (key, value) in fields) form.Add(new StringContent(value), key);
        if (photo is not null) form.Add(new ByteArrayContent(photo), "photo", "proje.jpg");
        if (image is not null) form.Add(new ByteArrayContent(image), "image", "gonderi.jpg");
        return form;
    }

    [Fact]
    public async Task Panel_HasFourSections_AndOpensOnProjects()
    {
        var client = await AdminAsync(postgres.Shared);
        var root = await client.GetAsync(Root);
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.EndsWith("/referans-projeler", root.Headers.Location!.ToString());
        foreach (var url in new[] { Projects, Ongoing, Posts, Highlights })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
            foreach (var tab in new[] { "Referans projeler", "Devam eden projeler", "Bilgi içerikleri", "Öne çıkanlar" }) Assert.Contains(tab, html);
            Assert.Contains("aria-current=\"page\"", html);
        }
    }

    [Fact]
    public async Task Posts_ListAllWithAudienceAndCaption()
    {
        var html = WebUtility.HtmlDecode(await (await AdminAsync(postgres.Shared)).GetStringAsync(Posts));
        Assert.Equal(PostCount, System.Text.RegularExpressions.Regex.Matches(html, "class=\"sm-card\"").Count);
        Assert.Contains("Hedef kitle: Kafe, restoran ve otel işletmecileri", html);
        Assert.Contains("Projeniz yağmura gerçekten hazır mı?", html);
        Assert.Contains("2001'den bu yana", html);
        Assert.DoesNotContain("1998'den", html);
    }

    [Fact]
    public async Task Posts_AreInstagramPortraitSize()
    {
        var client = await AdminAsync(postgres.Shared);
        var html = await client.GetStringAsync(Posts);
        var urls = System.Text.RegularExpressions.Regex.Matches(html, "<img src=\"(/yonetim/sosyal-medya/gorsel/[^\"]+)\"").Select(x => x.Groups[1].Value).ToList();
        Assert.Equal(PostCount, urls.Count);
        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal((1080, 1350), (bitmap.Width, bitmap.Height));
        }
    }

    [Fact]
    public async Task Panel_IsNotPublic_AndFilesAreOutsideOfWebRoot()
    {
        var anonymous = postgres.Shared.NewClient();
        foreach (var url in new[] { Posts, Highlights, Projects, First, Root + "/tumunu-indir", Root + "/one-cikan/09-teklif-al-kapak.jpg", Projects + "/ekle" })
        {
            var response = await anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("yonetim-giris", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/social%20media/bilgi-icerikleri/01-villa-otomatik-giyotin.jpg")).StatusCode);
    }

    [Theory]
    [InlineData(Root + "/gorsel/yok.jpg")]
    [InlineData(Root + "/gorsel/icerik.json")]
    [InlineData(Root + "/gorsel/..%2F..%2Fappsettings.json")]
    [InlineData(Root + "/gorsel/..%2Fone-cikanlar%2F01-hakkimizda-kapak.jpg")]
    [InlineData(Root + "/one-cikan/yok.jpg")]
    [InlineData(Root + "/one-cikan/..%2Fbilgi-icerikleri%2F01-villa-otomatik-giyotin.jpg")]
    [InlineData(Root + "/one-cikan/..%2F..%2Fappsettings.json")]
    [InlineData(Root + "/referans-projeler/gorsel/999999")]
    public async Task OnlyListedFiles_CanBeRead(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, (await (await AdminAsync(postgres.Shared)).GetAsync(url)).StatusCode);

    [Fact]
    public async Task Highlights_HaveSquareCoverAndStory_WithShortNames()
    {
        var client = await AdminAsync(postgres.Shared);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Highlights));
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

    [Fact]
    public async Task Project_IsCreatedEditedAndDeleted_WithLayoutAndFiles()
    {
        var app = postgres.Shared;
        var client = await AdminAsync(app);
        var name = "Proje " + Guid.NewGuid().ToString("N")[..6];
        var token = await client.GetTokenAsync(Projects + "/ekle");
        var editor = await client.GetStringAsync(Projects + "/ekle");
        Assert.Contains("social-project-editor.js", editor);
        Assert.Contains("data-preset=\"1,1\"", editor);

        var created = await client.PostAsync(Projects + "/ekle", ProjectForm(token, name, Jpeg(1600, 1200, SKColors.SteelBlue), Jpeg(1080, 1350, SKColors.Navy),
            ("TextX", "1"), ("TextY", "0.25"), ("FocusX", "0.3"), ("FocusY", "0.5"), ("TextScale", "1.2")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var row = await app.WithDbAsync(db => db.SocialProjectPosts.AsNoTracking().SingleAsync(x => x.Name == name));
        Assert.Equal((1d, 0.25, 0.3, 1.2), (row.TextX, row.TextY, row.FocusX, row.TextScale)); // noktalı ondalık sunucu kültüründen bağımsız okunur
        Assert.Contains($"Referans projelerimizden: {name}.", row.Caption);
        string Disk(string path) => app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath + path;
        Assert.True(File.Exists(Disk(row.PhotoPath)));
        Assert.True(File.Exists(Disk(row.ImagePath)));

        var list = WebUtility.HtmlDecode(await client.GetStringAsync(Projects));
        Assert.Contains(name, list);
        var download = await client.GetAsync($"{Projects}/gorsel/{row.Id}?indir=true");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith($"referans-{row.Id:00}-proje-", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using (var bitmap = SKBitmap.Decode(await download.Content.ReadAsByteArrayAsync())) Assert.Equal((1080, 1350), (bitmap.Width, bitmap.Height));

        // Fotoğraf değiştirilmeden yeniden düzenlenir: eski görsel silinir, fotoğraf korunur.
        token = await client.GetTokenAsync($"{Projects}/duzenle/{row.Id}");
        Assert.Contains(row.PhotoPath, await client.GetStringAsync($"{Projects}/duzenle/{row.Id}"));
        var edited = await client.PostAsync($"{Projects}/duzenle/{row.Id}", ProjectForm(token, name + " B", null, Jpeg(1080, 1350, SKColors.Black), ("TextX", "0"), ("TextY", "1"), ("Caption", "Kendi metnim")));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        var after = await app.WithDbAsync(db => db.SocialProjectPosts.AsNoTracking().SingleAsync(x => x.Id == row.Id));
        Assert.Equal((name + " B", "Kendi metnim", 0d, 1d, row.PhotoPath), (after.Name, after.Caption, after.TextX, after.TextY, after.PhotoPath));
        Assert.NotEqual(row.ImagePath, after.ImagePath);
        Assert.False(File.Exists(Disk(row.ImagePath)));

        using (var archive = new ZipArchive(await (await client.GetAsync(Root + "/tumunu-indir")).Content.ReadAsStreamAsync()))
            Assert.Contains(archive.Entries, x => x.FullName.StartsWith($"referans-projeler/referans-{row.Id:00}-"));

        token = await client.GetTokenAsync(Projects);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"{Projects}/sil/{row.Id}", token)).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.SocialProjectPosts.AnyAsync(x => x.Id == row.Id)));
        Assert.False(File.Exists(Disk(after.ImagePath)));
        Assert.False(File.Exists(Disk(after.PhotoPath)));
    }

    // Devam eden projeler ayrı sekmededir: kendi etiketi, metni, dosya adı ve ZIP klasörü vardır; diğer sekmeden açılamaz.
    [Fact]
    public async Task OngoingProject_IsSeparateFromReferences()
    {
        var app = postgres.Shared;
        var client = await AdminAsync(app);
        var name = "Şantiye " + Guid.NewGuid().ToString("N")[..6];
        var editor = await client.GetStringAsync(Ongoing + "/ekle");
        Assert.Contains("label: \"Devam Eden Projelerimiz\"", WebUtility.HtmlDecode(editor));
        var token = await client.GetTokenAsync(Ongoing + "/ekle");

        var created = await client.PostAsync(Ongoing + "/ekle", ProjectForm(token, name, Jpeg(1600, 1200, SKColors.SteelBlue), Jpeg(1080, 1350, SKColors.Navy), ("Kind", "reference")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.EndsWith("/devam-eden-projeler", created.Headers.Location!.ToString());
        var row = await app.WithDbAsync(db => db.SocialProjectPosts.AsNoTracking().SingleAsync(x => x.Name == name));
        Assert.Equal("ongoing", row.Kind); // tür formdan değil adresten gelir
        Assert.Contains($"Devam eden projelerimizden: {name}.", row.Caption);

        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync(Ongoing)));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync(Projects)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Projects}/duzenle/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Projects}/gorsel/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync($"{Projects}/sil/{row.Id}", await client.GetTokenAsync(Projects))).StatusCode);

        var download = await client.GetAsync($"{Ongoing}/gorsel/{row.Id}?indir=true");
        Assert.StartsWith($"devam-eden-{row.Id:00}-santiye-", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using (var archive = new ZipArchive(await (await client.GetAsync(Root + "/tumunu-indir")).Content.ReadAsStreamAsync()))
        {
            Assert.Contains(archive.Entries, x => x.FullName.StartsWith($"devam-eden-projeler/devam-eden-{row.Id:00}-"));
            Assert.Contains(archive.Entries, x => x.FullName == "devam-eden-projeler/aciklamalar.txt");
        }

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"{Ongoing}/sil/{row.Id}", await client.GetTokenAsync(Ongoing))).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.SocialProjectPosts.AnyAsync(x => x.Id == row.Id)));
    }

    [Fact]
    public async Task Project_RejectsMissingPhoto_AndWrongImageSize()
    {
        var app = postgres.Shared;
        var client = await AdminAsync(app);
        var token = await client.GetTokenAsync(Projects + "/ekle");
        var name = "Hatalı " + Guid.NewGuid().ToString("N")[..6];

        var noPhoto = await client.PostAsync(Projects + "/ekle", ProjectForm(token, name, null, Jpeg(1080, 1350, SKColors.Navy)));
        Assert.Equal(HttpStatusCode.OK, noPhoto.StatusCode);
        Assert.Contains("Proje fotoğrafını seçin.", WebUtility.HtmlDecode(await noPhoto.Content.ReadAsStringAsync()));

        var wrongSize = await client.PostAsync(Projects + "/ekle", ProjectForm(token, name, Jpeg(800, 600, SKColors.Gray), Jpeg(500, 500, SKColors.Navy)));
        Assert.Equal(HttpStatusCode.OK, wrongSize.StatusCode);
        Assert.Contains("1080×1350", WebUtility.HtmlDecode(await wrongSize.Content.ReadAsStringAsync()));

        var notImage = await client.PostAsync(Projects + "/ekle", ProjectForm(token, name, System.Text.Encoding.UTF8.GetBytes("<svg/>"), Jpeg(1080, 1350, SKColors.Navy)));
        Assert.Equal(HttpStatusCode.OK, notImage.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.SocialProjectPosts.AnyAsync(x => x.Name == name)));
    }

    [Fact]
    public async Task Downloads_ReturnAttachment_AndZipIsSplitIntoSections()
    {
        var client = await AdminAsync(postgres.Shared);
        var one = await client.GetAsync(First + "?indir=true");
        Assert.Equal("attachment", one.Content.Headers.ContentDisposition?.DispositionType);

        var zip = await client.GetAsync(Root + "/tumunu-indir");
        Assert.Equal("application/zip", zip.Content.Headers.ContentType?.MediaType);
        using var archive = new ZipArchive(await zip.Content.ReadAsStreamAsync());
        Assert.Equal(PostCount, archive.Entries.Count(x => x.FullName.StartsWith("bilgi-icerikleri/") && x.FullName.EndsWith(".jpg")));
        Assert.Equal(18, archive.Entries.Count(x => x.FullName.StartsWith("one-cikanlar/")));
        Assert.DoesNotContain(archive.Entries, x => !x.FullName.Contains('/'));
        using var reader = new StreamReader(archive.GetEntry("bilgi-icerikleri/aciklamalar.txt")!.Open());
        Assert.Contains("Hedef kitle: Site yöneticileri", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ExistingDatabase_GetsSocialProjectTable()
    {
        await using (var first = postgres.CreateApp("social_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"SocialProjectPosts\";"));
        }
        await using var second = postgres.CreateApp("social_upgrade", schemaMode: "existing");
        second.NewClient();
        Assert.False(await second.WithDbAsync(db => db.SocialProjectPosts.AnyAsync()));
    }
}
