using System.Net;
using KarakusAluminyum.Web.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class LogoNormalizerTests
{
    private static byte[] Draw(SKColor background, Action<SKCanvas> draw, int w = 600, int h = 300)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(background); draw(canvas); }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Mark(SKCanvas canvas, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawCircle(150, 150, 80, paint);
        canvas.DrawRect(280, 110, 240, 80, paint);
    }

    private static (int Black, int Colored) Check(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        var (black, colored) = (0, 0);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha < 128) continue;
                if (c.Red < 30 && c.Green < 30 && c.Blue < 30) black++; else colored++;
            }
        return (black, colored);
    }

    public static TheoryData<string, uint, uint> Variants => new()
    {
        { "beyazda siyah", 0xFFFFFFFF, 0xFF000000 },
        { "siyahta beyaz", 0xFF000000, 0xFFFFFFFF },
        { "beyazda renkli", 0xFFFFFFFF, 0xFFE02020 },
        { "lacivertte beyaz", 0xFF10204A, 0xFFFFFFFF },
        { "seffafta beyaz", 0x00000000, 0xFFFFFFFF },
        { "seffafta siyah", 0x00000000, 0xFF000000 },
        { "seffafta renkli", 0x00000000, 0xFF2060E0 },
        { "griye yakin soluk", 0xFFFFFFFF, 0xFFB0B0B0 },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryVariant_BecomesTheSameBlackLogoOnTransparent(string name, uint background, uint mark)
    {
        var (png, outcome) = LogoNormalizer.Normalize(Draw(new SKColor(background), c => Mark(c, new SKColor(mark))));
        Assert.Equal(LogoOutcome.Black, outcome);
        var (black, colored) = Check(png);
        Assert.True(colored == 0, $"{name}: {colored} renkli piksel");
        // Daire + dikdörtgen alanı: 20.106 + 19.200
        Assert.InRange(black, 37000, 41500);
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width / 2 - 20, 2).Alpha); // şekillerin arası zemin yok
    }

    [Fact]
    public void GradientMark_BecomesSolidBlack()
    {
        var (png, _) = LogoNormalizer.Normalize(Draw(SKColors.White, c =>
        {
            using var dark = new SKPaint { Color = new SKColor(0x1d, 0x35, 0x57) };
            using var shader = SKShader.CreateLinearGradient(new SKPoint(50, 0), new SKPoint(250, 0), new[] { new SKColor(0xe6, 0x39, 0x46), new SKColor(0xf4, 0xa2, 0x61) }, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            c.DrawRect(50, 50, 200, 200, paint);
            c.DrawRect(300, 100, 250, 100, dark);
        }));
        using var bitmap = SKBitmap.Decode(png);
        Assert.True(bitmap.GetPixel(190, bitmap.Height / 2).Alpha > 240); // gradyanın açık tarafı
    }

    [Fact]
    public void Margins_AreCropped()
    {
        var (png, _) = LogoNormalizer.Normalize(Draw(SKColors.White, c => Mark(c, SKColors.Black), 1200, 900));
        using var bitmap = SKBitmap.Decode(png);
        Assert.InRange(bitmap.Width, 445, 455); // 70-520
        Assert.InRange(bitmap.Height, 158, 164);
    }

    [Fact]
    public void WhiteTextInsideColoredBadge_StaysAsHole()
    {
        var (png, outcome) = LogoNormalizer.Normalize(Draw(SKColors.Transparent, c =>
        {
            using var red = new SKPaint { Color = SKColors.Red, IsAntialias = true };
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            c.DrawCircle(300, 150, 140, red);
            c.DrawRect(240, 120, 120, 60, white);
        }));
        Assert.Equal(LogoOutcome.Black, outcome);
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).Alpha);
        Assert.True(bitmap.GetPixel(bitmap.Width / 2, 20).Alpha > 200);
    }

    [Fact]
    public void BlackTextOnWhitePlate_DropsThePlate()
    {
        var (png, outcome) = LogoNormalizer.Normalize(Draw(SKColors.Transparent, c =>
        {
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var black = new SKPaint { Color = SKColors.Black };
            c.DrawCircle(300, 150, 145, white);
            c.DrawRect(220, 120, 160, 60, black);
        }));
        Assert.Equal(LogoOutcome.Black, outcome);
        using var bitmap = SKBitmap.Decode(png);
        Assert.InRange(bitmap.Width, 155, 165); // yalnızca yazı kalır
    }

    [Fact]
    public void InnerWhiteOnWhiteBackground_StaysEmpty()
    {
        var (png, _) = LogoNormalizer.Normalize(Draw(SKColors.White, c =>
        {
            using var green = new SKPaint { Color = SKColors.Green };
            using var white = new SKPaint { Color = SKColors.White };
            c.DrawRect(100, 50, 400, 200, green);
            c.DrawRect(250, 120, 100, 60, white);
        }));
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).Alpha);
    }

    [Fact]
    public void JpegNoise_DoesNotBecomeVisible()
    {
        using var source = SKBitmap.Decode(Draw(SKColors.White, c => Mark(c, SKColors.Black)));
        using var image = SKImage.FromBitmap(source);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 60);
        var (png, outcome) = LogoNormalizer.Normalize(jpeg.ToArray());
        Assert.Equal(LogoOutcome.Black, outcome);
        Assert.InRange(Check(png).Black, 37000, 42500);
    }

    [Theory]
    [InlineData("foto")]
    [InlineData("tek renk")]
    public void Unseparable_FallsBackToGrayscale(string kind)
    {
        var input = kind == "tek renk" ? Draw(SKColors.White, _ => { }) : Draw(SKColors.White, c =>
        {
            var random = new Random(7);
            using var paint = new SKPaint();
            for (var i = 0; i < 4000; i++)
            {
                paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                c.DrawRect(random.Next(600), random.Next(300), 12, 12, paint);
            }
        });
        var (png, outcome) = LogoNormalizer.Normalize(input);
        Assert.Equal(LogoOutcome.Grayscale, outcome);
        using var bitmap = SKBitmap.Decode(png);
        var c0 = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(c0.Red == c0.Green && c0.Green == c0.Blue);
    }

    [Fact]
    public void NonImage_IsRejected() => Assert.Throws<ImageFormatException>(() => LogoNormalizer.Normalize([1, 2, 3]));
}

