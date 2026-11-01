using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services;

// Mevcut kurulumdaki PNG/JPG kayıtlarını WebP'ye taşır. Her açılışta çalışır, yapılacak iş kalmadıysa dosyaya dokunmaz.
// Yüklenen dosyaların aslı silinmez: eski belge kopyaları ve metin içindeki bağlantılar çalışmaya devam eder.
public static class ImageMigration
{
    private const long ReoptimizeBytes = 300 * 1024;
    private static readonly string[] Legacy = { ".png", ".jpg", ".jpeg" };

    public static async Task RunAsync(AppDbContext db, string webRoot, ILogger logger, CancellationToken cancellationToken = default)
    {
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = 0;

        async Task<string> MapAsync(string path)
        {
            if (mapped.TryGetValue(path, out var known)) return known;
            var result = path;
            try { result = await ConvertAsync(path, webRoot, cancellationToken); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(error, "Görsel dönüştürülemedi, eski haliyle bırakıldı: {Path}", path);
            }
            return mapped[path] = result;
        }

        async Task UpdateAsync<T>(IQueryable<T> rows, Func<T, string?> read, Action<T, string> write)
        {
            foreach (var row in await rows.ToListAsync(cancellationToken))
            {
                var path = read(row);
                if (string.IsNullOrWhiteSpace(path)) continue;
                var next = await MapAsync(path);
                if (next == path) continue;
                write(row, next);
                changed++;
            }
        }

        await UpdateAsync(db.Services, x => x.Image, (x, v) => x.Image = v);
        await UpdateAsync(db.Projects, x => x.Image, (x, v) => x.Image = v);
        await UpdateAsync(db.BlogPosts, x => x.Image, (x, v) => x.Image = v);
        await UpdateAsync(db.GalleryImages, x => x.ImagePath, (x, v) => x.ImagePath = v);
        await UpdateAsync(db.Brands, x => x.LogoPath, (x, v) => x.LogoPath = v);
        await UpdateAsync(db.DocumentTemplates, x => x.LogoPath, (x, v) => x.LogoPath = v);
        await db.SaveChangesAsync(cancellationToken);
        if (changed > 0) logger.LogInformation("Görsel geçişi: {Count} kayıt WebP adresine taşındı.", changed);
    }

    private static async Task<string> ConvertAsync(string path, string webRoot, CancellationToken cancellationToken)
    {
        var isUpload = path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase);
        if (!isUpload && !path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)) return path;
        var physical = Path.GetFullPath(Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!physical.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return path;
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (Legacy.Contains(extension))
        {
            var target = Path.ChangeExtension(physical, ".webp");
            var targetUrl = Path.ChangeExtension(path, ".webp");
            if (File.Exists(target)) return targetUrl;
            if (!isUpload || !File.Exists(physical)) return path; // repodaki görseller önceden dönüştürülmüştür
            await ImageOptimizer.WriteAsync(ImageOptimizer.Process(await File.ReadAllBytesAsync(physical, cancellationToken)), target, cancellationToken);
            return targetUrl;
        }

        // Daha önce WebP olarak yüklenmiş ama iyileştirilmemiş büyük dosyalar.
        if (isUpload && extension == ".webp" && File.Exists(physical) && !File.Exists(ImageOptimizer.MediumPath(physical)) && new FileInfo(physical).Length > ReoptimizeBytes)
            await ImageOptimizer.WriteAsync(ImageOptimizer.Process(await File.ReadAllBytesAsync(physical, cancellationToken)), physical, cancellationToken);
        return path;
    }
}
