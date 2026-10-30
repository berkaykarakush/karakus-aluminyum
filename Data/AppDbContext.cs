using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Analytics;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Data;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<ServiceContent> Services => Set<ServiceContent>();
    public DbSet<PortfolioProject> Projects => Set<PortfolioProject>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<SiteSettingRecord> SiteSettingRecords => Set<SiteSettingRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<ProfileSystem> ProfileSystems => Set<ProfileSystem>();
    public DbSet<GlassType> GlassTypes => Set<GlassType>();
    public DbSet<WindowDesign> WindowDesigns => Set<WindowDesign>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<GlassList> GlassLists => Set<GlassList>();
    public DbSet<GlassListItem> GlassListItems => Set<GlassListItem>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<TemplateClause> TemplateClauses => Set<TemplateClause>();
    public DbSet<Contract> Contracts => Set<Contract>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ServiceContent>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<PortfolioProject>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<BlogPost>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<SiteSettingRecord>().HasIndex(x => x.Key).IsUnique();
        builder.Entity<ServiceContent>().Property(x => x.Name).HasMaxLength(160);
        builder.Entity<ServiceContent>().Property(x => x.Slug).HasMaxLength(180);
        builder.Entity<PortfolioProject>().Property(x => x.Title).HasMaxLength(180);
        builder.Entity<PortfolioProject>().Property(x => x.Slug).HasMaxLength(180);
        builder.Entity<BlogPost>().Property(x => x.Title).HasMaxLength(180);
        builder.Entity<BlogPost>().Property(x => x.Slug).HasMaxLength(180);

        builder.Entity<Customer>().HasIndex(x => x.Name);
        builder.Entity<Customer>().HasIndex(x => x.Phone);
        builder.Entity<Customer>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Entity<Quote>().HasIndex(x => x.QuoteNumber).IsUnique();
        builder.Entity<Quote>().HasOne(x => x.Customer).WithMany(x => x.Quotes)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AuditLog>().HasIndex(x => x.CreatedUtc);
        builder.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
        builder.Entity<QuoteItem>().HasOne(x => x.Quote).WithMany(x => x.Items)
            .HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GalleryImage>().Property(x => x.ImagePath).HasMaxLength(500);
            builder.Entity<GalleryImage>().Property(x => x.Title).HasMaxLength(160);
            builder.Entity<GalleryImage>().Property(x => x.Caption).HasMaxLength(500);
            builder.Entity<GalleryImage>().HasIndex(x => new { x.ServiceId, x.SortOrder });
            builder.Entity<GalleryImage>().HasIndex(x => new { x.ProjectId, x.SortOrder });
            builder.Entity<GalleryImage>().HasOne(x => x.Service).WithMany()
                .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<GalleryImage>().HasOne(x => x.Project).WithMany()
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<FaqItem>().HasIndex(x => new { x.ServiceId, x.SortOrder });
        builder.Entity<FaqItem>().HasOne(x => x.Service).WithMany()
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DocumentTemplate>().HasIndex(x => x.Kind).IsUnique();
        builder.Entity<Contract>().HasIndex(x => x.ContractNumber).IsUnique();
        builder.Entity<Contract>().HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Contract>().HasOne(x => x.Quote).WithMany()
            .HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<WindowDesign>().HasIndex(x => x.UpdatedUtc);
        builder.Entity<WindowDesign>().HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<WindowDesign>().HasOne(x => x.ProfileSystem).WithMany()
            .HasForeignKey(x => x.ProfileSystemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AnalyticsEvent>().HasIndex(x => x.OccurredUtc);
        builder.Entity<GlassList>().HasIndex(x => x.UpdatedUtc);
        builder.Entity<GlassList>().HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<GlassListItem>().HasOne(x => x.GlassList).WithMany(x => x.Items)
            .HasForeignKey(x => x.GlassListId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GlassListItem>().HasOne(x => x.GlassType).WithMany()
            .HasForeignKey(x => x.GlassTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
