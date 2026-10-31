using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using KarakusAluminyum.Web.Controllers;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddRazorPages();
// Container yeniden başladığında kimlik doğrulama çerezlerinin geçerli kalması için anahtar deposu kalıcı tutulur.
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .SetApplicationName("KarakusAluminyum")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services.AddHealthChecks();
builder.Services.Configure<SiteSettings>(builder.Configuration.GetSection("SiteSettings"));
builder.Services.AddScoped<SeoService>();
builder.Services.AddScoped<LengthUnits>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<KarakusAluminyum.Web.Services.Accounting.AccountingService>();
builder.Services.AddScoped<KarakusAluminyum.Web.Services.Accounting.AccountingQueries>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AnalyticsController.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddScoped<SiteSettingsProvider>();
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddHttpClient(nameof(ContactNotificationService), client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IContactNotificationService, ContactNotificationService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), npgsql => npgsql.EnableRetryOnFailure(3)));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/yonetim-giris";
    options.AccessDeniedPath = "/erisim-engellendi";
    options.Cookie.Name = "KarakusAluminyum.AdminAuth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

var app = builder.Build();

// Şema yönetimi açıkça seçilir. Production'da var olan DB'yi EnsureCreated ile değiştirmeyin.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var schemaMode = app.Configuration["Database:SchemaMode"] ?? (app.Environment.IsDevelopment() ? "ensurecreated" : "existing");
    if (schemaMode.Equals("ensurecreated", StringComparison.OrdinalIgnoreCase))
        await db.Database.EnsureCreatedAsync();
    else if (schemaMode.Equals("migrate", StringComparison.OrdinalIgnoreCase))
    {
        if (!db.Database.GetMigrations().Any())
            throw new InvalidOperationException("Database:SchemaMode=migrate seçildi ancak EF Core migration bulunamadı. Önce migration oluşturun ve mevcut veritabanı için baseline planını uygulayın.");
        await db.Database.MigrateAsync();
    }
    else if (!schemaMode.Equals("existing", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Database:SchemaMode yalnızca ensurecreated, migrate veya existing olabilir.");

    // Idempotent CRM/audit DDL, mevcut kurulumlarda geçiş sırasında korunması gereken şemayı ekler.
    await CrmSchemaInitializer.EnsureCreatedAsync(db);
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/hata/500");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "healthy", database = "reachable" })
            : Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
app.Run();

// Entegrasyon testlerinde WebApplicationFactory<Program> için gerekli.
public partial class Program { }
