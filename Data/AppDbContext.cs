using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Crm;
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
    }
}
