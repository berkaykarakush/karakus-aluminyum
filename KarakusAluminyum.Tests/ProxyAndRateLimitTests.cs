using System.Net;
using Xunit;

namespace KarakusAluminyum.Tests;

[Collection("postgres")]
public sealed class ProxyAndRateLimitTests(PostgresFixture postgres)
{
    private static HttpClient ClientFrom(AppFactory app, string ip)
    {
        var client = app.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        return client;
    }

    private static async Task<HttpResponseMessage> SubmitContactAsync(HttpClient client, int number)
    {
        var token = await client.GetTokenAsync("/iletisim");
        return await client.PostFormAsync("/iletisim", token, ("Name", "Ayşe Yılmaz"), ("ContactInfo", $"sinir{number}@example.com"), ("Service", "PVC Doğrama"), ("ProjectLocation", "Bornova"), ("Message", "Projemiz için bilgi almak istiyoruz."), ("PrivacyAccepted", "true"));
    }

    [Fact]
    public async Task ForwardedProto_MakesLoginRedirectUseHttps()
    {
        await using var app = postgres.CreateApp("proxy_scheme");
        var response = await ClientFrom(app, "203.0.113.1").GetAsync("/yonetim");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ContactForm_LimitsEachIpSeparately()
    {
        await using var app = postgres.CreateApp("limit_contact");
        var client = ClientFrom(app, "203.0.113.2");
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Redirect, (await SubmitContactAsync(client, i)).StatusCode);

        var rejected = await SubmitContactAsync(client, 10);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("Çok fazla deneme", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.Redirect, (await SubmitContactAsync(ClientFrom(app, "203.0.113.3"), 11)).StatusCode);
    }

    [Fact]
    public async Task Login_LimitsAttemptsAcrossAccounts()
    {
        await using var app = postgres.CreateApp("limit_login");
        var client = ClientFrom(app, "203.0.113.4");
        var token = await client.GetTokenAsync("/yonetim-giris");
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync("/yonetim-giris", token, ("Email", $"yok{i}@example.com"), ("Password", "yanlis"))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostFormAsync("/yonetim-giris", token, ("Email", AppFactory.AdminEmail), ("Password", AppFactory.Password))).StatusCode);
    }
}
