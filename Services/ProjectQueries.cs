using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services;

public static class ProjectQueries
{
    public const string PlaceholderImage = "/images/image-placeholder.svg";

    // Kendi görseli olmayan proje, galerisindeki ilk görselle gösterilir.
    public static Task<List<ProjectItem>> PublishedAsync(AppDbContext db, int take = int.MaxValue)
        => db.Projects.AsNoTracking().Where(x => x.IsPublished).OrderByDescending(x => x.CreatedUtc).Take(take)
            .Select(x => new ProjectItem(x.Slug, x.Title, x.Type, x.Summary,
                x.Image != null && x.Image != PlaceholderImage
                    ? x.Image
                    : db.GalleryImages.Where(g => g.ProjectId == x.Id && g.IsPublished).OrderBy(g => g.SortOrder).Select(g => g.ImagePath).FirstOrDefault() ?? PlaceholderImage,
                x.Location))
            .ToListAsync();
}
