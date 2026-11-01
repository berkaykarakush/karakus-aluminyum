using SkiaSharp;

namespace KarakusAluminyum.Web.Services;

public sealed class ImageFormatException(string message) : InvalidOperationException(message);

public sealed record OptimizedImage(byte[] Main, byte[]? Medium, byte[]? Share, int Width, int Height);

// Her görsel buradan geçer: EXIF temizlenir, yön düzeltilir, sRGB'ye çevrilir ve WebP olarak kaydedilir.
public static class ImageOptimizer
{
    public const int MaxEdge = 1920;
    public const int LogoEdge = 512;
    public const int MediumWidth = 800;
    private const int ShareEdge = 1200;
    private const int PhotoQuality = 82;
    private const int TransparentQuality = 90;
    private const int MediumQuality = 72; // kartlarda küçük gösterilir, ayrıntı kaybı görünmez
    private const int ShareQuality = 82;
    private const long MaxPixels = 60_000_000; // sıkıştırılmış küçük dosyanın belleği doldurmasını önler

    public static string MediumPath(string path) => Path.ChangeExtension(path, null) + $"-{MediumWidth}.webp";
    public static string SharePath(string path) => Path.ChangeExtension(path, null) + "-paylasim.jpg";

    public static OptimizedImage Process(byte[] input, int maxEdge = MaxEdge)
    {
        using var source = Decode(input);
        var hasAlpha = HasTransparency(source);
        using var main = Fit(source, Math.Min(maxEdge, Math.Max(source.Width, source.Height)));
        var quality = hasAlpha ? TransparentQuality : PhotoQuality;
        var encoded = Encode(main, quality);
        // Zaten WebP olan görsel yeniden kodlanınca büyüyorsa aslı korunur.
        if (encoded.Length >= input.Length && IsWebp(input) && main.Width == source.Width && main.Height == source.Height) encoded = input;

        byte[]? medium = null;
        if (main.Width > MediumWidth * 5 / 4)
        {
            using var scaled = Scale(main, MediumWidth, (int)Math.Round(main.Height * (double)MediumWidth / main.Width));
            medium = Encode(scaled, hasAlpha ? TransparentQuality : MediumQuality);
        }

        byte[]? share = null;
        if (!hasAlpha)
        {
            using var fitted = Fit(main, ShareEdge);
            using var image = SKImage.FromBitmap(fitted);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, ShareQuality);
            share = data.ToArray();
        }
        return new OptimizedImage(encoded, medium, share, main.Width, main.Height);
    }

    // Dosya adı değişmez kalır, yalnızca uzantı .webp olur; yazma yarıda kalırsa bozuk dosya görünmez.
    public static async Task WriteAsync(OptimizedImage image, string webpPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(webpPath)!);
        if (image.Medium is not null) await WriteFileAsync(MediumPath(webpPath), image.Medium, cancellationToken);
        if (image.Share is not null) await WriteFileAsync(SharePath(webpPath), image.Share, cancellationToken);
        await WriteFileAsync(webpPath, image.Main, cancellationToken);
    }

    public static void DeleteWithVariants(string webpPath)
    {
        foreach (var path in new[] { webpPath, MediumPath(webpPath), SharePath(webpPath) })
            if (File.Exists(path)) File.Delete(path);
    }

    public static bool IsAnimated(byte[] input)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        return codec is { FrameCount: > 1 };
    }

    private static async Task WriteFileAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsWebp(byte[] input) => input.Length > 12 && input[0] == 'R' && input[1] == 'I' && input[2] == 'F' && input[3] == 'F' && input[8] == 'W' && input[9] == 'E' && input[10] == 'B' && input[11] == 'P';

    private static SKBitmap Decode(byte[] input)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data) ?? throw new ImageFormatException("Görsel okunamadı. Dosya bozuk veya desteklenmeyen biçimde olabilir.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new ImageFormatException("Görsel çözünürlüğü çok yüksek; en fazla 60 megapiksel olabilir.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var raw = new SKBitmap(info);
        var result = codec.GetPixels(info, raw.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new ImageFormatException("Görsel okunamadı. Dosya bozuk olabilir.");
        return Orient(raw, codec.EncodedOrigin);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(result);
        float w = source.Width, h = source.Height;
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        });
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    private static bool HasTransparency(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        var pixels = pixmap.GetPixelSpan<byte>();
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 255) return true;
        return false;
    }

    private static SKBitmap Fit(SKBitmap source, int edge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= edge) return source.Copy();
        var ratio = (double)edge / longest;
        return Scale(source, Math.Max(1, (int)Math.Round(source.Width * ratio)), Math.Max(1, (int)Math.Round(source.Height * ratio)));
    }

    // Büyük küçültmede önce yarıya inilir: tek adımda küçültmek ince çizgileri (profil, cam kenarı) tırtıklı yapar.
    private static SKBitmap Scale(SKBitmap source, int width, int height)
    {
        var current = source;
        while (current.Width >= width * 2 && current.Height >= height * 2)
        {
            var half = current.Resize(new SKImageInfo(current.Width / 2, current.Height / 2, current.ColorType, current.AlphaType, current.ColorSpace), new SKSamplingOptions(SKFilterMode.Linear));
            if (!ReferenceEquals(current, source)) current.Dispose();
            current = half;
        }
        var result = current.Resize(new SKImageInfo(width, height, current.ColorType, current.AlphaType, current.ColorSpace), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (!ReferenceEquals(current, source)) current.Dispose();
        return result;
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));
        return data.ToArray();
    }
}
