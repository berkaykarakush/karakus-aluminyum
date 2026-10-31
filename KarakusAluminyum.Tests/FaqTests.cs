using System.Net;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class FaqMigrationTests(PostgresFixture postgres)
{
    private const string Slug = "pvc-dograma";

    private static Task<int> ServiceIdAsync(AppFactory app)
        => app.WithDbAsync(db => db.Services.Where(s => s.Slug == Slug).Select(s => s.Id).SingleAsync());

    [Fact]
    public async Task FirstStart_MovesServiceFaqsToTable_AndSeedsHomeFaqs()
    {
        await using var app = postgres.CreateApp("faq_first_start");
        app.NewClient();
        var serviceId = await ServiceIdAsync(app);

        var serviceFaqs = await app.WithDbAsync(db => db.FaqItems.Where(f => f.ServiceId == serviceId).OrderBy(f => f.SortOrder).ToListAsync());
        Assert.Equal(3, serviceFaqs.Count); // eski genel sorular taşındıktan sonra hizmete özel sorularla değişir
        Assert.DoesNotContain(serviceFaqs, f => f.Question == "Sistem seçimi nasıl yapılır?");
        Assert.All(serviceFaqs, f => Assert.False(string.IsNullOrEmpty(f.Answer)));
        Assert.Equal("[]", await app.WithDbAsync(db => db.Services.Where(s => s.Id == serviceId).Select(s => s.FaqsJson).SingleAsync()));
        Assert.Equal(3, await app.WithDbAsync(db => db.FaqItems.CountAsync(f => f.ServiceId == null)));
    }

    [Fact]
    public async Task Restart_MovesExistingAdminFaqsOnce_KeepingPipesInAnswers()
    {
        await using (var first = postgres.CreateApp("faq_legacy"))
        {
            first.NewClient();
            var id = await ServiceIdAsync(first);
            await first.WithDbAsync(async db =>
            {
                await db.FaqItems.Where(f => f.ServiceId == id).ExecuteDeleteAsync();
                return await db.Services.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.FaqsJson, "[\"Eski soru?|Cevap | devamı\",\"Cevapsız soru?\"]"));
            });
        }

        await using (var second = postgres.CreateApp("faq_legacy")) second.NewClient();

        await using var third = postgres.CreateApp("faq_legacy");
        third.NewClient();
        var serviceId = await ServiceIdAsync(third);
        var faqs = await third.WithDbAsync(db => db.FaqItems.Where(f => f.ServiceId == serviceId).OrderBy(f => f.SortOrder).ToListAsync());

        Assert.Equal(2, faqs.Count);
        Assert.Equal("Eski soru?", faqs[0].Question);
        Assert.Equal("Cevap | devamı", faqs[0].Answer);
        Assert.True(faqs[0].IsPublished);
        Assert.False(faqs[1].IsPublished); // cevapsız kayıt yayında boş görünmesin
    }

    [Fact]
    public async Task Restart_KeepsCorruptLegacyJsonUntouched()
    {
        const string corrupt = "soru|cevap ama JSON değil";
        await using (var first = postgres.CreateApp("faq_corrupt"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Services.Where(s => s.Slug == Slug)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FaqsJson, corrupt)));
        }

        await using var second = postgres.CreateApp("faq_corrupt");
        second.NewClient();
        Assert.Equal(corrupt, await second.WithDbAsync(db => db.Services.Where(s => s.Slug == Slug).Select(s => s.FaqsJson).SingleAsync()));
    }

    [Fact]
    public async Task Restart_DoesNotRestoreDeletedFaqs()
    {
        await using (var first = postgres.CreateApp("faq_no_reseed"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.FaqItems.ExecuteDeleteAsync());
        }

        await using var second = postgres.CreateApp("faq_no_reseed");
        second.NewClient();
        Assert.Equal(0, await second.WithDbAsync(db => db.FaqItems.CountAsync()));
    }
}

