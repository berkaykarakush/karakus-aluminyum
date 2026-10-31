using KarakusAluminyum.Web.Models.Accounting;
using System.Text.Json;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Data;

public static class SeedData
{
    private sealed record ServiceSeed(string Name, string Slug, string Description, string ImageUrl);

    private static readonly ServiceSeed[] Services =
    {
        new("Alüminyum Doğrama", 
            "aluminyum-dograma", 
            "Konut ve ticari yapılar için projeye uygun alüminyum doğrama çözümleri.", 
            "/images/aluminyum-dograma.png"),
        new("PVC Doğrama", 
            "pvc-dograma", 
            "Kapı ve pencere uygulamaları için kullanım ihtiyacına uygun PVC doğrama çözümleri."
            ,"/images/pvc-dograma.png"),
        new("Otomatik Giyotin Camlama Sistemleri", 
            "otomatik-giyotin-camlama-sistemleri", 
            "Dikey hareketli cam uygulamaları için otomatik giyotin camlama sistemleri.",
            "/images/otomatik-giyotin-camlama-sistemleri.png"),
        new("Manuel Giyotin Rüzgâr Kırıcı Sistemleri", 
            "manuel-giyotin-ruzgar-kirici-sistemleri", 
            "Teras ve açık alanlarda değerlendirilebilen manuel giyotin cam ve rüzgâr kırıcı çözümleri.",
            "/images/manuel-giyotin-ruzgar-kirici-sistemleri.png"),
        new("Katlanır Cam Sistemleri", 
            "katlanir-cam-sistemleri", 
            "Balkon, teras ve benzeri alanlar için katlanır cam sistemi seçenekleri.",
            "/images/katlanir-cam-sistemleri.png"),
        new("Yana Kayar Sürme Cam Sistemleri", 
            "yana-kayar-surme-cam-sistemleri", 
            "Geniş açıklıklar ve iç-dış mekân geçişleri için yana kayar sürme cam sistemleri.",
            "/images/yana-kayar-surme-cam-sistemleri.png"),
        new("Otomatik Tente", 
            "otomatik-tente", 
            "Dış mekân gölgelendirmesi için otomatik tente çözümleri.",
            "/images/otomatik-tente.png"),
        new("Fotoselli Döner Kapı", 
            "fotoselli-doner-kapi", 
            "Bina girişleri için fotoselli ve döner kapı sistemleri.",
            "/images/fotoselli-doner-kapi.png"),
        new("Sarmal Kepenk ve Panjur Sistemleri", 
            "sarmal-kepenk-ve-panjur-sistemleri", 
            "Yapıların kapı ve pencere açıklıkları için kepenk ve panjur sistemleri.",
            "/images/sarmal-kepenk-ve-panjur-sistemleri.png"),
        new("Ofis Bölme Sistemleri", 
            "ofis-bolme-sistemleri", 
            "Ofis alanlarının kullanım ihtiyacına göre düzenlenmesine yönelik bölme sistemleri.",
            "/images/ofis-bolme-sistemleri.png"),
        new("Endüstriyel ve Garaj Kapı Sistemleri", 
            "endustriyel-ve-garaj-kapi-sistemleri", 
            "Endüstriyel alanlar ve garajlar için kapı sistemi çözümleri.",
            "/images/otomatik-kapi-ve-diger-sistemler.png"),
        new("Rampa ve Körük Sistemleri", 
            "rampa-ve-koruk-sistemleri", 
            "Yükleme alanlarında kullanılmak üzere rampa ve körük sistemi çözümleri.",
            "/images/otomatik-kapi-ve-diger-sistemler.png"),
        new("Bahçe Kapısı ve Bariyer Sistemleri", 
            "bahce-kapisi-ve-bariyer-sistemleri", 
            "Araç ve yaya girişleri için bahçe kapısı ve bariyer sistemleri.",
            "/images/otomatik-kapi-ve-diger-sistemler.png"),
    };

    // Önceki seed sürümünün kaydettiği, wwwroot'ta hiç bulunmayan görsel yolları.
    private static readonly string[] MissingServiceImages =
    {
        "/images/endustriyel-ve-garaj-kapi-sistemleri.png",
        "/images/rampa-ve-koruk-sistemleri.png",
        "/images/bahce-kapisi-ve-bariyer-sistemleri.png"
    };

