using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Analytics;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
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
    public DbSet<NotificationJob> NotificationJobs => Set<NotificationJob>();
    public DbSet<SiteSettingRecord> SiteSettingRecords => Set<SiteSettingRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<ClientReference> ClientReferences => Set<ClientReference>();
    public DbSet<SocialProjectPost> SocialProjectPosts => Set<SocialProjectPost>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<ProfileSystem> ProfileSystems => Set<ProfileSystem>();
    public DbSet<GlassType> GlassTypes => Set<GlassType>();
    public DbSet<WindowDesign> WindowDesigns => Set<WindowDesign>();
    public DbSet<DrawingList> DrawingLists => Set<DrawingList>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<FinanceAccount> FinanceAccounts => Set<FinanceAccount>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<CariAdjustment> CariAdjustments => Set<CariAdjustment>();
    public DbSet<CostProject> CostProjects => Set<CostProject>();
    public DbSet<ProjectCostItem> ProjectCostItems => Set<ProjectCostItem>();
    public DbSet<AccountingSequence> AccountingSequences => Set<AccountingSequence>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<GlassList> GlassLists => Set<GlassList>();
    public DbSet<GlassListItem> GlassListItems => Set<GlassListItem>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionScan> ProductionScans => Set<ProductionScan>();
    public DbSet<MachineExportProfile> MachineExportProfiles => Set<MachineExportProfile>();
    public DbSet<LabelPrinter> LabelPrinters => Set<LabelPrinter>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<ServiceVisit> ServiceVisits => Set<ServiceVisit>();
    public DbSet<ServicePart> ServiceParts => Set<ServicePart>();
    public DbSet<PunchItem> PunchItems => Set<PunchItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Installer> Installers => Set<Installer>();
    public DbSet<DeliveryNote> DeliveryNotes => Set<DeliveryNote>();
    public DbSet<DeliveryNoteLine> DeliveryNoteLines => Set<DeliveryNoteLine>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<TemplateClause> TemplateClauses => Set<TemplateClause>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<PresentationSection> PresentationSections => Set<PresentationSection>();
    public DbSet<Prospect> Prospects => Set<Prospect>();
    public DbSet<ProspectSector> ProspectSectors => Set<ProspectSector>();
    public DbSet<ProspectDistrict> ProspectDistricts => Set<ProspectDistrict>();
    public DbSet<ProspectActivity> ProspectActivities => Set<ProspectActivity>();
    public DbSet<ProspectScan> ProspectScans => Set<ProspectScan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Üretim siparişleri: müşteri silinemez; teklif, sözleşme, çizim listesi, camcı ve montajcı silinirse bağ kopar.
        builder.Entity<ProductionOrder>().HasIndex(x => x.OrderNumber).IsUnique();
        builder.Entity<ProductionScan>().HasIndex(x => new { x.ProductionOrderId, x.WindowDesignId, x.Unit, x.Station }).IsUnique();
        builder.Entity<ProductionScan>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductionOrder>().HasIndex(x => x.Status);
        builder.Entity<ProductionOrder>().HasIndex(x => x.PlannedShipDate);
        builder.Entity<ProductionOrder>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductionOrder>().HasOne(x => x.GlassSupplier).WithMany().HasForeignKey(x => x.GlassSupplierId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductionOrder>().HasOne(x => x.Quote).WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductionOrder>().HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductionOrder>().HasOne(x => x.DrawingList).WithMany().HasForeignKey(x => x.DrawingListId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductionOrder>().HasOne(x => x.Installer).WithMany().HasForeignKey(x => x.InstallerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<OrderEvent>().HasOne(x => x.ProductionOrder).WithMany(x => x.Events).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DeliveryNote>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<DeliveryNote>().HasOne(x => x.ProductionOrder).WithMany(x => x.DeliveryNotes).HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DeliveryNoteLine>().HasOne(x => x.DeliveryNote).WithMany(x => x.Lines).HasForeignKey(x => x.DeliveryNoteId).OnDelete(DeleteBehavior.Cascade);
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
        builder.Entity<Quote>().HasIndex(x => x.PublicToken).IsUnique();
        builder.Entity<Quote>().HasOne(x => x.Customer).WithMany(x => x.Quotes)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Quote>().HasOne(x => x.DrawingList).WithMany().HasForeignKey(x => x.DrawingListId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Quote>().HasOne(x => x.GlassType).WithMany().HasForeignKey(x => x.GlassTypeId).OnDelete(DeleteBehavior.SetNull);
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
        builder.Entity<Contract>().HasIndex(x => x.PublicToken).IsUnique();
        builder.Entity<PresentationSection>().HasIndex(x => x.SortOrder);
        builder.Entity<Prospect>().HasIndex(x => x.GooglePlaceId).IsUnique().HasFilter("\"GooglePlaceId\" IS NOT NULL");
        builder.Entity<Prospect>().HasIndex(x => new { x.Stage, x.NextFollowUp });
        builder.Entity<Prospect>().HasIndex(x => x.SectorId);
        builder.Entity<Prospect>().HasOne(x => x.Sector).WithMany().HasForeignKey(x => x.SectorId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Prospect>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProspectActivity>().HasOne(x => x.Prospect).WithMany(x => x.Activities).HasForeignKey(x => x.ProspectId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProspectScan>().HasOne(x => x.Sector).WithMany().HasForeignKey(x => x.SectorId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProspectScan>().HasIndex(x => new { x.Status, x.CreatedUtc });
        builder.Entity<ProspectDistrict>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<Contract>().HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Contract>().HasOne(x => x.Quote).WithMany()
            .HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<WindowDesign>().HasOne(x => x.ProfileSystem).WithMany()
            .HasForeignKey(x => x.ProfileSystemId).OnDelete(DeleteBehavior.Restrict);
        // Muhasebe kayıtlarına bağlı cari, ürün ve hesap silinemez.
        builder.Entity<Invoice>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasOne(x => x.Quote).WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasIndex(x => new { x.Kind, x.CustomerId, x.Number });
        builder.Entity<Invoice>().HasIndex(x => new { x.Status, x.DueDate });
        builder.Entity<InvoiceLine>().HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InvoiceLine>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkLog>().HasIndex(x => x.Date);
        builder.Entity<WorkLog>().HasIndex(x => new { x.EmployeeId, x.Date });
        builder.Entity<WorkLog>().HasIndex(x => new { x.InstallerId, x.Date });
        builder.Entity<WorkLog>().HasOne(x => x.Installer).WithMany().HasForeignKey(x => x.InstallerId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkLog>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkLog>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkLog>().HasOne(x => x.ServiceRequest).WithMany().HasForeignKey(x => x.ServiceRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ServiceRequest>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<ServiceRequest>().HasIndex(x => x.Status);
        builder.Entity<ServiceRequest>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServiceRequest>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ServiceRequest>().HasOne(x => x.Installer).WithMany().HasForeignKey(x => x.InstallerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ServiceVisit>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<ServiceVisit>().HasOne(x => x.ServiceRequest).WithMany(x => x.Visits).HasForeignKey(x => x.ServiceRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<StockMovement>().HasOne<ServiceVisit>().WithMany().HasForeignKey(x => x.ServiceVisitId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ServiceVisit>().HasOne<KarakusAluminyum.Web.Models.Accounting.Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ServicePart>().HasOne<KarakusAluminyum.Web.Models.Accounting.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ServicePart>().HasOne(x => x.ServiceVisit).WithMany(x => x.Parts).HasForeignKey(x => x.ServiceVisitId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PunchItem>().HasIndex(x => new { x.ProductionOrderId, x.Status });
        builder.Entity<PunchItem>().HasIndex(x => x.Status);
        builder.Entity<PunchItem>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PunchItem>().HasOne(x => x.WindowDesign).WithMany().HasForeignKey(x => x.WindowDesignId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<LabelPrinter>().Property(x => x.GapMm).HasColumnType("numeric(5,2)");
        builder.Entity<ExchangeRate>().HasIndex(x => new { x.Currency, x.Date }).IsUnique();
        builder.Entity<StockMovement>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockMovement>().HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<StockMovement>().HasOne<KarakusAluminyum.Web.Models.Production.ProductionOrder>().WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Product>().HasIndex(x => x.Code);
        builder.Entity<CashTransaction>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashTransaction>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashTransaction>().HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashTransaction>().HasIndex(x => new { x.AccountId, x.Date });
        builder.Entity<PaymentAllocation>().HasOne(x => x.Invoice).WithMany(x => x.Allocations).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaymentAllocation>().HasOne(x => x.CashTransaction).WithMany().HasForeignKey(x => x.CashTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PaymentAllocation>().HasOne(x => x.Cheque).WithMany().HasForeignKey(x => x.ChequeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Cheque>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Cheque>().HasOne(x => x.EndorsedToCustomer).WithMany().HasForeignKey(x => x.EndorsedToCustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Cheque>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Cheque>().HasIndex(x => new { x.Status, x.DueDate });
        builder.Entity<Expense>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Expense>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Expense>().HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        // Bağlı kaydı olan proje silinemez; bağlı belge silinirse proje kalır.
        builder.Entity<Invoice>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Expense>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectCostItem>().HasOne(x => x.Project).WithMany(x => x.Items).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CostProject>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<CostProject>().HasOne(x => x.Quote).WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<CostProject>().HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<CariAdjustment>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        // Talep silinince (KVKK) bildirim kaydı ve alıcı bilgisi de silinir.
        builder.Entity<NotificationJob>().HasOne(x => x.ContactMessage).WithMany().HasForeignKey(x => x.ContactMessageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<NotificationJob>().HasIndex(x => new { x.Status, x.NextAttemptUtc });
        builder.Entity<NotificationJob>().HasIndex(x => x.CreatedUtc);
        builder.Entity<NotificationJob>().HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<NotificationJob>().HasOne(x => x.ServiceRequest).WithMany().HasForeignKey(x => x.ServiceRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WindowDesign>().HasOne(x => x.DrawingList).WithMany(x => x.Items).HasForeignKey(x => x.DrawingListId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WindowDesign>().HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DrawingList>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<DrawingList>().HasIndex(x => x.UpdatedUtc);
        builder.Entity<DrawingList>().HasIndex(x => x.PublicToken).IsUnique();
        builder.Entity<WindowDesign>().HasIndex(x => x.UpdatedUtc);
        builder.Entity<WindowDesign>().HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
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
