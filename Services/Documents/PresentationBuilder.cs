using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Documents;

// Kapağa yazılan belge bilgileri. Teklifle basılırken müşteri ve teklif bilgileri dolar.
public sealed record PresentationCover(string? Customer, string? Project, string? Number, DateTime Date);
public sealed record PresentationProject(string Title, string Type, string Location, string? Image);
public sealed record PresentationBrandGroup(string Label, IReadOnlyList<Brand> Brands);
public sealed record PresentationStat(string Value, string Label);
public sealed record PresentationContentsEntry(string Title, int Page);

// Basılan tek sayfa. Uzun listeler (hizmet, referans, proje) birden çok sayfaya bölünür: Part/Parts.
public sealed record PresentationPage(PresentationSection Section, IReadOnlyList<PresentationItem> Items, int Number, int Part, int Parts)
{
    public IReadOnlyList<ServiceContent> Services { get; init; } = Array.Empty<ServiceContent>();
    public IReadOnlyList<ClientReference> References { get; init; } = Array.Empty<ClientReference>();
    public IReadOnlyList<PresentationProject> Projects { get; init; } = Array.Empty<PresentationProject>();
    public IReadOnlyList<PresentationBrandGroup> Brands { get; init; } = Array.Empty<PresentationBrandGroup>();
}

public sealed record PresentationVm(
    IReadOnlyList<PresentationPage> Pages,
    PresentationCover Cover,
    SiteSettings Site,
    IReadOnlyList<PresentationStat> Stats,
    IReadOnlyList<PresentationContentsEntry> Contents,
    IReadOnlyDictionary<string, string> Variables,
    bool Portrait = false)
{
    public const string LogoPath = "/images/logo-ust.webp";
    public int PageCount => Pages.Count;
}

public sealed class PresentationBuilder
{
    public const int ServicesPerPage = 6;
    public const int ReferencesPerPage = 15;
    public const int ProjectsPerPage = 3;

    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public PresentationBuilder(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    public async Task<PresentationVm> BuildAsync(PresentationCover cover, bool portrait = false, CancellationToken cancellationToken = default)
    {
        var sections = await _db.PresentationSections.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var site = await _settings.GetAsync(cancellationToken);
        var kinds = sections.Select(x => x.Kind).ToHashSet();

        // Yalnızca açık bölümlerin ihtiyaç duyduğu listeler okunur.
        var services = await _db.Services.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var references = await _db.ClientReferences.AsNoTracking().Where(x => x.IsPublished).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var projects = kinds.Contains(PresentationKinds.Projects) || kinds.Contains(PresentationKinds.About) ? await LoadProjectsAsync(cancellationToken) : new();
        var brands = kinds.Contains(PresentationKinds.Brands)
            ? (await _db.Brands.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken))
                .GroupBy(x => x.Category).OrderBy(g => Array.IndexOf(ProductCategories.All, g.Key))
                .Select(g => new PresentationBrandGroup(ProductCategories.Label(g.Key), g.ToList())).ToList()
            : new();

        var pages = new List<PresentationPage>();
        foreach (var section in sections)
        {
            var items = DocumentJson.ParseItems(section.ItemsJson).Where(i => !string.IsNullOrWhiteSpace(i.Title) || !string.IsNullOrWhiteSpace(i.Text)).ToList();
            // Listesi boş kalan otomatik bölüm basılmaz: "Referanslarımız" başlıklı boş sayfa gitmesin.
            switch (section.Kind)
            {
                case PresentationKinds.Services:
                    AddChunks(services, ServicesPerPage, (part, parts, chunk) => new PresentationPage(section, items, 0, part, parts) { Services = chunk });
                    break;
                case PresentationKinds.References:
                    AddChunks(references, ReferencesPerPage, (part, parts, chunk) => new PresentationPage(section, items, 0, part, parts) { References = chunk });
                    break;
                case PresentationKinds.Projects:
                    AddChunks(projects, ProjectsPerPage, (part, parts, chunk) => new PresentationPage(section, items, 0, part, parts) { Projects = chunk });
                    break;
                case PresentationKinds.Brands:
                    if (brands.Count > 0) pages.Add(new PresentationPage(section, items, 0, 1, 1) { Brands = brands });
                    break;
                default:
                    pages.Add(new PresentationPage(section, items, 0, 1, 1));
                    break;
            }
        }
        pages = pages.Select((p, i) => p with { Number = i + 1 }).ToList();

        var contents = pages.Where(p => p.Part == 1 && p.Section.Kind is not (PresentationKinds.Cover or PresentationKinds.Contents))
            .Select(p => new PresentationContentsEntry(ContentsTitle(p.Section), p.Number)).ToList();

        var foundedYear = SiteSettings.FoundedYear;
        var experience = DateTime.UtcNow.AddHours(3).Year - foundedYear;
        var stats = new List<PresentationStat> { new($"{experience}+", "Yıllık deneyim") };
        if (services.Count > 0) stats.Add(new(services.Count.ToString(), "Hizmet alanı"));
        if (references.Count > 0) stats.Add(new(references.Count.ToString(), "Referans firma"));
        if (projects.Count > 0) stats.Add(new(projects.Count.ToString(), "Referans proje"));

        var variables = new Dictionary<string, string>
        {
            ["kurulus_yili"] = foundedYear.ToString(),
            ["tecrube_yili"] = experience.ToString(),
            ["sehir"] = string.IsNullOrWhiteSpace(site.City) ? "İzmir" : site.City,
            ["firma"] = string.IsNullOrWhiteSpace(cover.Customer) ? "" : cover.Customer.Trim(),
            ["hizmet_sayisi"] = services.Count.ToString()
        };
        return new PresentationVm(pages, cover, site, stats, contents, variables, portrait);

        void AddChunks<T>(IReadOnlyList<T> list, int size, Func<int, int, IReadOnlyList<T>, PresentationPage> create)
        {
            var chunks = list.Chunk(size).ToList();
            for (var i = 0; i < chunks.Count; i++) pages.Add(create(i + 1, chunks.Count, chunks[i]));
        }
    }

    // Proje görseli yer tutucuysa projenin ilk galeri görseli kullanılır; o da yoksa görselsiz kart basılır.
    private async Task<List<PresentationProject>> LoadProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.Projects.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Title).ToListAsync(cancellationToken);
        var ids = projects.Select(x => x.Id).ToList();
        var gallery = (await _db.GalleryImages.AsNoTracking().Where(x => x.ProjectId != null && ids.Contains(x.ProjectId.Value) && x.IsPublished)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new { x.ProjectId, x.ImagePath }).ToListAsync(cancellationToken))
            .GroupBy(x => x.ProjectId!.Value).ToDictionary(g => g.Key, g => g.First().ImagePath);
        return projects.Select(p => new PresentationProject(p.Title, p.Type, p.Location,
            IsRealImage(p.Image) ? p.Image : gallery.GetValueOrDefault(p.Id) is { } g && IsRealImage(g) ? g : null)).ToList();
    }

    // İçindekilerde kısa bölüm adı (üst başlık) gösterilir; yoksa sayfa başlığı.
    public static string ContentsTitle(PresentationSection section) => string.IsNullOrWhiteSpace(section.Eyebrow) ? section.Title : section.Eyebrow.Trim();

    public static bool IsRealImage(string? path) => !string.IsNullOrWhiteSpace(path) && !path.Contains("image-placeholder", StringComparison.OrdinalIgnoreCase);
}
