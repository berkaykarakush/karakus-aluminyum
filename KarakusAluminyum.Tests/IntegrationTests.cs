using System.Net;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using KarakusAluminyum.Web.Controllers;
using KarakusAluminyum.Web.Models.Analytics;
using KarakusAluminyum.Web.Models.Documents;
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
    [InlineData("/yonetim/raporlar", true)]
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

public sealed class ReportRangeTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 21, 30, 0, DateTimeKind.Utc); // İstanbul'da 31 Ekim 00:30

    [Fact]
    public void Range_UsesTurkeyDayBoundaries()
    {
        var range = ReportRange.Parse("7", null, null, Now);
        Assert.Equal(new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc), range.ToUtc);
        Assert.Equal(7, range.Days);
        Assert.Equal("2026-10-25", range.FromText);
        Assert.Equal("2026-10-31", range.ToText);
        Assert.Equal(range.FromUtc, range.Previous.ToUtc);
    }

    [Theory]
    [InlineData("ozel", "2026-10-10", "2026-10-01")] // ters tarih
    [InlineData("ozel", "bozuk", "2026-10-01")]
    [InlineData("ozel", "2000-01-01", "2026-01-01")] // çok uzun
    [InlineData("yok", null, null)]
    public void InvalidRange_FallsBackToThirtyDays(string key, string? from, string? to)
        => Assert.Equal(30, ReportRange.Parse(key, from, to, Now).Days);

    [Fact]
    public void AcceptanceRate_ExcludesDrafts_AndCountsConverted()
    {
        Assert.Null(ReportService.AcceptanceRate(new[] { QuoteStatus.Draft }));
        Assert.Equal(50m, ReportService.AcceptanceRate(new[] { QuoteStatus.Draft, QuoteStatus.Sent, QuoteStatus.Converted }));
        Assert.Equal(33.3m, ReportService.AcceptanceRate(new[] { QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Expired }));
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+90 555", "'+90 555")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("düz", "düz")]
    public void CsvCell_PreventsFormulaInjection(string value, string expected)
        => Assert.Equal(expected, KarakusAluminyum.Web.Areas.Admin.Controllers.ReportsController.Cell(value));

    [Theory]
    [InlineData("/urunler/x?utm=1#a", "/urunler/x")]
    [InlineData("/yonetim/ayarlar", null)]
    [InlineData("//evil.com", null)]
    [InlineData("https://evil.com", null)]
    [InlineData(null, null)]
    public void AnalyticsPath_IsCleaned(string? path, string? expected)
        => Assert.Equal(expected, AnalyticsController.CleanPath(path));

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS) Mobile", "mobile")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS)", "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0)", "desktop")]
    public void Device_IsDetected(string userAgent, string expected) => Assert.Equal(expected, AnalyticsController.DeviceOf(userAgent));

    [Theory]
    [InlineData("Ga4MeasurementId", "G-ABC123", true)]
    [InlineData("Ga4MeasurementId", "", true)]
    [InlineData("Ga4MeasurementId", "UA-123", false)]
    [InlineData("Ga4MeasurementId", "G-ABC');alert(1)//", false)]
    [InlineData("MetaPixelId", "1234567890", true)]
    [InlineData("MetaPixelId", "12a", false)]
    [InlineData("BaseUrl", "javascript:alert(1)", false)]
    [InlineData("BaseUrl", "https://www.ornek.com", true)]
    public void SettingValidation_RejectsUnsafeValues(string key, string value, bool valid)
        => Assert.Equal(valid, SiteSettingCatalog.Validate(SiteSettingCatalog.Find(key)!, value) is null);

    [Fact]
    public void Catalog_HasNoPersonalIdsInDefaults()
    {
        Assert.Equal("", SiteSettingCatalog.Find("Ga4MeasurementId")!.Default);
        Assert.Equal("", SiteSettingCatalog.Find("MetaPixelId")!.Default);
    }
}

