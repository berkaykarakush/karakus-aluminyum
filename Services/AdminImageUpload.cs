using Microsoft.AspNetCore.Hosting;

namespace KarakusAluminyum.Web.Services;

public static class AdminImageUpload
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public static async Task<string?> SaveAsync(IFormFile? file, IWebHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Görsel en fazla 8 MB olabilir.");
        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (!Extensions.Contains(extension)) throw new InvalidOperationException("Yalnızca JPG, PNG, WEBP veya GIF görselleri yükleyebilirsiniz.");
        var folder = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "images");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(folder, fileName);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(stream, cancellationToken);
        return $"/uploads/images/{fileName}";
    }
}
