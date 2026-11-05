using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Seo;

public enum PageKind { Core, Service, Project, Article }

public sealed record PublicPage(string Path, string Title, string Description, PageKind Kind, DateTime? ModifiedUtc, IReadOnlyList<(string Path, string Title)> Images, string Body)
{
    public string MarkdownPath => Path == "/" ? "/index.md" : Path + ".md";
}

// Aranabilir sayfaların tek listesi. Site haritası, llms dosyaları, Markdown sayfalar ve IndexNow buradan beslenir;
// panelden yayımlanan içerik başka bir işlem gerekmeden hepsinde görünür.
public sealed class SiteContentIndex
{
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public SiteContentIndex(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    private static DateTime? Latest(IEnumerable<DateTime?> dates) => dates.Where(x => x is not null).DefaultIfEmpty(null).Max();
    private static List<string> Parse(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public async Task<List<PublicPage>> PagesAsync()
    {
        var site = await _settings.GetAsync();
        var services = await _db.Services.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ToListAsync();
        var projects = await _db.Projects.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.CreatedUtc).ToListAsync();
        var posts = await _db.BlogPosts.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.PublishedUtc).ToListAsync();
        var faqs = await _db.FaqItems.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        // Kurumsal ve İletişim sayfaları panel ayarlarından beslenir; tarihleri ilgili ayarların son değişikliğidir.
        var settingDates = await _db.SiteSettingRecords.AsNoTracking().Where(x => x.UpdatedUtc != null).Select(x => new { x.Key, x.UpdatedUtc }).ToListAsync();
        DateTime? SettingsModified(string group) => Latest(settingDates.Where(x => SiteSettingCatalog.Find(x.Key)?.Group == group).Select(x => x.UpdatedUtc));
        var images = await _db.GalleryImages.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();

        string Links(IEnumerable<(string Path, string Title, string Text)> rows) => string.Join('\n', rows.Select(x => $"- [{x.Title}]({x.Path}): {x.Text}"));
        string Faq(IEnumerable<FaqItem> rows) => string.Join("\n\n", rows.Select(x => $"### {x.Question}\n\n{x.Answer}"));
        List<(string, string)> Gallery(IEnumerable<GalleryImage> rows, string fallback) => rows.Select(x => (x.ImagePath, string.IsNullOrWhiteSpace(x.Title) ? fallback : x.Title!)).DistinctBy(x => x.ImagePath).ToList();

        var serviceLinks = Links(services.Select(x => ("/urunler/" + x.Slug, x.Name, x.ShortDescription)));
        var projectLinks = Links(projects.Select(x => ("/projeler/" + x.Slug, x.Title, x.Summary)));
        var postLinks = Links(posts.Select(x => ("/blog/" + x.Slug, x.Title, x.Summary)));
        var homeFaqs = faqs.Where(x => x.ServiceId == null).ToList();
        var servicesModified = Latest(services.Select(x => (DateTime?)x.UpdatedUtc));
        var projectsModified = Latest(projects.Select(x => (DateTime?)x.UpdatedUtc));
        var postsModified = Latest(posts.Select(x => (DateTime?)x.UpdatedUtc));

        var pages = new List<PublicPage>
        {
            new("/", site.SiteName, site.SiteDescription, PageKind.Core, Latest(new[] { servicesModified, projectsModified, postsModified, Latest(homeFaqs.Select(x => (DateTime?)x.UpdatedUtc)) }), Array.Empty<(string, string)>(),
                $"{site.HeroTitle} {site.HeroTitleEmphasis.TrimEnd('.')}.\n\n{site.HeroText}\n\n## Ürün ve hizmetler\n\n{serviceLinks}" + (homeFaqs.Count > 0 ? $"\n\n## Sık sorulan sorular\n\n{Faq(homeFaqs)}" : "")),
            new("/kurumsal", "Hakkımızda", $"{SiteSettings.FoundedYear}'den beri {site.PrimaryServiceArea}/{site.City}'de alüminyum, cam ve kapı sistemleri uygulayan firmamızın çalışma yaklaşımı.", PageKind.Core, SettingsModified(SiteSettingCatalog.AboutGroup), Array.Empty<(string, string)>(), $"{site.AboutIntro}\n\n## {site.AboutSectionTitle}\n\n{site.AboutText}"),
            new("/urunler", "Ürün ve Hizmetler", "Sunduğumuz alüminyum, cam ve kapı sistemleri.", PageKind.Core, servicesModified, Array.Empty<(string, string)>(), serviceLinks),
            new("/projeler", "Projelerimiz", "Uyguladığımız projelerden örnekler.", PageKind.Core, projectsModified, Array.Empty<(string, string)>(), projectLinks),
            new("/galeri", "Galeri", "Uygulamalarımızdan görüntüler.", PageKind.Core, Latest(images.Select(x => (DateTime?)x.CreatedUtc)), Gallery(images, "Karakuş Alüminyum uygulaması"), $"{images.Count} görsel yayında."),
            new("/blog", "Bilgi Merkezi", "Alüminyum sistemler ve ürün seçimi hakkında yazılar.", PageKind.Core, postsModified, Array.Empty<(string, string)>(), postLinks),
            new("/iletisim", "İletişim ve Teklif", "Projeniz için ölçü ve teklif almak üzere bize ulaşın.", PageKind.Core, SettingsModified(SiteSettingCatalog.ContactGroup), Array.Empty<(string, string)>(), $"- Telefon: {site.PhoneText}\n- E-posta: {site.Email}\n- Adres: {site.Address}\n- Hizmet bölgesi: {site.City}, {site.PrimaryServiceArea}\n\nTeklif formu: /iletisim")
        };

        foreach (var x in services)
        {
            var body = new StringBuilder(x.Intro);
            var benefits = Parse(x.BenefitsJson);
            if (benefits.Count > 0) body.Append("\n\n## Öne çıkanlar\n\n").AppendJoin('\n', benefits.Select(b => "- " + b));
            var own = faqs.Where(f => f.ServiceId == x.Id).ToList();
            if (own.Count > 0) body.Append("\n\n## Sık sorulan sorular\n\n").Append(Faq(own));
            var photos = new[] { (x.Image ?? "", x.Name) }.Where(p => p.Item1.Length > 0).Concat(Gallery(images.Where(i => i.ServiceId == x.Id), x.Name)).DistinctBy(p => p.Item1).ToList();
            pages.Add(new("/urunler/" + x.Slug, x.Name, string.IsNullOrWhiteSpace(x.SeoDescription) ? x.ShortDescription : x.SeoDescription, PageKind.Service, x.UpdatedUtc, photos, body.ToString()));
        }
        foreach (var x in projects)
        {
            var photos = new[] { (x.Image ?? "", x.Title) }.Where(p => p.Item1.Length > 0 && p.Item1 != ProjectQueries.PlaceholderImage).Concat(Gallery(images.Where(i => i.ProjectId == x.Id), x.Title)).DistinctBy(p => p.Item1).ToList();
            pages.Add(new("/projeler/" + x.Slug, x.Title, x.Summary, PageKind.Project, x.UpdatedUtc, photos, $"- Tür: {x.Type}\n- Konum: {x.Location}\n\n{x.Summary}" + (string.IsNullOrWhiteSpace(x.Content) ? "" : $"\n\n{x.Content}")));
        }
        foreach (var x in posts)
            pages.Add(new("/blog/" + x.Slug, x.Title, string.IsNullOrWhiteSpace(x.SeoDescription) ? x.Summary : x.SeoDescription, PageKind.Article, x.UpdatedUtc, new[] { (x.Image, x.Title) }, $"{x.Summary}\n\n{x.Content}"));
        return pages;
    }
}