[Collection("postgres")]
public sealed class FaqPageTests(PostgresFixture postgres)
{
    private static async Task<string> PageAsync(HttpClient client, string url)
        => WebUtility.HtmlDecode(await client.GetStringAsync(url));

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/sss/ekle"));
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string token, string url, string question, string answer, string serviceId = "", string published = "true")
        => client.PostFormAsync(url, token, ("ServiceId", serviceId), ("Question", question), ("Answer", answer), ("SortOrder", "0"), ("IsPublished", published));

    [Fact]
    public async Task HomePage_ShowsPublishedFaqs_ButNotPlaceholderDraft()
    {
        var html = await PageAsync(postgres.Shared.NewClient(), "/");
        Assert.Contains("Projem için nasıl teklif alabilirim?", html);
        Assert.Contains("iletişim formu üzerinden paylaşabilirsiniz", html);
        Assert.DoesNotContain("İzmir dışında hizmet veriyor musunuz?", html);
    }

    [Fact]
    public async Task ServicePage_ShowsItsFaqsFromDatabase()
        => Assert.Contains("Isı yalıtımlı alüminyum doğrama ne zaman tercih edilmeli?", await PageAsync(postgres.Shared.NewClient(), "/urunler/aluminyum-dograma"));

    [Fact]
    public async Task ServicePage_WithoutFaqs_HidesSection()
    {
        await using var app = postgres.CreateApp("faq_empty_section");
        app.NewClient();
        await app.WithDbAsync(db => db.FaqItems.ExecuteDeleteAsync());

        var client = app.NewClient();
        Assert.DoesNotContain("Sık Sorulan Sorular", await PageAsync(client, "/urunler/aluminyum-dograma"));
        Assert.DoesNotContain("Sık Sorulan Sorular", await PageAsync(client, "/"));
    }

    [Fact]
    public async Task Admin_CreateEditUnpublishDelete_IsReflectedOnHomePage()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var question = "Soru " + Guid.NewGuid().ToString("N")[..8] + "?";

        var created = await SaveAsync(client, token, "/yonetim/sss/ekle", question, "İlk cevap");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = await app.WithDbAsync(db => db.FaqItems.Where(f => f.Question == question).Select(f => f.Id).SingleAsync());
        Assert.Contains("İlk cevap", await PageAsync(client, "/"));
        Assert.Contains(question, await PageAsync(client, "/yonetim/sss?sayfa=genel"));

        await SaveAsync(client, token, $"/yonetim/sss/duzenle/{id}", question, "Yeni cevap");
        var html = await PageAsync(client, "/");
        Assert.Contains("Yeni cevap", html);
        Assert.DoesNotContain("İlk cevap", html);

        await SaveAsync(client, token, $"/yonetim/sss/duzenle/{id}", question, "Yeni cevap", published: "false");
        Assert.DoesNotContain(question, await PageAsync(client, "/"));
        Assert.Contains("Taslak", await PageAsync(client, "/yonetim/sss?sayfa=genel"));

        await client.PostFormAsync($"/yonetim/sss/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.FaqItems.AnyAsync(f => f.Id == id)));
    }

    [Fact]
    public async Task Admin_FaqForService_AppearsOnlyOnThatService()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var serviceId = await app.WithDbAsync(db => db.Services.Where(s => s.Slug == "otomatik-tente").Select(s => s.Id).SingleAsync());
        var question = "Tente " + Guid.NewGuid().ToString("N")[..8] + "?";

        await SaveAsync(client, token, "/yonetim/sss/ekle", question, "Cevap", serviceId.ToString());

        Assert.Contains(question, await PageAsync(client, "/urunler/otomatik-tente"));
        Assert.DoesNotContain(question, await PageAsync(client, "/urunler/pvc-dograma"));
        Assert.DoesNotContain(question, await PageAsync(client, "/"));
        Assert.Contains(question, await PageAsync(client, $"/yonetim/sss?sayfa={serviceId}"));
        Assert.DoesNotContain(question, await PageAsync(client, "/yonetim/sss?sayfa=genel"));
    }

    [Theory]
    [InlineData("", "Cevap var", "")]
    [InlineData("Soru var?", "   ", "")]
    [InlineData("Geçersiz hizmet?", "Cevap var", "999999")]
    public async Task Admin_RejectsInvalidFaq(string question, string answer, string serviceId)
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var before = await app.WithDbAsync(db => db.FaqItems.CountAsync());

        var response = await SaveAsync(client, token, "/yonetim/sss/ekle", question, answer, serviceId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await app.WithDbAsync(db => db.FaqItems.CountAsync()));
    }

    [Theory]
    [InlineData("/yonetim/sss/duzenle/999999", "GET")]
    [InlineData("/yonetim/sss/sil/999999", "POST")]
    public async Task Admin_UnknownFaq_ReturnsNotFound(string url, string method)
    {
        var (client, token) = await AdminAsync(postgres.Shared);
        var response = method == "GET" ? await client.GetAsync(url) : await client.PostFormAsync(url, token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_NewServiceWithoutFaqField_IsSaved_AndDeleteRemovesItsFaqs()
    {
        await using var app = postgres.CreateApp("faq_service_form");
        var (client, token) = await AdminAsync(app);
        var form = await client.GetStringAsync("/yonetim/hizmetler/ekle");
        Assert.DoesNotContain("FaqsJson", form);

        var created = await client.PostFormAsync("/yonetim/hizmetler/ekle", token,
            ("Name", "Test Hizmeti"), ("Slug", "test-hizmeti"), ("ShortDescription", "Kısa"), ("Intro", "Detay"),
            ("SeoTitle", "Başlık"), ("SeoDescription", "Açıklama"), ("BenefitsJson", "[]"), ("SortOrder", "99"), ("IsPublished", "true"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var serviceId = await app.WithDbAsync(db => db.Services.Where(s => s.Slug == "test-hizmeti").Select(s => s.Id).SingleAsync());

        await SaveAsync(client, token, "/yonetim/sss/ekle", "Silinecek soru?", "Cevap", serviceId.ToString());
        Assert.Contains("Silinecek soru?", await PageAsync(client, "/urunler/test-hizmeti"));
        Assert.Contains($"/yonetim/sss?sayfa={serviceId}", await client.GetStringAsync($"/yonetim/hizmetler/duzenle/{serviceId}"));

        await client.PostFormAsync($"/yonetim/hizmetler/sil/{serviceId}", token);
        Assert.False(await app.WithDbAsync(db => db.FaqItems.AnyAsync(f => f.ServiceId == serviceId)));
    }

    [Fact]
    public async Task PublicPage_EscapesHtmlInFaqs()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var marker = Guid.NewGuid().ToString("N")[..8];

        await SaveAsync(client, token, "/yonetim/sss/ekle", $"<script>x{marker}</script>?", "Cevap");

        Assert.DoesNotContain($"<script>x{marker}", await client.GetStringAsync("/"));
    }
}
