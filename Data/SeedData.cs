using System.Text.Json;
using KarakusAluminyum.Web.Models;
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
            "/images/endustriyel-ve-garaj-kapi-sistemleri.png"),
        new("Rampa ve Körük Sistemleri", 
            "rampa-ve-koruk-sistemleri", 
            "Yükleme alanlarında kullanılmak üzere rampa ve körük sistemi çözümleri.",
            "/images/rampa-ve-koruk-sistemleri.png"),
        new("Bahçe Kapısı ve Bariyer Sistemleri", 
            "bahce-kapisi-ve-bariyer-sistemleri", 
            "Araç ve yaya girişleri için bahçe kapısı ve bariyer sistemleri.",
            "/images/bahce-kapisi-ve-bariyer-sistemleri.png"),
    };

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Admin", "Editor" })
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
                || existing.Image.Contains("image-placeholder", StringComparison.OrdinalIgnoreCase))
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

        // Doğrulanmış firma bilgileri. Bilinmeyen sosyal medya hesapları veya iletişim kanalları uydurulmaz.
        var settingSeeds = new Dictionary<string, (string Value, string Description)>
        {
            ["SiteName"] = ("Karakuş Alüminyum", "Web sitesi başlığı"),
            ["BaseUrl"] = ("https://www.karakusaluminyum.com", "Canonical alan adı"),
            ["PhoneDisplay"] = ("+90 553 835 48 72", "Görünen telefon"),
            ["PhoneE164"] = ("+905538354872", "Uluslararası telefon biçimi"),
            ["Email"] = ("info@karakusaluminyum.com", "İletişim e-postası"),
            ["Address"] = ("Karacaoğlan Mah. 6252 Sk. No: 3, Bornova/İzmir", "İşletme adresi"),
            ["City"] = ("İzmir", "Şehir"),
            ["PrimaryServiceArea"] = ("Bornova", "Ana hizmet bölgesi"),
            ["WhatsAppNumber"] = ("905538354872", "Ülke koduyla WhatsApp numarası"),
            ["GoogleMapsUrl"] = ("https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d3125.168057091929!2d27.206807025382613!3d38.43758697370485!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x14b962db502ab2f7%3A0xe5025465f1957d53!2zS2FyYWt1xZ8gQWzDvG1pbnl1bQ!5e0!3m2!1str!2str!4v1793255765258!5m2!1str!2str", "Google Maps bağlantısı"),
            ["GoogleBusinessProfileUrl"] = ("https://share.google/0QhP26b6Mp7BmGUGt", "Google İşletme Profili bağlantısı; doğrulandıktan sonra girilmeli"),
            ["InstagramUrl"] = ("https://www.instagram.com/karakus-aluminyum", "Instagram bağlantısı; doğrulandıktan sonra girilmeli"),
            ["FacebookUrl"] = ("https://www.facebook.com/karakus-aluminyum", "Facebook bağlantısı; doğrulandıktan sonra girilmeli"),
            ["LinkedInUrl"] = ("https://www.linkedin.com/karakus-aluminyum", "LinkedIn bağlantısı")
        };

        var settingKeys = settingSeeds.Keys.ToArray();
        var existingSettings = await db.SiteSettingRecords.Where(s => settingKeys.Contains(s.Key)).ToListAsync();
        var authoritativeKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "SiteName", "BaseUrl", "PhoneDisplay", "PhoneE164", "Email", "Address", "City", "PrimaryServiceArea", "WhatsAppNumber"
        };

        foreach (var pair in settingSeeds)
        {
            var setting = existingSettings.FirstOrDefault(s => s.Key == pair.Key);
            if (setting is null)
            {
                db.SiteSettingRecords.Add(new SiteSettingRecord { Key = pair.Key, Value = pair.Value.Value, Description = pair.Value.Description });
                continue;
            }

            // Kesin firma bilgilerini düzelt; sosyal bağlantılar gibi doğrulanmamış/isteğe bağlı alanları mevcutsa ezme.
            if (authoritativeKeys.Contains(pair.Key))
            {
                setting.Value = pair.Value.Value;
                setting.Description = pair.Value.Description;
            }
            else if (string.IsNullOrWhiteSpace(setting.Value)
                || setting.Value.Contains("DOMAININIZ", StringComparison.OrdinalIgnoreCase)
                || setting.Value.Contains("XXX", StringComparison.OrdinalIgnoreCase)
                || setting.Value.Contains("XXXXXXXX", StringComparison.OrdinalIgnoreCase)
                || setting.Value == "İzmir, Türkiye"
                || setting.Value == "0232 XXX XX XX"
                || setting.Value == "90XXXXXXXXXX")
            {
                setting.Value = pair.Value.Value;
                setting.Description = pair.Value.Description;
            }
        }

        // Yeni eklenen hizmet/projeler galeri seed'inin sorgularında görünsün diye önce kaydedilir.
        await db.SaveChangesAsync();

        // Galeri seed: mevcut wwwroot görselleriyle hizmet/proje galerilerini doldur.
        if (!await db.GalleryImages.AnyAsync())
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