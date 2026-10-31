using System.Text;
using System.Xml;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Seo;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Controllers;

// Arama motorları ve yapay zekâ okuyucuları için keşif dosyaları. İçerik her istekte SiteContentIndex'ten üretilir.
public sealed class DiscoveryController : Controller
{
    private const int CacheSeconds = 600;
    private const string PrivatePaths = "Disallow: /yonetim\nDisallow: /yonetim-giris\nDisallow: /erisim-engellendi\nDisallow: /hata/\nDisallow: /t/\nDisallow: /iletisim/tesekkurler\n";
    // Yapay zekâ arama, cevap ve eğitim tarayıcıları. Ayarlardan birlikte kapatılabilir.
    private static readonly string[] AiCrawlers =
    {
        "GPTBot", "OAI-SearchBot", "ChatGPT-User", "ClaudeBot", "Claude-User", "Claude-SearchBot", "anthropic-ai", "PerplexityBot", "Perplexity-User", "Google-Extended", "Applebot-Extended", "CCBot", "Amazonbot", "DuckAssistBot", "MistralAI-User", "meta-externalagent"
    };

    private readonly SiteContentIndex _index;
    private readonly SiteSettingsProvider _settings;
    private readonly IndexNowPublisher _indexNow;
    public DiscoveryController(SiteContentIndex index, SiteSettingsProvider settings, IndexNowPublisher indexNow) { _index = index; _settings = settings; _indexNow = indexNow; }

    private static string Base(SiteSettings site) => site.BaseUrl.TrimEnd('/');
    private static string Absolute(SiteSettings site, string path) => path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : Base(site) + (path.StartsWith('/') ? path : "/" + path);

    [HttpGet("robots.txt"), ResponseCache(Duration = CacheSeconds)]
    public async Task<ContentResult> Robots()
    {
        var site = await _settings.GetAsync();
        var text = new StringBuilder("User-agent: *\nAllow: /\n").Append(PrivatePaths).Append('\n');
        text.AppendJoin('\n', AiCrawlers.Select(x => "User-agent: " + x)).Append('\n');
        // Tek tek belirtilen tarayıcı genel kuralı görmez; panel yolları burada da kapatılır.
        if (site.AiCrawlersAllowed == "true") text.Append("Allow: /\n").Append(PrivatePaths);
        else text.Append("Disallow: /\n");
        text.Append($"\nSitemap: {Absolute(site, "/sitemap.xml")}\n");
        return Content(text.ToString(), "text/plain; charset=utf-8");
    }

    [HttpGet("sitemap.xml"), ResponseCache(Duration = CacheSeconds)]
    public async Task<IActionResult> Sitemap()
    {
        var site = await _settings.GetAsync();
        const string main = "http://www.sitemaps.org/schemas/sitemap/0.9", image = "http://www.google.com/schemas/sitemap-image/1.1";
        var output = new MemoryStream();
        await using (var xml = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Async = true, Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", main);
            xml.WriteAttributeString("xmlns", "image", null, image);
            foreach (var page in await _index.PagesAsync())
            {
                xml.WriteStartElement("url", main);
                xml.WriteElementString("loc", main, Absolute(site, page.Path));
                if (page.ModifiedUtc is { } modified) xml.WriteElementString("lastmod", main, DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"));
                foreach (var photo in page.Images.Take(1000))
                {
                    xml.WriteStartElement("image", "image", image);
                    xml.WriteElementString("image", "loc", image, Absolute(site, photo.Path));
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return File(output.ToArray(), "application/xml; charset=utf-8");
    }

    // llmstxt.org biçimi: sitenin yapay zekâ okuyucuları için özeti ve Markdown sayfa bağlantıları.
    [HttpGet("llms.txt"), ResponseCache(Duration = CacheSeconds)]
    public async Task<ContentResult> Llms()
    {
        var site = await _settings.GetAsync();
        var pages = await _index.PagesAsync();
        var text = new StringBuilder($"# {site.SiteName}\n\n> {site.SiteDescription}\n\n");
        text.Append($"- Konum: {site.Address}\n- Telefon: {site.PhoneDisplay}\n- E-posta: {site.Email}\n- Web: {Base(site)}\n- Tüm içerik tek dosyada: {Absolute(site, "/llms-full.txt")}\n");
        void Section(string title, PageKind kind)
        {
            var rows = pages.Where(x => x.Kind == kind).ToList();
            if (rows.Count == 0) return;
            text.Append($"\n## {title}\n\n").AppendJoin('\n', rows.Select(x => $"- [{x.Title}]({Absolute(site, x.MarkdownPath)}): {x.Description}")).Append('\n');
        }
        Section("Ana sayfalar", PageKind.Core);
        Section("Ürün ve hizmetler", PageKind.Service);
        Section("Projeler", PageKind.Project);
        Section("Yazılar", PageKind.Article);
        return Content(text.ToString(), "text/plain; charset=utf-8");
    }

    [HttpGet("llms-full.txt"), ResponseCache(Duration = CacheSeconds)]
    public async Task<ContentResult> LlmsFull()
    {
        var site = await _settings.GetAsync();
        var text = new StringBuilder($"# {site.SiteName}\n\n> {site.SiteDescription}\n");
        foreach (var page in await _index.PagesAsync()) text.Append("\n---\n\n").Append(Render(site, page));
        return Content(text.ToString(), "text/plain; charset=utf-8");
    }

    [HttpGet("index.md"), ResponseCache(Duration = CacheSeconds)]
    public Task<IActionResult> HomeMarkdown() => MarkdownAsync("/");

    [HttpGet("{section}.md"), ResponseCache(Duration = CacheSeconds)]
    public Task<IActionResult> SectionMarkdown(string section) => MarkdownAsync("/" + section);

    [HttpGet("urunler/{slug}.md"), ResponseCache(Duration = CacheSeconds)]
    public Task<IActionResult> ServiceMarkdown(string slug) => MarkdownAsync("/urunler/" + slug);

    [HttpGet("projeler/{slug}.md"), ResponseCache(Duration = CacheSeconds)]
    public Task<IActionResult> ProjectMarkdown(string slug) => MarkdownAsync("/projeler/" + slug);

    [HttpGet("blog/{slug}.md"), ResponseCache(Duration = CacheSeconds)]
    public Task<IActionResult> PostMarkdown(string slug) => MarkdownAsync("/blog/" + slug);

    private async Task<IActionResult> MarkdownAsync(string path)
    {
        var page = (await _index.PagesAsync()).FirstOrDefault(x => x.Path == path);
        if (page is null) return NotFound();
        var site = await _settings.GetAsync();
        // Arama sonucunda HTML sayfa öne çıksın diye Markdown kopya kaynağı işaret eder.
        Response.Headers.Append("Link", $"<{Absolute(site, page.Path)}>; rel=\"canonical\"");
        return Content(Render(site, page), "text/markdown; charset=utf-8");
    }

    private static string Render(SiteSettings site, PublicPage page) =>
        $"# {page.Title}\n\nKaynak: {Absolute(site, page.Path)}\n{(page.ModifiedUtc is { } modified ? $"Güncelleme: {modified:yyyy-MM-dd}\n" : "")}\n{page.Description}\n\n{AbsoluteLinks(site, page.Body)}\n";

    private static string AbsoluteLinks(SiteSettings site, string body) => body.Replace("](/", $"]({Base(site)}/");

    [HttpGet("indexnow-{key}.txt")]
    public async Task<IActionResult> IndexNowKey(string key)
    {
        var current = await _indexNow.KeyAsync();
        return key == current ? Content(current, "text/plain") : NotFound();
    }
}
