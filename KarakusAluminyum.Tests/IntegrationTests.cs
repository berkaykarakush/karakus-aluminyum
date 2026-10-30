using System.Net;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class SeedAndSchemaTests(PostgresFixture postgres)
{
    private static string RepoWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KarakusAluminyum.Web.csproj"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "wwwroot");
    }

    private static string DiskPath(string urlPath) => Path.Combine(RepoWwwroot(), urlPath.TrimStart('/'));

    [Fact]
    public async Task FirstStart_OnEmptyDatabase_SeedsGallery()
    {
        await using var app = postgres.CreateApp("first_start");
        var html = await app.NewClient().GetStringAsync("/galeri");

        Assert.True(await app.WithDbAsync(db => db.GalleryImages.AnyAsync()));
        Assert.Contains("class=\"gallery-item\"", html);
    }

    [Fact]
    public async Task Restart_DoesNotRestoreDeletedGalleryImages()
    {
        await using (var first = postgres.CreateApp("no_reseed"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.GalleryImages.ExecuteDeleteAsync());
        }

        await using var second = postgres.CreateApp("no_reseed");
        second.NewClient();
        Assert.Equal(0, await second.WithDbAsync(db => db.GalleryImages.CountAsync()));
    }

    [Fact]
    public async Task ExistingDatabaseWithoutGalleryTable_IsUpgradedOnStart()
    {
        await using (var first = postgres.CreateApp("existing_db"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"GalleryImages\"; DROP TABLE \"FaqItems\""));
        }

        await using var second = postgres.CreateApp("existing_db", schemaMode: "existing");
        var client = second.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/galeri")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/urunler/aluminyum-dograma")).StatusCode);
    }

    [Fact]
    public async Task Restart_RepairsServiceImagesThatDoNotExist()
    {
        const string slug = "rampa-ve-koruk-sistemleri";
        await using (var first = postgres.CreateApp("image_repair"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Services.Where(s => s.Slug == slug)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Image, "/images/rampa-ve-koruk-sistemleri.png")));
        }

        await using var second = postgres.CreateApp("image_repair");
        second.NewClient();
        var image = await second.WithDbAsync(db => db.Services.Where(s => s.Slug == slug).Select(s => s.Image).SingleAsync());
        Assert.True(File.Exists(DiskPath(image!)), image);
    }

    [Fact]
    public async Task SeededImages_AllExistOnDisk()
    {
        var app = postgres.Shared;
        app.NewClient();
        var paths = await app.WithDbAsync(async db =>
            (await db.Services.Select(s => s.Image).ToListAsync())
            .Concat(await db.Projects.Select(p => p.Image).ToListAsync())
            .Concat(await db.GalleryImages.Select(g => g.ImagePath).ToListAsync())
            .Distinct().ToList());

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(File.Exists(DiskPath(p!)), p));
    }

    [Fact]
    public void StylesheetImages_AllExistOnDisk()
    {
        var css = File.ReadAllText(Path.Combine(RepoWwwroot(), "css", "site.css"));
        var paths = Regex.Matches(css, "url\\(\"(/images/[^\"]+)\"\\)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(File.Exists(DiskPath(p)), p));
    }

    [Fact]
    public void LocalBusinessSchema_ImageExistsOnDisk()
    {
        var seo = new SeoService(Options.Create(new SiteSettings { BaseUrl = "https://example.test/" }));
        var schema = (Dictionary<string, object?>)seo.LocalBusinessSchema();
        var path = new Uri((string)schema["image"]!).AbsolutePath;
        Assert.True(File.Exists(DiskPath(path)), path);
    }
}