    public const string GallerySeededKey = "GallerySeeded";
    public const string HomeFaqsSeededKey = "HomeFaqsSeeded";
    public const string DrawingSeededKey = "DrawingSeeded";
    public const string ClausesSeededKey = "ClausesSeeded";
    public const string ProductSystemsSeededKey = "ProductSystemsSeeded";
    public const string BrandsSeededKey = "BrandsSeeded";
    public const string AccountingSeededKey = "AccountingSeeded";
    private const string BrandSeedVersion = "2";

    // Listeye marka eklenince BrandSeedVersion artırılır.
    private static readonly (string Name, string Category)[] Brands =
    {
        ("Linea Rossa", ProductCategories.Aluminum), ("Asaş", ProductCategories.Aluminum), ("Çuhadaroğlu", ProductCategories.Aluminum), ("Burak Alüminyum", ProductCategories.Aluminum),
        ("Sistem Alüminyum", ProductCategories.Aluminum), ("Alutem", ProductCategories.Aluminum), ("Özgür", ProductCategories.Aluminum), ("Reynaers", ProductCategories.Aluminum),
        ("Schüco", ProductCategories.Aluminum), ("Alumil", ProductCategories.Aluminum),
        ("Winer", ProductCategories.Pvc), ("Egepen", ProductCategories.Pvc), ("Winsa", ProductCategories.Pvc), ("Fıratpen", ProductCategories.Pvc),
        ("Pimapen", ProductCategories.Pvc), ("Adopen", ProductCategories.Pvc), ("Kalde", ProductCategories.Pvc), ("Rehau", ProductCategories.Pvc),
        ("Veka", ProductCategories.Pvc), ("Gealan", ProductCategories.Pvc), ("Penhouse", ProductCategories.Pvc), ("Baywin", ProductCategories.Pvc),
        ("Aline", ProductCategories.Pvc), ("Kompen", ProductCategories.Pvc),
        ("Asaş", ProductCategories.Shutter),
        ("Vitrum", ProductCategories.Railing), ("Apart Grup", ProductCategories.Railing), ("Alutem", ProductCategories.Railing),
        ("Albert Genau", ProductCategories.GlassBalcony), ("Vitrum", ProductCategories.GlassBalcony), ("Apart Grup", ProductCategories.GlassBalcony), ("Black Cam Balkon", ProductCategories.GlassBalcony)
    };


