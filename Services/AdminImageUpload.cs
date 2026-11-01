using Microsoft.AspNetCore.Hosting;

namespace KarakusAluminyum.Web.Services;

public static class AdminImageUpload
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public static async Task<string?> SaveAsync(IFormFile? file, IWebHostEnvironment environment, string folder = "uploads/images", int maxEdge = ImageOptimizer.MaxEdge, Func<byte[], byte[]>? transform = null, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Görsel en fazla 8 MB olabilir.");
        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (!Extensions.Contains(extension)) throw new InvalidOperationException("Yalnızca JPG, PNG, WEBP veya GIF görselleri yükleyebilirsiniz.");
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        return await SaveOptimizedAsync(file, webRoot, folder, maxEdge, transform, cancellationToken);
    }

    // Dosyanın içeriği çözülemezse uzantısı ne olursa olsun reddedilir; kaydedilen dosya sunucuda yeniden üretilir.
    public static async Task<string> SaveOptimizedAsync(IFormFile file, string webRoot, string folder, int maxEdge = ImageOptimizer.MaxEdge, Func<byte[], byte[]>? transform = null, CancellationToken cancellationToken = default)
    {
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        var input = memory.ToArray();
        if (transform is not null) input = transform(input);
        var image = ImageOptimizer.Process(input, maxEdge);
        var name = Guid.NewGuid().ToString("N");
        var physicalFolder = Path.Combine(webRoot, folder.Replace('/', Path.DirectorySeparatorChar));

        // Hareketli GIF tek kareye düşmesin diye olduğu gibi saklanır.
        if (ImageOptimizer.IsAnimated(input))
        {
            Directory.CreateDirectory(physicalFolder);
            await File.WriteAllBytesAsync(Path.Combine(physicalFolder, name + ".gif"), input, cancellationToken);
            return $"/{folder}/{name}.gif";
        }
        await ImageOptimizer.WriteAsync(image, Path.Combine(physicalFolder, name + ".webp"), cancellationToken);
        return $"/{folder}/{name}.webp";
    }
}