[Collection("postgres")]
public sealed class AdminPanelTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/ayarlar"));
    }

    private static Task<HttpResponseMessage> SaveSettingsAsync(HttpClient client, string token, params (string Key, string Value)[] values)
        => client.PostFormAsync("/yonetim/ayarlar/kaydet", token, values.Select(v => ($"values[{v.Key}]", v.Value)).ToArray());

    private static Task<string> SettingAsync(AppFactory app, string key) => app.WithDbAsync(db => db.SiteSettingRecords.Where(s => s.Key == key).Select(s => s.Value).SingleAsync());

    // Regresyon: yöneticinin değerleri her yeniden başlatmada koddakilerle eziliyordu.
    [Fact]
    public async Task EditedSettings_SurviveRestart()
    {
        await using (var first = postgres.CreateApp("settings_restart"))
        {
            var (client, token) = await AdminAsync(first);
            Assert.Equal(HttpStatusCode.Redirect, (await SaveSettingsAsync(client, token, ("PhoneDisplay", "+90 111 222 33 44"), ("BaseUrl", "https://www.ornek-alan.com"), ("HeroTitle", "Yeni başlık"), ("InstagramUrl", ""))).StatusCode);
        }

        await using var second = postgres.CreateApp("settings_restart");
        second.NewClient();
        Assert.Equal("+90 111 222 33 44", await SettingAsync(second, "PhoneDisplay"));
        Assert.Equal("", await SettingAsync(second, "InstagramUrl"));

        var client2 = second.NewClient();
        var home = WebUtility.HtmlDecode(await client2.GetStringAsync("/"));
        Assert.Contains("Yeni başlık", home);
        Assert.Contains("+90 111 222 33 44", home);
    }

    // Regresyon: site haritası ve robots appsettings'teki yer tutucu alan adını kullanıyordu.
    [Fact]
    public async Task SitemapRobotsAndSchema_UseDatabaseDomain()
    {
        await using var app = postgres.CreateApp("settings_domain");
        var (client, token) = await AdminAsync(app);
        await SaveSettingsAsync(client, token, ("BaseUrl", "https://www.ornek-alan.com"));
        var anonymous = app.NewClient();

        var sitemap = await anonymous.GetStringAsync("/sitemap.xml");
        Assert.Contains("https://www.ornek-alan.com/galeri", sitemap);
        Assert.DoesNotContain("DOMAININIZ", sitemap);
        Assert.Contains("https://www.ornek-alan.com/sitemap.xml", await anonymous.GetStringAsync("/robots.txt"));
        var home = await anonymous.GetStringAsync("/");
        Assert.DoesNotContain("DOMAININIZ", home);
        Assert.DoesNotContain("XXX", home);
    }

    [Fact]
    public async Task InvalidTrackingId_IsRejected_AndNothingIsSaved()
    {
        await using var app = postgres.CreateApp("settings_invalid");
        var (client, token) = await AdminAsync(app);
        var response = await SaveSettingsAsync(client, token, ("Ga4MeasurementId", "G-1');alert(1)//"), ("PhoneDisplay", "Değişmemeli"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await SettingAsync(app, "Ga4MeasurementId"));
        Assert.NotEqual("Değişmemeli", await SettingAsync(app, "PhoneDisplay"));
    }

    [Fact]
    public async Task TrackingConfig_AppearsForVisitors_ButNotForAdmins_OrNoindexPages()
    {
        await using var app = postgres.CreateApp("settings_tracking");
        var (client, token) = await AdminAsync(app);
        await SaveSettingsAsync(client, token, ("Ga4MeasurementId", "G-TEST12345"), ("MetaPixelId", "1234567890"), ("FirstPartyAnalyticsEnabled", "true"));

        Assert.DoesNotContain("tracking-config", await client.GetStringAsync("/"));
        var visitor = app.NewClient();
        var home = await visitor.GetStringAsync("/");
        Assert.Contains("id=\"tracking-config\"", home);
        Assert.Contains("G-TEST12345", home);
        Assert.Contains("1234567890", home);
        Assert.Contains("id=\"consent-banner\"", home);
        // Google ve Meta kodu sayfaya gömülmez; onaydan sonra tracking.js yükler.
        Assert.DoesNotContain("googletagmanager.com", home);
        Assert.DoesNotContain("connect.facebook.net", home);
        Assert.DoesNotContain("tracking-config", await visitor.GetStringAsync("/yonetim-giris"));
        Assert.Contains("\"event\":\"view_service\"", await visitor.GetStringAsync("/urunler/aluminyum-dograma"));
    }

    [Fact]
    public async Task AllTrackingOff_RemovesBanner()
    {
        await using var app = postgres.CreateApp("settings_tracking_off");
        var (client, token) = await AdminAsync(app);
        await SaveSettingsAsync(client, token, ("PhoneDisplay", "+90 111"));
        Assert.Equal("false", await SettingAsync(app, "FirstPartyAnalyticsEnabled")); // onay kutusu gönderilmedi
        Assert.DoesNotContain("consent-banner", await app.NewClient().GetStringAsync("/"));
    }

    [Fact]
    public async Task LeadEvent_OnlyAfterRealFormSubmission()
    {
        var app = postgres.Shared;
        var visitor = app.NewClient();
        Assert.DoesNotContain("\"event\":\"lead\"", await visitor.GetStringAsync("/iletisim/tesekkurler"));
        var token = await visitor.GetTokenAsync("/iletisim");
        await visitor.PostFormAsync("/iletisim", token, ("Name", "Dönüşüm Testi"), ("ContactInfo", "0555"), ("Message", "Proje için bilgi almak istiyorum."), ("PrivacyAccepted", "true"), ("Website", ""));
        Assert.Contains("\"event\":\"lead\"", await visitor.GetStringAsync("/iletisim/tesekkurler"));
    }

    private static HttpRequestMessage Beacon(object body, string userAgent = "Mozilla/5.0 (Windows NT 10.0)", string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/t") { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    [Fact]
    public async Task AnalyticsEndpoint_StoresAnonymousEvent_AndIgnoresBadRequests()
    {
        await using var app = postgres.CreateApp("analytics_endpoint");
        var client = app.NewClient();
        var sid = Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Beacon(new { type = "pageview", path = "/urunler/x?gizli=1", referrer = "https://www.google.com/search?q=gizli", utmSource = "Instagram", sessionId = sid }, "Mozilla/5.0 (iPhone) Mobile"))).StatusCode);
        var stored = await app.WithDbAsync(db => db.AnalyticsEvents.AsNoTracking().SingleAsync());
        Assert.Equal("/urunler/x", stored.Path);
        Assert.Equal("google.com", stored.ReferrerHost);
        Assert.Equal("mobile", stored.Device);
        Assert.Equal("Instagram", stored.UtmSource);

        foreach (var bad in new[]
        {
            Beacon(new { type = "uydurma", path = "/", sessionId = sid }),
            Beacon(new { type = "pageview", path = "/", sessionId = "kisa" }),
            Beacon(new { type = "pageview", path = "/yonetim/gizli", sessionId = sid }),
            Beacon(new { type = "pageview", path = "/", sessionId = sid }, "Googlebot/2.1"),
            Beacon(new { type = "pageview", path = "/", sessionId = sid }, origin: "https://baska-site.com")
        })
            await client.SendAsync(bad);
        Assert.Equal(1, await app.WithDbAsync(db => db.AnalyticsEvents.CountAsync()));
    }

    [Fact]
    public async Task AnalyticsEndpoint_RespectsDisabledSetting()
    {
        await using var app = postgres.CreateApp("analytics_disabled");
        var (client, token) = await AdminAsync(app);
        await SaveSettingsAsync(client, token, ("PhoneDisplay", "+90 1")); // kutu işaretsiz: kapalı
        await app.NewClient().SendAsync(Beacon(new { type = "pageview", path = "/", sessionId = Guid.NewGuid().ToString("N") }));
        Assert.Equal(0, await app.WithDbAsync(db => db.AnalyticsEvents.CountAsync()));
    }

    [Fact]
    public async Task Reports_CalculateFromDatabase_WithHandCheckedNumbers()
    {
        await using var app = postgres.CreateApp("reports_numbers");
        var now = DateTime.UtcNow;
        await app.WithDbAsync(async db =>
        {
            var customer = new Customer { Name = "Rapor Müşteri", CreatedUtc = now.AddDays(-2) };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            db.Quotes.AddRange(
                new Quote { QuoteNumber = "R1", Title = "a", CustomerId = customer.Id, Status = QuoteStatus.Accepted, GrandTotal = 1000, CreatedUtc = now.AddDays(-2) },
                new Quote { QuoteNumber = "R2", Title = "b", CustomerId = customer.Id, Status = QuoteStatus.Rejected, GrandTotal = 3000, CreatedUtc = now.AddDays(-3) },
                new Quote { QuoteNumber = "R3", Title = "c", CustomerId = customer.Id, Status = QuoteStatus.Draft, GrandTotal = 500, CreatedUtc = now.AddDays(-4) },
                new Quote { QuoteNumber = "R4", Title = "önceki", CustomerId = customer.Id, Status = QuoteStatus.Sent, GrandTotal = 9000, CreatedUtc = now.AddDays(-40) },
                new Quote { QuoteNumber = "R5", Title = "çok eski", CustomerId = customer.Id, Status = QuoteStatus.Sent, GrandTotal = 7777, CreatedUtc = now.AddDays(-200) });
            db.Contracts.AddRange(
                new Contract { ContractNumber = "S1", CustomerId = customer.Id, Title = "s", BuyerName = "x", Status = ContractStatus.Signed, TotalAmount = 1000, DocumentDate = now.AddDays(-1), DocumentJson = "{\"payments\":[{\"kind\":\"cash\",\"amount\":400},{\"kind\":\"check\",\"amount\":600,\"count\":2}]}" },
                new Contract { ContractNumber = "S2", CustomerId = customer.Id, Title = "t", BuyerName = "x", Status = ContractStatus.Draft, TotalAmount = 5000, DocumentDate = now.AddDays(-1) });
            var s1 = "sessionsessionone1"; var s2 = "sessionsessiontwo2";
            db.AnalyticsEvents.AddRange(
                new AnalyticsEvent { Type = "pageview", Path = "/", SessionId = s1, UtmSource = "Instagram", OccurredUtc = now.AddHours(-5) },
                new AnalyticsEvent { Type = "pageview", Path = "/urunler", SessionId = s1, OccurredUtc = now.AddHours(-4) },
                new AnalyticsEvent { Type = "lead", Path = "/iletisim", SessionId = s1, OccurredUtc = now.AddHours(-3) },
                new AnalyticsEvent { Type = "pageview", Path = "/", SessionId = s2, ReferrerHost = "google.com", Device = "mobile", OccurredUtc = now.AddHours(-2) });
            db.ContactMessages.Add(new ContactMessage { Name = "Mesaj", ContactInfo = "1", Message = "m", Service = "PVC", CreatedUtc = now.AddDays(-1) });
            return await db.SaveChangesAsync();
        });

        await using var scope = app.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<ReportService>().BuildAsync(ReportRange.Parse("30", null, null));

        Assert.Equal(3, report.Sales.QuoteCount);
        Assert.Equal(4500m, report.Sales.QuoteTotal);       // 1000 + 3000 + 500; aralık dışındakiler hariç
        Assert.Equal(1500m, report.Sales.AverageQuote);
        Assert.Equal(50m, report.Sales.AcceptanceRate);     // Kabul 1 / (kabul + red) 2; taslak hariç
        Assert.Equal(1, report.Sales.PreviousQuoteCount);
        Assert.Equal(1000m, report.Sales.SignedTotal);      // taslak sözleşme sayılmaz
        Assert.Equal(new[] { "Çek", "Peşin" }, report.Sales.PaymentsByKind.Select(p => p.Label));
        Assert.Equal(600m, report.Sales.PaymentsByKind[0].Value);
        Assert.Equal(2, report.Web.Sessions);
        Assert.Equal(3, report.Web.PageViews);
        Assert.Equal(1, report.Web.Leads);
        Assert.Equal(50m, report.Web.ConversionRate);
        Assert.Equal("instagram", Assert.Single(report.Web.LeadsBySource).Label); // formun kaynağı oturumun ilk kaynağıdır
        Assert.Contains(report.Web.Sources, s => s.Label == "google.com");
        Assert.Equal(1, report.Contact.MessageCount);
        Assert.Equal(4500m, report.Sales.Chart.Series[0].Values.Sum());
        Assert.Equal(5, report.Sales.Chart.Labels.Count); // 30 gün haftalık gruplanır
        Assert.Equal(1m, report.Contact.Chart.Series[0].Values.Sum());
    }

    [Fact]
    public async Task ReportPages_RenderWithAndWithoutData_AndCsvDownloads()
    {
        var (client, _) = await AdminAsync(postgres.Shared);
        foreach (var url in new[] { "/yonetim", "/yonetim/raporlar", "/yonetim/raporlar?aralik=7", "/yonetim/raporlar?aralik=365", "/yonetim/raporlar?aralik=ozel&bas=2026-01-01&bit=2026-02-01" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);

        foreach (var section in new[] { "satis", "uretim", "iletisim", "web" })
        {
            var csv = await client.GetAsync($"/yonetim/raporlar/csv?bolum={section}");
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Search_FindsRecords_AndTreatsWildcardsLiterally()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        var name = "Arananfirma" + Guid.NewGuid().ToString("N")[..6];
        await app.WithDbAsync(async db => { db.Customers.Add(new Customer { Name = name }); return await db.SaveChangesAsync(); });

        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/ara?q={name.ToLowerInvariant()}")));
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/ara?q=%25%25")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/ara?q=a")).StatusCode);
    }

    [Theory]
    [InlineData("/yonetim/raporlar")]
    [InlineData("/yonetim/raporlar/csv")]
    [InlineData("/yonetim/ara?q=x")]
    public async Task ProtectedPages_RedirectAnonymous(string url)
        => Assert.Contains("/yonetim-giris", (await postgres.Shared.NewClient().GetAsync(url)).Headers.Location!.ToString());

    [Fact]
    public async Task Sidebar_ShowsGroups_AndHidesAdminGroupFromEditors()
    {
        var app = postgres.Shared;
        var (admin, _) = await AdminAsync(app);
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim/crm/teklifler"));
        foreach (var group in new[] { "Satış", "Üretim", "Site içeriği", "Raporlar", "Yönetim" }) Assert.Contains(group, html);
        Assert.Contains("data-nav-group=\"sales\" data-active=\"1\"", html);

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string email = "menu-editor@test.local";
        if (await users.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser { UserName = email, Email = email };
            await users.CreateAsync(user, AppFactory.Password);
            await users.AddToRoleAsync(user, "Editor");
        }
        var editor = app.NewClient();
        await editor.LoginAsync(email);
        var editorHtml = await editor.GetStringAsync("/yonetim");
        Assert.DoesNotContain("data-nav-group=\"admin\"", editorHtml);
    }
}