[Collection("postgres")]
public sealed class RoutingAndAuthorizationTests(PostgresFixture postgres)
{
    private static async Task CreateEditorAsync(AppFactory app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Editor")).Succeeded);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/galeri")]
    [InlineData("/galeri?k=hizmet-aluminyum-dograma")]
    [InlineData("/galeri?k=bilinmeyen")]
    [InlineData("/urunler")]
    [InlineData("/urunler/aluminyum-dograma")]
    [InlineData("/projeler")]
    [InlineData("/projeler/nisu-insaat-a-evleri")]
    [InlineData("/blog")]
    [InlineData("/iletisim")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    public async Task PublicPages_ReturnOk(string url)
    {
        var response = await postgres.Shared.NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/kurumsal")]
    [InlineData("/iletisim/tesekkurler")]
    public async Task PublicPages_DoNotShowTemplateNotes(string url)
    {
        var html = WebUtility.HtmlDecode(await postgres.Shared.NewClient().GetStringAsync(url));
        Assert.DoesNotContain("Bu metni", html);
        Assert.DoesNotContain("Yayın öncesinde", html);
        Assert.DoesNotContain("burada anlatın", html);
        Assert.DoesNotContain("Bu sayfaya firmanızın", html);
        Assert.DoesNotContain("Canlıya geçmeden", html);
    }

    [Fact]
    public async Task HomePage_LinksOnlyToExistingProjects_WithRealCovers()
    {
        var client = postgres.Shared.NewClient();
        var html = await client.GetStringAsync("/");
        var cards = Regex.Matches(html, "<a class=\"project-card\" href=\"([^\"]+)\"><img src=\"([^\"]+)\"");

        Assert.Equal(3, cards.Count);
        foreach (Match card in cards)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(card.Groups[1].Value)).StatusCode);
            Assert.DoesNotContain("image-placeholder", card.Groups[2].Value);
        }
    }

    [Fact]
    public async Task ServicePage_GalleryDoesNotRepeatCoverImage()
    {
        var html = await postgres.Shared.NewClient().GetStringAsync("/urunler/otomatik-tente");
        Assert.DoesNotContain("class=\"gallery-item\"", html);
    }

    [Fact]
    public async Task Sitemap_IncludesGallery()
        => Assert.Contains("/galeri</loc>", await postgres.Shared.NewClient().GetStringAsync("/sitemap.xml"));

    [Theory]
    [InlineData("/yonetim")]
    [InlineData("/yonetim/galeri")]
    [InlineData("/yonetim/galeri/ekle")]
    [InlineData("/yonetim/sss")]
    [InlineData("/yonetim/sss/ekle")]
    [InlineData("/yonetim/cizim")]
    [InlineData("/yonetim/cizim/ekle")]
    [InlineData("/yonetim/cizim/sistemler")]
    [InlineData("/yonetim/cizim/optimizasyon")]
    [InlineData("/yonetim/camlar")]
    [InlineData("/yonetim/camlar/ekle")]
    [InlineData("/yonetim/camlar/tipler")]
    [InlineData("/yonetim/sozlesmeler")]
    [InlineData("/yonetim/sozlesmeler/ekle")]
    [InlineData("/yonetim/sablonlar")]
    [InlineData("/yonetim/sablonlar/quote")]
    [InlineData("/yonetim/crm/teklifler/yazdir/1")]
    [InlineData("/yonetim/sozlesmeler/yazdir/1")]
    [InlineData("/yonetim/crm/teklifler")]
    [InlineData("/yonetim/crm/musteriler")]
    [InlineData("/yonetim/hizmetler")]
    [InlineData("/yonetim/projeler")]
    [InlineData("/yonetim/yazilar")]
    [InlineData("/yonetim/mesajlar")]
    [InlineData("/yonetim/ayarlar")]
    [InlineData("/yonetim/kullanicilar")]
    [InlineData("/yonetim/denetim-kaydi")]
    public async Task AdminPages_RedirectAnonymousToLogin(string url)
    {
        var response = await postgres.Shared.NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/yonetim-giris", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/yonetim/galeri", true)]
    [InlineData("/yonetim/sss", true)]
    [InlineData("/yonetim/cizim", true)]
    [InlineData("/yonetim/camlar", true)]
    [InlineData("/yonetim/camlar/tipler", true)]
    [InlineData("/yonetim/cizim/sistemler", true)]
    [InlineData("/yonetim/crm/teklifler", true)]
    [InlineData("/yonetim/sozlesmeler", true)]
    [InlineData("/yonetim/sablonlar", false)]
    [InlineData("/yonetim/sablonlar/contract", false)]
    [InlineData("/yonetim/ayarlar", false)]
    [InlineData("/yonetim/kullanicilar", false)]
    [InlineData("/yonetim/denetim-kaydi", false)]
    public async Task Editor_CanOnlyReachEditorPages(string url, bool allowed)
    {
        const string email = "editor@test.local";
        var app = postgres.Shared;
        var client = app.NewClient();
        await CreateEditorAsync(app, email);
        await client.LoginAsync(email);

        var response = await client.GetAsync(url);
        if (allowed) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else Assert.Contains("/erisim-engellendi", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Settings_HidesInternalSeedMarkers()
    {
        var client = postgres.Shared.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        var html = await client.GetStringAsync("/yonetim/ayarlar");
        Assert.DoesNotContain("GallerySeeded", html);
        Assert.DoesNotContain("HomeFaqsSeeded", html);
        Assert.DoesNotContain("DrawingSeeded", html);
        Assert.DoesNotContain("ClausesSeeded", html);
    }
}

[Collection("postgres")]
public sealed class GalleryTests(PostgresFixture postgres)
{
    private static readonly byte[] FakeImage = [0x89, 0x50, 0x4E, 0x47];

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string title, string fileName, byte[] bytes, params (string Key, string Value)[] extra)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(title), "Title" },
            { new StringContent("0"), "SortOrder" },
            { new StringContent("true"), "IsPublished" },
            { new ByteArrayContent(bytes), "file", fileName }
        };
        foreach (var (key, value) in extra) form.Add(new StringContent(value), key);
        return await client.PostAsync("/yonetim/galeri/ekle", form);
    }

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/galeri/ekle"));
    }

    [Fact]
    public async Task Upload_IsServedAndListed_ThenDeleteRemovesFileAndRow()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);

        var upload = await UploadAsync(client, token, "yukleme-testi", "foto.PNG", FakeImage);
        Assert.Equal("/yonetim/galeri", upload.Headers.Location?.OriginalString);

        var image = await app.WithDbAsync(db => db.GalleryImages.AsNoTracking().SingleAsync(g => g.Title == "yukleme-testi"));
        Assert.StartsWith("/uploads/galeri/genel/", image.ImagePath);
        Assert.EndsWith(".png", image.ImagePath);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(image.ImagePath)).StatusCode);
        Assert.Contains(image.ImagePath, await client.GetStringAsync("/galeri?k=genel"));

        var diskPath = Path.Combine(app.WebRoot, image.ImagePath.TrimStart('/'));
        Assert.True(File.Exists(diskPath));

        await client.PostFormAsync($"/yonetim/galeri/sil/{image.Id}", token);
        Assert.False(File.Exists(diskPath));
        Assert.False(await app.WithDbAsync(db => db.GalleryImages.AnyAsync(g => g.Id == image.Id)));
    }

    [Fact]
    public async Task Delete_KeepsSeedImagesOnDisk()
    {
        var app = postgres.CreateApp("delete_seed");
        await using (app)
        {
            var (client, token) = await AdminAsync(app);
            var seeded = await app.WithDbAsync(db => db.GalleryImages.AsNoTracking().FirstAsync());
            var copy = Path.Combine(app.WebRoot, seeded.ImagePath.TrimStart('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            await File.WriteAllBytesAsync(copy, FakeImage);

            await client.PostFormAsync($"/yonetim/galeri/sil/{seeded.Id}", token);

            Assert.True(File.Exists(copy));
            Assert.False(await app.WithDbAsync(db => db.GalleryImages.AnyAsync(g => g.Id == seeded.Id)));
        }
    }

    [Theory]
    [InlineData("zararli.svg", 4)]
    [InlineData("kabuk.php", 4)]
    [InlineData("buyuk.png", 5 * 1024 * 1024 + 1)]
    public async Task Upload_RejectsInvalidFiles(string fileName, int size)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var title = "red-" + fileName;

        var response = await UploadAsync(client, token, title, fileName, new byte[size]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form hata mesajıyla yeniden gösterilir
        Assert.False(await app.WithDbAsync(db => db.GalleryImages.AnyAsync(g => g.Title == title)));
    }

    [Fact]
    public async Task Upload_RejectsBothServiceAndProject()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var serviceId = await app.WithDbAsync(db => db.Services.Select(s => s.Id).FirstAsync());
        var projectId = await app.WithDbAsync(db => db.Projects.Select(p => p.Id).FirstAsync());

        var response = await UploadAsync(client, token, "ikisi-birden", "a.png", FakeImage,
            ("ServiceId", serviceId.ToString()), ("ProjectId", projectId.ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.GalleryImages.AnyAsync(g => g.Title == "ikisi-birden")));
    }

    [Fact]
    public async Task PublicGallery_AllTabHasNoDuplicates_AndCategoryFilters()
    {
        var client = postgres.Shared.NewClient();
        static List<string> Items(string html) =>
            Regex.Matches(html, "<a href=\"([^\"]+)\" class=\"gallery-item\"").Select(m => m.Groups[1].Value).ToList();

        var all = Items(await client.GetStringAsync("/galeri"));
        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Distinct().Count());

        var filtered = Items(await client.GetStringAsync("/galeri?k=hizmet-pvc-dograma"));
        Assert.Equal(["/images/pvc-dograma.png", "/images/kapi-ve-pencere-sistemleri.png"], filtered);

        Assert.Contains("Bu kategoride henüz görsel yok.", await client.GetStringAsync("/galeri?k=hizmet-yok"));
    }
}