[Collection("postgres")]
public sealed class ReferenceLogoUploadTests(PostgresFixture postgres)
{
    private static byte[] Png(SKColor background, SKColor mark)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(600, 300, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            using var paint = new SKPaint { Color = mark };
            canvas.DrawRect(100, 80, 400, 140, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(string Path, HttpResponseMessage Response, HttpClient Client)> UploadAsync(AppFactory app, byte[] logo, bool keepColors = false)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail, AppFactory.Password);
        var token = await client.GetTokenAsync("/yonetim/referanslar/ekle");
        var name = "L" + Guid.NewGuid().ToString("N")[..8];
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent(name), "Name" }, { new StringContent("0"), "SortOrder" }, { new StringContent("true"), "IsPublished" }, { new ByteArrayContent(logo), "logoFile", "logo.png" } };
        if (keepColors) form.Add(new StringContent("true"), "keepColors");
        var response = await client.PostAsync("/yonetim/referanslar/ekle", form);
        var path = await app.WithDbAsync(db => db.ClientReferences.Where(x => x.Name == name).Select(x => x.LogoPath!).SingleAsync());
        return (path, response, client);
    }

    [Fact]
    public async Task WhiteLogoOnBlack_IsStoredBlack()
    {
        await using var app = postgres.CreateApp("logo_black");
        var (path, _, _) = await UploadAsync(app, Png(SKColors.Black, SKColors.White));
        Assert.EndsWith(".webp", path);
        using var bitmap = SKBitmap.Decode(Path.Combine(app.WebRoot, path.TrimStart('/')));
        Assert.Equal((400, 140), (bitmap.Width, bitmap.Height));
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(center.Alpha > 240 && center.Red < 30, center.ToString());
    }

    [Fact]
    public async Task KeepColorsOption_LeavesLogoAsIs()
    {
        await using var app = postgres.CreateApp("logo_keep");
        var (path, _, _) = await UploadAsync(app, Png(SKColors.White, SKColors.Red), keepColors: true);
        using var bitmap = SKBitmap.Decode(Path.Combine(app.WebRoot, path.TrimStart('/')));
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(center.Red > 200 && center.Green < 60, center.ToString());
    }

    [Fact]
    public async Task UnseparableLogo_TellsTheAdmin()
    {
        await using var app = postgres.CreateApp("logo_notice");
        using var plain = new SKBitmap(new SKImageInfo(300, 150, SKColorType.Rgba8888, SKAlphaType.Premul));
        var random = new Random(3);
        using (var canvas = new SKCanvas(plain))
        {
            using var paint = new SKPaint();
            for (var i = 0; i < 2000; i++) { paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)); canvas.DrawRect(random.Next(300), random.Next(150), 8, 8, paint); }
        }
        using var image = SKImage.FromBitmap(plain);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var (_, response, client) = await UploadAsync(app, data.ToArray());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("gri tonlu", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/referanslar")));
    }
}
