using SkiaSharp;

namespace KarakusAluminyum.Web.Services;

public enum LogoOutcome { Black, Grayscale }

// Referans logolarını tek görünüme getirir: zemin kaldırılır, logo şeffaf zeminde siyah olur.
// Siyah zeminde beyaz, beyaz zeminde siyah, şeffaf veya renkli logoların hepsi aynı sonucu verir.
public static class LogoNormalizer
{
    private const int WorkEdge = 1600;
    private const int Solid = 128;

    public static (byte[] Png, LogoOutcome Outcome) Normalize(byte[] input)
    {
        using var decoded = ImageOptimizer.Decode(input);
        using var bitmap = ImageOptimizer.Fit(decoded, WorkEdge);
        var (w, h) = (bitmap.Width, bitmap.Height);
        var pixels = Read(bitmap);
        var transparent = pixels.Count(p => p.A < 32);
        var ink = transparent > w * h * 0.03 ? FromTransparent(pixels, w, h) : FromOpaque(pixels, w, h);
        if (ink is not null && IsReasonable(ink)) return (Encode(ink, w, h), LogoOutcome.Black);
        return (Grayscale(pixels, w, h), LogoOutcome.Grayscale);
    }

    private readonly record struct Px(float R, float G, float B, byte A)
    {
        public float Luma => 0.2126f * R + 0.7152f * G + 0.0722f * B;
        public float Saturation => Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B));
        public bool NearWhite => A >= Solid && Luma > 0.82f && Saturation < 0.16f;
        public float Distance(Px o) => MathF.Sqrt(((R - o.R) * (R - o.R) + (G - o.G) * (G - o.G) + (B - o.B) * (B - o.B)) / 3f);
    }

    private static Px[] Read(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        var bytes = pixmap.GetPixelSpan<byte>();
        var result = new Px[bitmap.Width * bitmap.Height];
        for (var i = 0; i < result.Length; i++)
        {
            var a = bytes[i * 4 + 3];
            // Çözülen pikseller alfa ile çarpılmıştır; renk karşılaştırması için geri açılır.
            var scale = a == 0 ? 0f : 1f / a;
            result[i] = new Px(Math.Min(bytes[i * 4] * scale, 1f), Math.Min(bytes[i * 4 + 1] * scale, 1f), Math.Min(bytes[i * 4 + 2] * scale, 1f), a);
        }
        return result;
    }

    private static IEnumerable<int> Edge(int w, int h)
    {
        for (var x = 0; x < w; x++) { yield return x; yield return (h - 1) * w + x; }
        for (var y = 1; y < h - 1; y++) { yield return y * w; yield return y * w + w - 1; }
    }

    // Şeffaf zemin: logonun şekli zaten alfa kanalındadır, yalnızca iç boşluklar ayıklanır.
    private static byte[]? FromTransparent(Px[] p, int w, int h)
    {
        var boundary = new List<int>();
        for (var i = 0; i < p.Length; i++)
            if (p[i].A >= Solid && Neighbors(i, w, h).Any(n => p[n].A < Solid)) boundary.Add(i);
        if (boundary.Count == 0) return null;

        // Şeffaf zemine oturtulmuş beyaz plaka: plaka zemin sayılır, üzerindeki yazı logodur.
        var plate = boundary.Count(i => p[i].NearWhite) >= boundary.Count * 0.8;
        var dark = p.Count(x => x.A >= Solid && !x.NearWhite);
        if (plate && dark > p.Count(x => x.A >= Solid) * 0.01)
        {
            var white = new Px(1, 1, 1, 255);
            return FromBackground(p, white, x => x.A >= Solid);
        }

        // Dışarıyla temas eden beyaz logonun parçasıdır (koyu zemin için hazırlanmış beyaz yazı).
        // Renkli şeklin içinde kalan beyaz ise boşluktur (kırmızı rozetin içindeki beyaz yazı).
        var outerWhite = new bool[p.Length];
        var queue = new Queue<int>();
        foreach (var i in boundary.Where(i => p[i].NearWhite)) { outerWhite[i] = true; queue.Enqueue(i); }
        while (queue.Count > 0)
            foreach (var n in Neighbors(queue.Dequeue(), w, h))
                if (!outerWhite[n] && p[n].NearWhite) { outerWhite[n] = true; queue.Enqueue(n); }

        var ink = new byte[p.Length];
        for (var i = 0; i < p.Length; i++)
            ink[i] = p[i].NearWhite && !outerWhite[i] ? (byte)0 : p[i].A;
        return ink;
    }

    // Opak zemin: kenarlardaki baskın renk zemindir. Kenarlar tek renk değilse görsel fotoğraf gibidir.
    private static byte[]? FromOpaque(Px[] p, int w, int h)
    {
        var edge = Edge(w, h).Where(i => p[i].A >= Solid).ToList();
        if (edge.Count == 0) return null;
        var bucket = edge.GroupBy(i => ((int)(p[i].R * 15), (int)(p[i].G * 15), (int)(p[i].B * 15))).OrderByDescending(g => g.Count()).First().ToList();
        var background = new Px((float)bucket.Average(i => p[i].R), (float)bucket.Average(i => p[i].G), (float)bucket.Average(i => p[i].B), 255);
        if (edge.Count(i => p[i].Distance(background) < 0.12f) < edge.Count * 0.55) return null;
        return FromBackground(p, background, _ => true);
    }

    // Zeminden uzaklık saydamlığı belirler; soluk logolar da tam siyaha çıksın diye en uzak tona göre ölçeklenir.
    private static byte[] FromBackground(Px[] p, Px background, Func<Px, bool> include)
    {
        var distances = p.Select(x => include(x) ? x.Distance(background) : 0f).ToArray();
        var far = distances.Where(d => d > 0.06f).OrderBy(d => d).ToList();
        var top = far.Count == 0 ? 1f : far[(int)(far.Count * 0.95)];
        var ink = new byte[p.Length];
        for (var i = 0; i < p.Length; i++)
        {
            var value = (distances[i] - 0.06f) / Math.Max(top * 0.3f, 0.05f);
            ink[i] = (byte)(Math.Clamp(value, 0f, 1f) * 255f * (p[i].A / 255f));
        }
        return ink;
    }

    private static IEnumerable<int> Neighbors(int i, int w, int h)
    {
        var (x, y) = (i % w, i / w);
        if (x > 0) yield return i - 1;
        if (x < w - 1) yield return i + 1;
        if (y > 0) yield return i - w;
        if (y < h - 1) yield return i + w;
    }

    // Neredeyse boş veya neredeyse tamamen dolu sonuç logo değildir: otomatik ayırma başarısız sayılır.
    private static bool IsReasonable(byte[] ink)
    {
        var solid = ink.Count(a => a >= Solid);
        return solid > ink.Length * 0.001 && solid < ink.Length * 0.92;
    }

    private static byte[] Encode(byte[] ink, int w, int h)
    {
        // Logolar aynı boyda görünsün diye çevresindeki boşluk kırpılır.
        int left = w, right = -1, top = h, bottom = -1;
        for (var i = 0; i < ink.Length; i++)
        {
            if (ink[i] < 16) continue;
            var (x, y) = (i % w, i / w);
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        var (cw, ch) = (right - left + 1, bottom - top + 1);
        using var output = new SKBitmap(new SKImageInfo(cw, ch, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var pixmap = output.PeekPixels())
        {
            var span = pixmap.GetPixelSpan<byte>();
            for (var y = 0; y < ch; y++)
                for (var x = 0; x < cw; x++)
                {
                    var o = (y * cw + x) * 4;
                    span[o] = span[o + 1] = span[o + 2] = 0;
                    span[o + 3] = ink[(y + top) * w + x + left];
                }
        }
        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] Grayscale(Px[] p, int w, int h)
    {
        using var output = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var pixmap = output.PeekPixels())
        {
            var span = pixmap.GetPixelSpan<byte>();
            for (var i = 0; i < p.Length; i++)
            {
                var value = (byte)(p[i].Luma * p[i].A);
                span[i * 4] = span[i * 4 + 1] = span[i * 4 + 2] = value;
                span[i * 4 + 3] = p[i].A;
            }
        }
        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