[Collection("postgres")]
public sealed class QuoteTests(PostgresFixture postgres)
{
    private static async Task<int> NewCustomerAsync(AppFactory app) => await app.WithDbAsync(async db =>
    {
        var customer = new Customer { Name = "Test Müşteri " + Guid.NewGuid().ToString("N")[..6] };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    });

    private static async Task<int> NewQuoteAsync(AppFactory app, QuoteStatus status)
    {
        var customerId = await NewCustomerAsync(app);
        return await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "T-" + Guid.NewGuid().ToString("N")[..10], Title = "Test", CustomerId = customerId, Status = status, CreatedUtc = DateTime.UtcNow };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });
    }

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/crm/teklifler/ekle"));
    }

    [Theory]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Sent, QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Draft, QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Accepted, QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Accepted, QuoteStatus.Accepted)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Rejected, QuoteStatus.Rejected)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Expired, QuoteStatus.Expired)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Draft, QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Rejected, QuoteStatus.Accepted, QuoteStatus.Rejected)]
    [InlineData(QuoteStatus.Accepted, QuoteStatus.Converted, QuoteStatus.Accepted)]
    public async Task SetStatus_FollowsWorkflow(QuoteStatus current, QuoteStatus requested, QuoteStatus expected)
    {
        var app = postgres.Shared;
        var id = await NewQuoteAsync(app, current);
        var (client, token) = await AdminAsync(app);

        var response = await client.PostFormAsync($"/yonetim/crm/teklifler/durum/{id}", token, ("status", ((int)requested).ToString()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, await app.WithDbAsync(db => db.Quotes.Where(q => q.Id == id).Select(q => q.Status).SingleAsync()));
    }

    [Fact]
    public async Task Details_OffersOnlyAllowedStatuses()
    {
        var app = postgres.Shared;
        var id = await NewQuoteAsync(app, QuoteStatus.Accepted);
        var (client, _) = await AdminAsync(app);

        var html = await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{id}");
        var options = Regex.Matches(html, "<option value=\"(\\d)\"").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.Equal([(int)QuoteStatus.Accepted], options);
    }
}