    // Tek seferlik seed'lerin işaretçileri; site ayarları ekranında gösterilmez.
    public static readonly string[] InternalSettingKeys = { GallerySeededKey, HomeFaqsSeededKey, DrawingSeededKey, ClausesSeededKey, ProductSystemsSeededKey, BrandsSeededKey, AccountingSeededKey, KarakusAluminyum.Web.Services.Seo.IndexNowPublisher.KeySetting, KarakusAluminyum.Web.Services.Seo.IndexNowPublisher.MarkerSetting };

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Admin", "Editor", "Accountant" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                if (!roleResult.Succeeded)
                    throw new InvalidOperationException($"'{role}' rolü oluşturulamadı: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
            }
        }

        var adminEmail = Environment.GetEnvironmentVariable("KARAKUS_ADMIN_EMAIL") ?? config["AdminSeed:Email"];
        var adminPassword = Environment.GetEnvironmentVariable("KARAKUS_ADMIN_PASSWORD") ?? config["AdminSeed:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin is null)
            {
                admin = new ApplicationUser { UserName = adminEmail.Trim(), Email = adminEmail.Trim(), EmailConfirmed = true };
                var result = await userManager.CreateAsync(admin, adminPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException("Admin hesabı oluşturulamadı: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                var roleResult = await userManager.AddToRoleAsync(admin, "Admin");
                if (!roleResult.Succeeded)
                    throw new InvalidOperationException("Admin rolü atanamadı: " + string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }
        }

        // Eksik hizmetleri ekle. Mevcut yönetici içeriğini koru; yalnızca boş/placeholder görsel yolunu düzelt.
        var existingServices = await db.Services.ToListAsync();
        var nextServiceOrder = (existingServices.Select(x => (int?)x.SortOrder).Max() ?? -1) + 1;

        foreach (var seed in Services)
        {
            var existing = existingServices.FirstOrDefault(x =>
                string.Equals(x.Slug, seed.Slug, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var service = new ServiceContent
                {
                    Name = seed.Name,
                    Slug = seed.Slug,
                    ShortDescription = seed.Description,
                    Intro = seed.Description + " Uygun sistem seçimi; alanın ölçüleri, kullanım amacı ve uygulama koşulları değerlendirilerek yapılmalıdır. Projeniz için detay ve teklif almak üzere bizimle iletişime geçebilirsiniz.",
                    Image = seed.ImageUrl,
                    SeoTitle = seed.Name + " | Karakuş Alüminyum",
                    SeoDescription = "İzmir'de " + seed.Name.ToLowerInvariant() + " çözümleri. Projeniz hakkında bilgi ve teklif almak için Karakuş Alüminyum ile iletişime geçin.",
                    BenefitsJson = JsonSerializer.Serialize(new[] { "Projeye göre değerlendirme", "Kullanım ihtiyacına uygun sistem seçimi", "Uygulama öncesi ölçü ve detayların değerlendirilmesi" }),
                    FaqsJson = JsonSerializer.Serialize(new[]
                    {
                        "Sistem seçimi nasıl yapılır?|Ölçüler, kullanım amacı, uygulama koşulları ve beklentiler değerlendirilerek uygun seçenekler belirlenir.",
                        "Teklif almak için hangi bilgiler gerekir?|Proje konumu, yaklaşık ölçüler, varsa çizimler ve istenen sistem bilgileri ilk değerlendirmeyi kolaylaştırır."
                    }),
                    SortOrder = nextServiceOrder++,
                    IsPublished = true
                };
                db.Services.Add(service);
                existingServices.Add(service);
            }
            else if (string.IsNullOrWhiteSpace(existing.Image)
                || existing.Image.Contains("image-placeholder", StringComparison.OrdinalIgnoreCase)
                || MissingServiceImages.Contains(existing.Image))
            {
                // Önceki seed sürümü tüm hizmetlere placeholder atıyordu; bu hatalı yolu düzelt.
                existing.Image = seed.ImageUrl;
            }
        }

        // Örnek/placeholder proje kayıtlarını kaldır; gerçek proje adları kullanıcı tarafından sağlandı.
        var placeholderSlugs = new[] { "ornek-konut-projesi", "ornek-ticari-yapi", "ornek-surme-sistem" };
        var placeholders = await db.Projects.Where(p => placeholderSlugs.Contains(p.Slug)).ToListAsync();
        if (placeholders.Count > 0) db.Projects.RemoveRange(placeholders);

        var projectSeeds = new[]
        {
            new PortfolioProject { Slug = "nisu-insaat-a-evleri", Title = "Nisu İnşaat A Evleri", Type = "Konut Projesi", Summary = "Nisu İnşaat A Evleri projesi. Uygulanan sistemler ve proje detayları firma tarafından doğrulandıktan sonra bu alana eklenebilir.", Image = "/images/image-placeholder.svg", Location = "İzmir", IsPublished = true },
            new PortfolioProject { Slug = "firat-insaat-b-evleri", Title = "Fırat İnşaat B Evleri", Type = "Konut Projesi", Summary = "Fırat İnşaat B Evleri projesi. Uygulanan sistemler ve proje detayları firma tarafından doğrulandıktan sonra bu alana eklenebilir.", Image = "/images/image-placeholder.svg", Location = "İzmir", IsPublished = true },
            new PortfolioProject { Slug = "orhan-insaat-rm-evleri", Title = "Orhan İnşaat RM Evleri", Type = "Konut Projesi", Summary = "Orhan İnşaat RM Evleri projesi. Uygulanan sistemler ve proje detayları firma tarafından doğrulandıktan sonra bu alana eklenebilir.", Image = "/images/image-placeholder.svg", Location = "İzmir", IsPublished = true }
        };
        foreach (var project in projectSeeds)
        {
            var existing = await db.Projects.FirstOrDefaultAsync(p => p.Slug == project.Slug);
            if (existing is null) db.Projects.Add(project);
        }

        if (!await db.BlogPosts.AnyAsync())
        {
            db.BlogPosts.Add(new BlogPost
            {
                Title = "Alüminyum ve Mimari Sistemler Hakkında",
                Slug = "aluminyum-ve-mimari-sistemler",
                Summary = "Karakuş Alüminyum hizmetleri ve mimari sistemler hakkında bilgilendirici içerikler.",
                Content = "Bu yazı taslak olarak oluşturulmuştur. Yayına almadan önce teknik bilgiler firma tarafından gözden geçirilmeli ve özgün içerikle tamamlanmalıdır.",
                IsPublished = false
            });
        }

        // Eksik ayarları ekle. Yöneticinin değerine dokunma; yalnızca eski yer tutucular düzeltilir.
        var existingSettings = await db.SiteSettingRecords.ToListAsync();
        foreach (var definition in SiteSettingCatalog.All)
        {
            var setting = existingSettings.FirstOrDefault(s => s.Key == definition.Key);
            if (setting is null)
                db.SiteSettingRecords.Add(new SiteSettingRecord { Key = definition.Key, Value = definition.Default, Description = definition.Label });
            else if (setting.Value.Contains("DOMAININIZ", StringComparison.OrdinalIgnoreCase) || setting.Value.Contains("XXX", StringComparison.OrdinalIgnoreCase))
                setting.Value = definition.Default;
        }

        // Anonim ziyaret kayıtları 400 günden uzun tutulmaz.
        await db.AnalyticsEvents.Where(e => e.OccurredUtc < DateTime.UtcNow.AddDays(-400)).ExecuteDeleteAsync();

        // Yeni eklenen hizmet/projeler galeri seed'inin sorgularında görünsün diye önce kaydedilir.
        await db.SaveChangesAsync();

        // Hizmetlerdeki eski "soru|cevap" JSON kayıtlarını FaqItems'a taşı. Taşınan satır "[]" olduğu için tekrar çalışmaz;
        // bozuk JSON olduğu gibi bırakılır, veri kaybolmaz.
        foreach (var service in await db.Services.Where(s => s.FaqsJson != "[]").ToListAsync())
        {
            List<string>? legacyFaqs;
            try { legacyFaqs = JsonSerializer.Deserialize<List<string>>(service.FaqsJson); }
            catch (JsonException) { continue; }

            var order = 0;
            foreach (var entry in (legacyFaqs ?? new()).Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var parts = entry.Split('|', 2);
                var answer = parts.Length > 1 ? parts[1].Trim() : "";
                // Cevabı olmayan kayıt yayında boş görünmesin diye taslak olarak taşınır.
                db.FaqItems.Add(new FaqItem { ServiceId = service.Id, Question = parts[0].Trim(), Answer = answer, SortOrder = order++, IsPublished = answer.Length > 0 });
            }
            service.FaqsJson = "[]";
        }

        // Ana sayfa SSS'leri: yalnızca bir kez eklenir; yöneticinin sildikleri geri gelmez.
        if (!await db.SiteSettingRecords.AnyAsync(s => s.Key == HomeFaqsSeededKey))
        {
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = HomeFaqsSeededKey, Value = "true", Description = "Ana sayfa SSS seed'i uygulandı" });
            db.FaqItems.AddRange(
                new FaqItem { Question = "Alüminyum doğrama nasıl seçilir?", Answer = "Açıklık ölçüsü, kullanım amacı, mimari tasarım, yalıtım beklentisi ve donanım seçenekleri birlikte değerlendirilmelidir.", SortOrder = 0 },
                new FaqItem { Question = "Projem için nasıl teklif alabilirim?", Answer = "Proje konumu, yaklaşık ölçüler, tercih edilen sistem ve varsa çizimleri iletişim formu üzerinden paylaşabilirsiniz.", SortOrder = 1 },
                // Cevabı firma tarafından doldurulacak şablon metin; doldurulana kadar yayınlanmaz.
                new FaqItem { Question = "İzmir dışında hizmet veriyor musunuz?", Answer = "Hizmet verdiğiniz il ve ilçeleri gerçek operasyon kapsamınıza göre burada belirtin.", SortOrder = 2, IsPublished = false });
        }

        // Çizim modülü başlangıç tanımları: ölçüler yer tutucu örnektir, fiyatlar bilerek boştur.
        if (!await db.SiteSettingRecords.AnyAsync(s => s.Key == DrawingSeededKey))
        {
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = DrawingSeededKey, Value = "true", Description = "Çizim modülü seed'i uygulandı" });
            db.ProfileSystems.Add(new ProfileSystem { Name = "Örnek Sistem (kendi profil değerlerinizi girin)" });
            db.GlassTypes.Add(new GlassType { Name = "Örnek Isıcam 4+16+4 (fiyat girin)" });
        }

        // Ürün türü başına başlangıç sistemi: ölçüler yer tutucu örnektir, fiyatlar bilerek boştur.
        if (!await db.SiteSettingRecords.AnyAsync(s => s.Key == ProductSystemsSeededKey))
        {
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = ProductSystemsSeededKey, Value = "true", Description = "Ürün türü sistemleri eklendi" });
            await db.SaveChangesAsync(); // üstteki örnek sistem aşağıdaki kontrolde görünsün
            foreach (var category in ProductCategories.All)
                if (!await db.ProfileSystems.AnyAsync(p => p.Category == category))
                    db.ProfileSystems.Add(new ProfileSystem { Name = $"Örnek {ProductCategories.Label(category)} (değerleri girin)", Category = category, Material = ProductCategories.Label(category) });
        }

        // Başlangıç markaları: logolar panelden yüklenir, o zamana kadar marka adı yazılır.
        // Yalnızca sürüm değişince eksikler eklenir; panelden silinen marka her açılışta geri gelmez.
        var brandMarker = await db.SiteSettingRecords.FirstOrDefaultAsync(s => s.Key == BrandsSeededKey);
        if (brandMarker?.Value != BrandSeedVersion)
        {
            if (brandMarker is null) db.SiteSettingRecords.Add(brandMarker = new SiteSettingRecord { Key = BrandsSeededKey, Description = "Başlangıç markaları eklendi" });
            brandMarker.Value = BrandSeedVersion;
            var existingBrands = (await db.Brands.AsNoTracking().Select(b => new { b.Name, b.Category }).ToListAsync()).Select(b => (b.Name, b.Category)).ToHashSet();
            for (var i = 0; i < Brands.Length; i++)
                if (!existingBrands.Contains(Brands[i]))
                    db.Brands.Add(new Brand { Name = Brands[i].Name, Category = Brands[i].Category, SortOrder = i + 1 });
        }

        // Muhasebe başlangıcı: merkez kasa ve yaygın gider kategorileri.
        if (!await db.SiteSettingRecords.AnyAsync(s => s.Key == AccountingSeededKey))
        {
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = AccountingSeededKey, Value = "true", Description = "Muhasebe başlangıç kayıtları eklendi" });
            db.FinanceAccounts.Add(new FinanceAccount { Name = "Merkez Kasa", Kind = FinanceAccountKind.Cash });
            foreach (var name in new[] { "Kira", "Elektrik, su, doğalgaz", "Telefon ve internet", "Yakıt ve ulaşım", "Araç giderleri", "Ofis ve kırtasiye", "Vergi ve harçlar", "SGK", "Banka masrafları", "Muhasebe ve danışmanlık", "Reklam", "Yemek", "Bakım ve onarım", "Diğer" })
                db.ExpenseCategories.Add(new ExpenseCategory { Name = name });
        }

        // Belge şablonları her zaman vardır; hazır maddeler yalnızca bir kez eklenir.
        if (!await db.DocumentTemplates.AnyAsync(t => t.Kind == Models.Documents.DocumentKinds.Quote)) db.DocumentTemplates.Add(DocumentSeed.QuoteTemplate());
        if (!await db.DocumentTemplates.AnyAsync(t => t.Kind == Models.Documents.DocumentKinds.Contract)) db.DocumentTemplates.Add(DocumentSeed.ContractTemplate());
        if (!await db.SiteSettingRecords.AnyAsync(s => s.Key == ClausesSeededKey))
        {
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = ClausesSeededKey, Value = "true", Description = "Hazır belge maddeleri eklendi" });
            db.TemplateClauses.AddRange(DocumentSeed.Clauses());
        }

        // Galeri seed: mevcut wwwroot görselleriyle hizmet/proje galerilerini doldur.
        // Yalnızca bir kez çalışır; yöneticinin sildiği görseller yeniden başlatınca geri gelmez.
        var gallerySeeded = await db.SiteSettingRecords.AnyAsync(s => s.Key == GallerySeededKey);
        if (!gallerySeeded)
            db.SiteSettingRecords.Add(new SiteSettingRecord { Key = GallerySeededKey, Value = "true", Description = "Galeri seed'i uygulandı" });
        if (!gallerySeeded && !await db.GalleryImages.AnyAsync())
        {
            var serviceIds = await db.Services.ToDictionaryAsync(s => s.Slug, s => s.Id);
            var projectIds = await db.Projects.ToDictionaryAsync(p => p.Slug, p => p.Id);

            void AddImages(int? serviceId, int? projectId, params string[] paths)
            {
                for (var i = 0; i < paths.Length; i++)
                    db.GalleryImages.Add(new GalleryImage
                    {
                        ServiceId = serviceId, ProjectId = projectId,
                        ImagePath = paths[i], SortOrder = i, IsPublished = true
                    });
            }

            // Hizmet galerileri
            if (serviceIds.TryGetValue("aluminyum-dograma", out var id))
                AddImages(id, null, "/images/aluminyum-dograma.png", "/images/kapi-ve-pencere-sistemleri.png", "/images/sarmal-kepenk-ve-panjur-sistemleri.png");
            if (serviceIds.TryGetValue("pvc-dograma", out id))
                AddImages(id, null, "/images/pvc-dograma.png", "/images/kapi-ve-pencere-sistemleri.png");
            if (serviceIds.TryGetValue("yana-kayar-surme-cam-sistemleri", out id))
                AddImages(id, null, "/images/yana-kayar-surme-cam-sistemleri.png", "/images/sürme-sistemleri.png", "/images/cam-balkon.png");
            if (serviceIds.TryGetValue("katlanir-cam-sistemleri", out id))
                AddImages(id, null, "/images/katlanir-cam-sistemleri.png", "/images/cam-balkon.png");
            if (serviceIds.TryGetValue("otomatik-giyotin-camlama-sistemleri", out id))
                AddImages(id, null, "/images/otomatik-giyotin-camlama-sistemleri.png", "/images/manuel-giyotin-ruzgar-kirici-sistemleri.png");
            if (serviceIds.TryGetValue("manuel-giyotin-ruzgar-kirici-sistemleri", out id))
                AddImages(id, null, "/images/manuel-giyotin-ruzgar-kirici-sistemleri.png");
            if (serviceIds.TryGetValue("otomatik-tente", out id))
                AddImages(id, null, "/images/otomatik-tente.png");
            if (serviceIds.TryGetValue("fotoselli-doner-kapi", out id))
                AddImages(id, null, "/images/fotoselli-doner-kapi.png", "/images/otomatik-kapi-ve-diger-sistemler.png");
            if (serviceIds.TryGetValue("sarmal-kepenk-ve-panjur-sistemleri", out id))
                AddImages(id, null, "/images/sarmal-kepenk-ve-panjur-sistemleri.png");
            if (serviceIds.TryGetValue("ofis-bolme-sistemleri", out id))
                AddImages(id, null, "/images/ofis-bolme-sistemleri.png");
            if (serviceIds.TryGetValue("endustriyel-ve-garaj-kapi-sistemleri", out id))
                AddImages(id, null, "/images/otomatik-kapi-ve-diger-sistemler.png");
            if (serviceIds.TryGetValue("rampa-ve-koruk-sistemleri", out id))
                AddImages(id, null, "/images/otomatik-kapi-ve-diger-sistemler.png");
            if (serviceIds.TryGetValue("bahce-kapisi-ve-bariyer-sistemleri", out id))
                AddImages(id, null, "/images/otomatik-kapi-ve-diger-sistemler.png");

            // Proje galerileri
            if (projectIds.TryGetValue("nisu-insaat-a-evleri", out var pid))
                AddImages(null, pid, "/images/aluminyum-dograma.png", "/images/yana-kayar-surme-cam-sistemleri.png", "/images/katlanir-cam-sistemleri.png");
            if (projectIds.TryGetValue("firat-insaat-b-evleri", out pid))
                AddImages(null, pid, "/images/kapi-ve-pencere-sistemleri.png", "/images/otomatik-giyotin-camlama-sistemleri.png");
            if (projectIds.TryGetValue("orhan-insaat-rm-evleri", out pid))
                AddImages(null, pid, "/images/cam-balkon.png", "/images/sarmal-kepenk-ve-panjur-sistemleri.png");

            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
    }
}