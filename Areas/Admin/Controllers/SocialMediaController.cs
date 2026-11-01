using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record SocialMediaPost(string File, string Title, string Audience, string Caption);
public sealed record SocialMediaHighlight(string Name, string Purpose, string AddLater, string Cover, string Story);
public sealed record SocialMediaVm(IReadOnlyList<SocialMediaHighlight> Highlights, IReadOnlyList<SocialMediaPost> Posts);

// Görseller "social media" klasöründedir, wwwroot dışında durur: yalnızca panele giriş yapanlar görebilir.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/sosyal-medya")]
public sealed class SocialMediaController : Controller
{
    private const string FolderName = "social media";
    private const string HighlightFolder = "one-cikanlar";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWebHostEnvironment _env;
    public SocialMediaController(IWebHostEnvironment env) => _env = env;

    private string Folder => Path.Combine(_env.ContentRootPath, FolderName);

    private async Task<List<T>> ReadAsync<T>(string file, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Folder, file);
        if (!System.IO.File.Exists(path)) return new();
        await using var stream = System.IO.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, cancellationToken) ?? new();
    }

    private Task<List<SocialMediaPost>> PostsAsync(CancellationToken cancellationToken) => ReadAsync<SocialMediaPost>("icerik.json", cancellationToken);
    private Task<List<SocialMediaHighlight>> HighlightsAsync(CancellationToken cancellationToken) => ReadAsync<SocialMediaHighlight>("one-cikanlar.json", cancellationToken);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(new SocialMediaVm(await HighlightsAsync(cancellationToken), await PostsAsync(cancellationToken)));

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
        var path = post is null ? null : Path.Combine(Folder, post.File);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        return indir ? PhysicalFile(path, "image/jpeg", post!.File) : PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("tumunu-indir")]
    public async Task<IActionResult> DownloadAll(CancellationToken cancellationToken)
    {
        var posts = await PostsAsync(cancellationToken);
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var post in posts)
            {
                var path = Path.Combine(Folder, post.File);
                if (System.IO.File.Exists(path)) archive.CreateEntryFromFile(path, post.File, CompressionLevel.NoCompression); // JPG zaten sıkıştırılmış
            }
            var highlights = await HighlightsAsync(cancellationToken);
            foreach (var file in highlights.SelectMany(x => new[] { x.Cover, x.Story }))
            {
                var path = Path.Combine(Folder, HighlightFolder, file);
                if (System.IO.File.Exists(path)) archive.CreateEntryFromFile(path, $"{HighlightFolder}/{file}", CompressionLevel.NoCompression);
            }
            var captions = archive.CreateEntry("aciklamalar.txt");
            await using var writer = new StreamWriter(captions.Open());
            foreach (var post in posts) await writer.WriteAsync($"{post.File}\nHedef kitle: {post.Audience}\n\n{post.Caption}\n\n----------\n\n");
        }
        output.Position = 0;
        return File(output, "application/zip", "karakus-sosyal-medya.zip");
    }
}
