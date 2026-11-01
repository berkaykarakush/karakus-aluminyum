using System.Net;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace KarakusAluminyum.Tests;

public static class TestImages
{
    // Düz renk yerine geçiş: gerçek fotoğraf gibi sıkıştırılır.
    public static byte[] Png(int width, int height, bool transparent = false) => Encode(width, height, transparent, SKEncodedImageFormat.Png);
    public static byte[] Jpeg(int width, int height) => Encode(width, height, false, SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(int width, int height, bool transparent, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
            using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), new[] { SKColors.Navy, SKColors.Orange }, SKShaderTileMode.Clamp) };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    // Telefon fotoğrafı gibi: pikseller yatay, EXIF "90 derece döndür" der ve konum benzeri bir metin taşır.
    public static byte[] JpegWithExif(int width, int height)
    {
        var jpeg = Jpeg(width, height);
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x30, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0, 8, 0, 0, 0,
            2, 0,
            0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, // Orientation = 6
            0x0F, 0x01, 2, 0, 4, 0, 0, 0, (byte)'G', (byte)'P', (byte)'S', 0, // Make
            0, 0, 0, 0
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    public static readonly byte[] AnimatedGif =
    [
        (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 1, 0,
        0x3B
    ];
}

public sealed class ImageOptimizerTests
{
    private static SKImageInfo Info(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        return codec.Info;
    }

    private static SKEncodedImageFormat Format(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec.EncodedFormat;
    }

    [Fact]
    public void LargePhoto_IsLimited_AndGetsSmallCopies_WithRatioKept()
    {
        var input = TestImages.Png(4000, 2000);
        var result = ImageOptimizer.Process(input);
        Assert.Equal((1920, 960), (result.Width, result.Height));
        Assert.Equal(SKEncodedImageFormat.Webp, Format(result.Main));
        Assert.True(result.Main.Length < input.Length);
        var medium = Info(result.Medium!);
        Assert.Equal((800, 400), (medium.Width, medium.Height));
        Assert.Equal(SKEncodedImageFormat.Jpeg, Format(result.Share!));
        Assert.Equal(1200, Info(result.Share!).Width);
    }

    [Fact]
    public void SmallPhoto_IsNotUpscaled_AndNeedsNoMediumCopy()
    {
        var result = ImageOptimizer.Process(TestImages.Png(600, 400));
        Assert.Equal((600, 400), (result.Width, result.Height));
        Assert.Null(result.Medium);
    }

    [Fact]
    public void TransparentLogo_KeepsTransparency_AndHasNoJpegCopy()
    {
        var result = ImageOptimizer.Process(TestImages.Png(1000, 500, transparent: true), ImageOptimizer.LogoEdge);
        Assert.Equal(512, result.Width);
        Assert.Null(result.Share);
        using var decoded = SKBitmap.Decode(result.Main);
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void PhonePhoto_IsRotated_AndExifIsRemoved()
    {
        var input = TestImages.JpegWithExif(400, 200);
        var result = ImageOptimizer.Process(input);
        Assert.Equal((200, 400), (result.Width, result.Height));
        Assert.Contains("GPS", System.Text.Encoding.ASCII.GetString(input));
        Assert.DoesNotContain("GPS", System.Text.Encoding.ASCII.GetString(result.Main));
        Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(result.Main));
    }

    [Fact]
    public void OptimizedWebp_NeverGrows()
    {
        var once = ImageOptimizer.Process(TestImages.Png(1000, 600)).Main;
        Assert.True(ImageOptimizer.Process(once).Main.Length <= once.Length);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(new byte[] { 0x3C, 0x3F, 0x70, 0x68, 0x70 })] // <?php
    [InlineData(new byte[0])]
    public void NonImageContent_IsRejected(byte[] input) => Assert.Throws<ImageFormatException>(() => ImageOptimizer.Process(input));

    [Fact]
    public void HugeResolution_IsRejected() => Assert.Throws<ImageFormatException>(() => ImageOptimizer.Process(TestImages.Png(8000, 8000)));

    [Fact]
    public void VariantPaths_AreDerivedFromMainPath()
    {
        Assert.Equal("/uploads/a-800.webp", ImageOptimizer.MediumPath("/uploads/a.webp"));
        Assert.Equal("/images/sürme-paylasim.jpg", ImageOptimizer.SharePath("/images/sürme.webp"));
    }
}

[Collection("postgres")]
public sealed class ImageUploadAndMigrationTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail, AppFactory.Password);
        return (client, await client.GetTokenAsync("/yonetim/galeri/ekle"));
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string title, string fileName, byte[] bytes) => client.PostAsync("/yonetim/galeri/ekle", new MultipartFormDataContent
    {
        { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(title), "Title" }, { new StringContent("0"), "SortOrder" }, { new StringContent("true"), "IsPublished" },
        { new ByteArrayContent(bytes), "file", fileName }
    });

    private static string Tag() => "G" + Guid.NewGuid().ToString("N")[..8];
    private static string Physical(AppFactory app, string url) => Path.Combine(app.WebRoot, url.TrimStart('/'));

    [Fact]
    public async Task GalleryUpload_StoresWebpWithVariants_ServesSrcset_AndDeleteRemovesAll()
    {
        await using var app = postgres.CreateApp("image_upload");
        var (client, token) = await AdminAsync(app);
        var title = Tag();
        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(client, token, title, "buyuk-foto.jpg", TestImages.Jpeg(3000, 2000))).StatusCode);
        var image = await app.WithDbAsync(db => db.GalleryImages.AsNoTracking().SingleAsync(x => x.Title == title));
        Assert.EndsWith(".webp", image.ImagePath);
        var main = Physical(app, image.ImagePath);
        var medium = ImageOptimizer.MediumPath(main);
        var share = ImageOptimizer.SharePath(main);
        Assert.All(new[] { main, medium, share }, x => Assert.True(File.Exists(x)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(main)!, "*.tmp"));

        var served = await client.GetAsync(image.ImagePath);
        Assert.Equal("image/webp", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", served.Headers.CacheControl?.ToString());

        var page = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync("/galeri?k=genel"));
        Assert.Contains($"srcset=\"{ImageOptimizer.MediumPath(image.ImagePath)} 800w, {image.ImagePath} 1920w\"", page);
        Assert.DoesNotContain("img-sizes", page);

        await client.PostFormAsync($"/yonetim/galeri/sil/{image.Id}", token);
        Assert.All(new[] { main, medium, share }, x => Assert.False(File.Exists(x)));
    }

    [Fact]
    public async Task FakeImageWithImageExtension_IsRejected_AndNothingIsSaved()
    {
        await using var app = postgres.CreateApp("image_fake");
        var (client, token) = await AdminAsync(app);
        var title = Tag();
        var response = await UploadAsync(client, token, title, "kabuk.png", "<?php echo 1;"u8.ToArray());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("okunamadı", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.False(await app.WithDbAsync(db => db.GalleryImages.AnyAsync(x => x.Title == title)));
        Assert.False(Directory.Exists(Path.Combine(app.WebRoot, "uploads", "galeri", "genel")) && Directory.GetFiles(Path.Combine(app.WebRoot, "uploads", "galeri", "genel")).Length > 0);
    }

    [Fact]
    public async Task AdminUpload_ConvertsLogo_AndKeepsAnimatedGif()
    {
        var root = Directory.CreateTempSubdirectory("gorsel-").FullName;
        try
        {
            await using var png = new MemoryStream(TestImages.Png(300, 100));
            var logo = await AdminImageUpload.SaveOptimizedAsync(new Microsoft.AspNetCore.Http.FormFile(png, 0, png.Length, "f", "logo.png"), root, "uploads/images");
            Assert.EndsWith(".webp", logo);
            await using var gif = new MemoryStream(TestImages.AnimatedGif);
            var animated = await AdminImageUpload.SaveOptimizedAsync(new Microsoft.AspNetCore.Http.FormFile(gif, 0, gif.Length, "f", "hareket.gif"), root, "uploads/images");
            Assert.EndsWith(".gif", animated);
            Assert.Equal(TestImages.AnimatedGif, await File.ReadAllBytesAsync(Path.Combine(root, animated.TrimStart('/'))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ServiceImage_UsesJpegForSharing_AndWebpOnPage()
    {
        await using var app = postgres.CreateApp("image_share");
        var (client, token) = await AdminAsync(app);
        var title = Tag();
        await UploadAsync(client, token, title, "a.png", TestImages.Png(2000, 1000));
        var path = await app.WithDbAsync(db => db.GalleryImages.Where(x => x.Title == title).Select(x => x.ImagePath).SingleAsync());
        var slug = await app.WithDbAsync(async db =>
        {
            var service = await db.Services.FirstAsync(x => x.IsPublished);
            service.Image = path;
            await db.SaveChangesAsync();
            return service.Slug;
        });
        var page = WebUtility.HtmlDecode(await app.NewClient().GetStringAsync($"/urunler/{slug}"));
        Assert.Contains($"property=\"og:image\" content=\"https://www.karakusaluminyum.com{ImageOptimizer.SharePath(path)}\"", page);
        Assert.Contains($"class=\"page-hero__media\" src=\"{path}\"", page);
        Assert.Contains($"{ImageOptimizer.MediumPath(path)} 800w", page);
    }

    [Fact]
    public async Task LegacyImageAddresses_RedirectToWebp_AndOthersStayMissing()
    {
        await using var app = postgres.CreateApp("image_redirect");
        var folder = Path.Combine(app.WebRoot, "images");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "eski.webp"), ImageOptimizer.Process(TestImages.Png(20, 20)).Main);
        await File.WriteAllBytesAsync(Path.Combine(folder, "sürme.webp"), ImageOptimizer.Process(TestImages.Png(20, 20)).Main);
        var client = app.NewClient();

        var moved = await client.GetAsync("/images/eski.png?x=1");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal("/images/eski.webp?x=1", moved.Headers.Location?.OriginalString);
        Assert.Equal("/images/s%C3%BCrme.webp", (await client.GetAsync("/images/s%C3%BCrme.png")).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/images/yok.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/uploads/eski.png")).StatusCode);
        Assert.Contains("max-age=2592000", (await client.GetAsync("/images/eski.webp")).Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Migration_ConvertsExistingRecords_KeepsOriginals_AndIsRepeatable()
    {
        await using var app = postgres.CreateApp("image_migration");
        app.NewClient();
        var root = Path.Combine(app.WebRoot, "gecis");
        var uploads = Path.Combine(root, "uploads", "images");
        var seeded = Path.Combine(root, "images");
        Directory.CreateDirectory(uploads);
        Directory.CreateDirectory(seeded);
        await File.WriteAllBytesAsync(Path.Combine(uploads, "foto.jpg"), TestImages.Jpeg(2400, 1200));
        await File.WriteAllBytesAsync(Path.Combine(uploads, "bozuk.png"), [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(seeded, "tohum.webp"), ImageOptimizer.Process(TestImages.Png(100, 100)).Main);
        var bigWebp = ImageOptimizer.Process(TestImages.Png(1200, 800)).Main;
        await File.WriteAllBytesAsync(Path.Combine(uploads, "eski-buyuk.webp"), [.. bigWebp]);

        string[] paths = ["/uploads/images/foto.jpg", "/uploads/images/bozuk.png", "/uploads/images/yok.png", "/images/tohum.png", "/images/yok.png", "/uploads/../../disari.png", "https://ornek.com/a.png"];
        var ids = await app.WithDbAsync(async db =>
        {
            var rows = paths.Select(x => new GalleryImage { ImagePath = x, Title = Tag() }).ToList();
            db.GalleryImages.AddRange(rows);
            await db.SaveChangesAsync();
            return rows.Select(x => x.Id).ToList();
        });

        async Task<List<string>> RunAsync()
        {
            await app.WithDbAsync(async db => { await ImageMigration.RunAsync(db, root, NullLogger.Instance); return 0; });
            return await app.WithDbAsync(async db =>
            {
                var rows = await db.GalleryImages.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
                return ids.Select(id => rows.Single(x => x.Id == id).ImagePath).ToList();
            });
        }

        var first = await RunAsync();
        Assert.Equal(new[] { "/uploads/images/foto.webp", "/uploads/images/bozuk.png", "/uploads/images/yok.png", "/images/tohum.webp", "/images/yok.png", "/uploads/../../disari.png", "https://ornek.com/a.png" }, first);
        Assert.True(File.Exists(Path.Combine(uploads, "foto.jpg"))); // asıl dosya silinmez
        Assert.True(File.Exists(Path.Combine(uploads, "foto-800.webp")));

        var stamp = File.GetLastWriteTimeUtc(Path.Combine(uploads, "foto.webp"));
        Assert.Equal(first, await RunAsync());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(uploads, "foto.webp")));
    }
}
