using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record SocialMediaPost(string File, string Title, string Audience, string Caption);
public sealed record SocialMediaHighlight(string Name, string Purpose, string AddLater, string Cover, string Story);

// Dört bölüm: öne çıkanlar ve bilgi içerikleri "social media" klasöründen okunur (wwwroot dışında, yalnızca panelden görülür);
// referans ve devam eden projeler panelden yüklenir, görselleri kalıcı uploads klasöründe durur.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/sosyal-medya")]
public sealed class SocialMediaController : Controller
{
    public const int Width = 1080, Height = 1350;
    private const string FolderName = "social media";
    private const string HighlightFolder = "one-cikanlar";
    private const string InfoFolder = "bilgi-icerikleri";
    private const string UploadFolder = "uploads/sosyal-medya";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public SocialMediaController(IWebHostEnvironment env, AppDbContext db, IAuditService audit) { _env = env; _db = db; _audit = audit; }

    private string Folder => Path.Combine(_env.ContentRootPath, FolderName);
    private string WebRoot => _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
    private string Physical(string webPath) => Path.Combine(WebRoot, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private async Task<List<T>> ReadAsync<T>(string folder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Folder, folder, "icerik.json");
        if (!System.IO.File.Exists(path)) return new();
        await using var stream = System.IO.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, cancellationToken) ?? new();
    }

    private Task<List<SocialMediaPost>> PostsAsync(CancellationToken cancellationToken) => ReadAsync<SocialMediaPost>(InfoFolder, cancellationToken);
    private Task<List<SocialMediaHighlight>> HighlightsAsync(CancellationToken cancellationToken) => ReadAsync<SocialMediaHighlight>(HighlightFolder, cancellationToken);

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Projects), new { segment = SocialProjectKind.Reference.Segment });

    [HttpGet("one-cikanlar")]
    public async Task<IActionResult> Highlights(CancellationToken cancellationToken) => View(await HighlightsAsync(cancellationToken));

    [HttpGet("bilgi-icerikleri")]
    public async Task<IActionResult> Posts(CancellationToken cancellationToken) => View(await PostsAsync(cancellationToken));

    [HttpGet("one-cikan/{file}")]
    public async Task<IActionResult> HighlightImage(string file, bool indir = false, CancellationToken cancellationToken = default)
    {
        var known = (await HighlightsAsync(cancellationToken)).Any(x => x.Cover == file || x.Story == file);
        var path = Path.Combine(Folder, HighlightFolder, Path.GetFileName(file));
        if (!known || !System.IO.File.Exists(path)) return NotFound();
        return indir ? PhysicalFile(path, "image/jpeg", file) : PhysicalFile(path, "image/jpeg");
    }

    // Dosya adı listeyle karşılaştırılır; adres üzerinden klasör dışına çıkılamaz.
    [HttpGet("gorsel/{file}")]
    public async Task<IActionResult> Image(string file, bool indir = false, CancellationToken cancellationToken = default)
    {
        var post = (await PostsAsync(cancellationToken)).FirstOrDefault(x => x.File == file);
        var path = post is null ? null : Path.Combine(Folder, InfoFolder, post.File);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        return indir ? PhysicalFile(path, "image/jpeg", post!.File) : PhysicalFile(path, "image/jpeg");
    }

    // Referans ve devam eden projeler aynı işlemleri kullanır; tür adresteki bölümden gelir.
    private const string KindRoute = "{segment:regex(^(referans-projeler|devam-eden-projeler)$)}";

    private SocialProjectKind Kind(string segment)
    {
        var kind = SocialProjectKind.FromSegment(segment) ?? SocialProjectKind.Reference;
        ViewBag.Kind = kind;
        return kind;
    }

    [HttpGet(KindRoute)]
    public async Task<IActionResult> Projects(string segment)
    {
        var kind = Kind(segment);
        return View(await _db.SocialProjectPosts.AsNoTracking().Where(x => x.Kind == kind.Key).OrderByDescending(x => x.CreatedUtc).ToListAsync());
    }

    [HttpGet(KindRoute + "/ekle")]
    public IActionResult CreateProject(string segment) => View("ProjectEdit", new SocialProjectPost { Kind = Kind(segment).Key });

    [HttpPost(KindRoute + "/ekle"), ValidateAntiForgeryToken, RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> CreateProject(string segment, SocialProjectPost input, IFormFile? photo, IFormFile? image)
    {
        var kind = Kind(segment);
        input.Id = 0;
        input.Kind = kind.Key;
        input.PhotoPath = input.ImagePath = "";
        ReadLayout(input);
        if (photo is null || photo.Length == 0) ModelState.AddModelError("photo", "Proje fotoğrafını seçin.");
        if (!ModelState.IsValid) return View("ProjectEdit", input);
        var saved = await SaveImagesAsync(input, photo, image);
        if (!saved) return View("ProjectEdit", input);

        var row = new SocialProjectPost { Kind = kind.Key, PhotoPath = input.PhotoPath, ImagePath = input.ImagePath };
        Apply(row, input);
        _db.SocialProjectPosts.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "SocialProjectPost", row.Id.ToString(), $"{kind.Key}: {row.Name}");
        TempData["Notice"] = $"{Upper(kind.Singular)} gönderisi kaydedildi.";
        return RedirectToAction(nameof(Projects), new { segment = kind.Segment });
    }

    // Kayıt başka türün adresinden açılamaz: devam eden proje, referans sekmesinden düzenlenmez.
    private Task<SocialProjectPost?> FindAsync(int id, SocialProjectKind kind, bool tracking = false)
    {
        var query = tracking ? _db.SocialProjectPosts : _db.SocialProjectPosts.AsNoTracking();
        return query.FirstOrDefaultAsync(x => x.Id == id && x.Kind == kind.Key);
    }

    [HttpGet(KindRoute + "/duzenle/{id:int}")]
    public async Task<IActionResult> EditProject(string segment, int id)
    {
        var row = await FindAsync(id, Kind(segment));
        return row is null ? NotFound() : View("ProjectEdit", row);
    }

    [HttpPost(KindRoute + "/duzenle/{id:int}"), ValidateAntiForgeryToken, RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> EditProject(string segment, int id, SocialProjectPost input, IFormFile? photo, IFormFile? image)
    {
        var kind = Kind(segment);
        var row = await FindAsync(id, kind, tracking: true);
        if (row is null) return NotFound();
        input.Id = id;
        input.Kind = kind.Key;
        input.PhotoPath = row.PhotoPath;
        input.ImagePath = row.ImagePath;
        ReadLayout(input);
        if (!ModelState.IsValid) return View("ProjectEdit", input);
        var (oldPhoto, oldImage) = (row.PhotoPath, row.ImagePath);
        if (!await SaveImagesAsync(input, photo, image)) return View("ProjectEdit", input);

        Apply(row, input);
        row.PhotoPath = input.PhotoPath;
        row.ImagePath = input.ImagePath;
        row.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        if (oldPhoto != row.PhotoPath) DeleteFile(oldPhoto);
        if (oldImage != row.ImagePath) DeleteFile(oldImage);
        await _audit.RecordAsync("update", "SocialProjectPost", id.ToString(), $"{kind.Key}: {row.Name}");
        TempData["Notice"] = $"{Upper(kind.Singular)} gönderisi kaydedildi.";
        return RedirectToAction(nameof(Projects), new { segment = kind.Segment });
    }

    [HttpPost(KindRoute + "/sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProject(string segment, int id)
    {
        var kind = Kind(segment);
        var row = await FindAsync(id, kind, tracking: true);
        if (row is null) return NotFound();
        _db.SocialProjectPosts.Remove(row);
        await _db.SaveChangesAsync();
        DeleteFile(row.PhotoPath);
        DeleteFile(row.ImagePath);
        await _audit.RecordAsync("delete", "SocialProjectPost", id.ToString(), $"{kind.Key}: {row.Name}");
        TempData["Notice"] = $"{Upper(kind.Singular)} gönderisi silindi.";
        return RedirectToAction(nameof(Projects), new { segment = kind.Segment });
    }

    [HttpGet(KindRoute + "/gorsel/{id:int}")]
    public async Task<IActionResult> ProjectImage(string segment, int id, bool indir = false)
    {
        var row = await FindAsync(id, Kind(segment));
        if (row is null || string.IsNullOrEmpty(row.ImagePath) || !System.IO.File.Exists(Physical(row.ImagePath))) return NotFound();
        return indir ? PhysicalFile(Physical(row.ImagePath), "image/jpeg", ProjectFileName(row)) : PhysicalFile(Physical(row.ImagePath), "image/jpeg");
    }

    private static string Upper(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + text[1..];

    [HttpGet("tumunu-indir")]
    public async Task<IActionResult> DownloadAll(CancellationToken cancellationToken)
    {
        var posts = await PostsAsync(cancellationToken);
        var highlights = await HighlightsAsync(cancellationToken);
        var projects = await _db.SocialProjectPosts.AsNoTracking().OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string name) { if (System.IO.File.Exists(path)) archive.CreateEntryFromFile(path, name, CompressionLevel.NoCompression); } // JPG zaten sıkıştırılmış
            async Task TextAsync(string name, string text)
            {
                await using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                await writer.WriteAsync(text);
            }

            foreach (var file in highlights.SelectMany(x => new[] { x.Cover, x.Story })) Add(Path.Combine(Folder, HighlightFolder, file), $"{HighlightFolder}/{file}");
            foreach (var post in posts) Add(Path.Combine(Folder, InfoFolder, post.File), $"{InfoFolder}/{post.File}");
            await TextAsync($"{InfoFolder}/aciklamalar.txt", string.Concat(posts.Select(p => $"{p.File}\nHedef kitle: {p.Audience}\n\n{p.Caption}\n\n----------\n\n")));
            foreach (var group in projects.GroupBy(p => SocialProjectKind.FromKey(p.Kind)))
            {
                foreach (var project in group) Add(Physical(project.ImagePath), $"{group.Key.Segment}/{ProjectFileName(project)}");
                await TextAsync($"{group.Key.Segment}/aciklamalar.txt", string.Concat(group.Select(p => $"{ProjectFileName(p)}\n\n{p.Caption}\n\n----------\n\n")));
            }
        }
        output.Position = 0;
        return File(output, "application/zip", "karakus-sosyal-medya.zip");
    }

    private static string ProjectFileName(SocialProjectPost row) => $"{SocialProjectKind.FromKey(row.Kind).FilePrefix}-{row.Id:00}-{Slug(row.Name)}.jpg";

    private static string Slug(string text)
    {
        var map = new Dictionary<char, string> { ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u", ['â'] = "a", ['î'] = "i", ['û'] = "u" };
        var builder = new StringBuilder();
        foreach (var c in text.Trim().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant())
            builder.Append(map.TryGetValue(c, out var r) ? r : char.IsAsciiLetterOrDigit(c) ? c.ToString() : "-");
        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "proje" : slug[..Math.Min(slug.Length, 60)];
    }

    // Konum değerleri tarayıcıdan noktalı ondalıkla gelir; sunucunun kültüründen bağımsız okunur ve sınırlanır.
    private void ReadLayout(SocialProjectPost input)
    {
        double Read(string key, double fallback, double min, double max)
        {
            ModelState.Remove(key);
            return double.TryParse(Request.Form[key], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? Math.Clamp(value, min, max) : fallback;
        }
        input.TextX = Read(nameof(input.TextX), 0, 0, 1);
        input.TextY = Read(nameof(input.TextY), 0, 0, 1);
        input.FocusX = Read(nameof(input.FocusX), 0.5, 0, 1);
        input.FocusY = Read(nameof(input.FocusY), 0.5, 0, 1);
        input.TextScale = Read(nameof(input.TextScale), 1, 0.6, 1.4);
    }

    private static void Apply(SocialProjectPost row, SocialProjectPost input)
    {
        row.Name = input.Name.Trim();
        row.Caption = string.IsNullOrWhiteSpace(input.Caption) ? SocialProjectPost.DefaultCaption(row.Name, row.Kind) : input.Caption.Trim();
        row.TextX = input.TextX;
        row.TextY = input.TextY;
        row.FocusX = input.FocusX;
        row.FocusY = input.FocusY;
        row.TextScale = input.TextScale;
    }

    // Fotoğraf her görsel gibi optimize edilir. Hazır görsel tarayıcıda çizilir; sunucu boyutunu doğrular ve yeniden kodlar.
    private async Task<bool> SaveImagesAsync(SocialProjectPost input, IFormFile? photo, IFormFile? image)
    {
        byte[]? rendered = null;
        if (image is null || image.Length == 0) ModelState.AddModelError("image", "Gönderi görseli üretilemedi. Sayfayı yenileyip tekrar deneyin.");
        else if (image.Length > 6_000_000) ModelState.AddModelError("image", "Gönderi görseli çok büyük.");
        else
        {
            await using var stream = image.OpenReadStream();
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null || bitmap.Width != Width || bitmap.Height != Height) ModelState.AddModelError("image", $"Gönderi görseli {Width}×{Height} olmalıdır.");
            else
            {
                using var skImage = SKImage.FromBitmap(bitmap);
                using var encoded = skImage.Encode(SKEncodedImageFormat.Jpeg, 90);
                rendered = encoded.ToArray();
            }
        }
        if (rendered is null) return false;

        string? newPhoto = null;
        if (photo is { Length: > 0 })
        {
            try { newPhoto = await AdminImageUpload.SaveAsync(photo, _env, $"{UploadFolder}/fotograf"); }
            catch (InvalidOperationException error) { ModelState.AddModelError("photo", error.Message); return false; }
        }

        var imagePath = $"/{UploadFolder}/{Guid.NewGuid():N}.jpg";
        Directory.CreateDirectory(Path.GetDirectoryName(Physical(imagePath))!);
        await System.IO.File.WriteAllBytesAsync(Physical(imagePath), rendered);
        if (newPhoto is not null) input.PhotoPath = newPhoto;
        input.ImagePath = imagePath;
        return true;
    }

    private void DeleteFile(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath) || !webPath.StartsWith($"/{UploadFolder}/", StringComparison.Ordinal)) return;
        var path = Physical(webPath);
        if (webPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ImageOptimizer.DeleteWithVariants(path);
        else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }
}