[Collection("postgres")]
public sealed class ContactTests(PostgresFixture postgres)
{
    private async Task<HttpResponseMessage> SubmitAsync(string name, string message = "Proje için bilgi almak istiyorum.", string privacy = "true", string website = "")
    {
        var client = postgres.Shared.NewClient();
        var token = await client.GetTokenAsync("/iletisim");
        return await client.PostFormAsync("/iletisim", token,
            ("Name", name), ("ContactInfo", "0555 000 00 00"), ("Message", message), ("PrivacyAccepted", privacy), ("Website", website));
    }

    private Task<bool> IsSavedAsync(string name) => postgres.Shared.WithDbAsync(db => db.ContactMessages.AnyAsync(m => m.Name == name));

    [Fact]
    public async Task ValidMessage_IsSavedAndRedirectsToThanks()
    {
        var response = await SubmitAsync("Gecerli Ziyaretci");
        Assert.Equal("/iletisim/tesekkurler", response.Headers.Location?.OriginalString);
        Assert.True(await IsSavedAsync("Gecerli Ziyaretci"));
    }

    [Fact]
    public async Task Honeypot_LooksSuccessfulButIsNotSaved()
    {
        var response = await SubmitAsync("Bot Ziyaretci", website: "http://spam.example");
        Assert.Equal("/iletisim/tesekkurler", response.Headers.Location?.OriginalString);
        Assert.False(await IsSavedAsync("Bot Ziyaretci"));
    }

    [Theory]
    [InlineData("Kisa Mesaj", "kısa", "true")]
    [InlineData("Onaysiz", "Proje için bilgi almak istiyorum.", "false")]
    public async Task InvalidMessage_IsNotSaved(string name, string message, string privacy)
    {
        var response = await SubmitAsync(name, message, privacy);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await IsSavedAsync(name));
    }
}
