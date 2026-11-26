using System.Net;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KarakusAluminyum.Tests;

// Yardım sayfası: tüm rollere açık, menüden ulaşılır ve anlattığı her panel ekranına çalışan bağlantı verir.
[Collection("postgres")]
public sealed class HelpTests(PostgresFixture postgres)
{
    private static async Task<HttpClient> UserAsync(AppFactory app, string role)
    {
        var email = $"help-{Guid.NewGuid():N}@test.local";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var client = app.NewClient();
        await client.LoginAsync(email);
        return client;
    }

    [Fact]
    public async Task Help_ExplainsSampleScenario_AndIsLinkedFromTheMenu()
    {
        var (admin, _) = await QuoteDocumentTests.AdminAsync(postgres.Shared);
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim/yardim"));
        foreach (var text in new[] { "Örnek senaryo", "Keşif randevusu oluştur", "Teklif hazırla", "Kapora + kalan planı oluştur", "Teslim tutanağı", "İlk kurulum kontrol listesi", "Sık sorulanlar" })
            Assert.Contains(text, html);
        Assert.Contains("href=\"/yonetim/yardim\"", WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim")));
    }

    [Theory]
    [InlineData("Editor")]
    [InlineData("Accountant")]
    public async Task Help_IsOpenToEveryPanelRole(string role)
    {
        var client = await UserAsync(postgres.Shared, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/yonetim/yardim")).StatusCode);
    }

    [Fact]
    public async Task Help_IsClosedToVisitors()
    {
        var response = await postgres.Shared.NewClient().GetAsync("/yonetim/yardim");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("yonetim-giris", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Ekran adresi değişirse yardım sayfası kırık bağlantı bırakmasın.
    [Fact]
    public async Task Help_LinksOpenRealPanelPages()
    {
        var (admin, _) = await QuoteDocumentTests.AdminAsync(postgres.Shared);
        var html = await admin.GetStringAsync("/yonetim/yardim");
        var main = html[html.IndexOf("<main", StringComparison.Ordinal)..];
        var links = Regex.Matches(main, "href=\"(/yonetim/[^\"#?]*)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(links.Count > 30, $"Yardımda yalnızca {links.Count} panel bağlantısı bulundu.");
        foreach (var link in links)
        {
            var response = await admin.GetAsync(link);
            // Bazı ekranlar ilk sekmesine yönlendirir (ör. sosyal medya); giriş sayfasına yönlendirme ise hatadır.
            if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is { } next && !next.ToString().Contains("yonetim-giris", StringComparison.OrdinalIgnoreCase))
                response = await admin.GetAsync(next);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{link} açılmadı ({(int)response.StatusCode}).");
        }
    }
}
