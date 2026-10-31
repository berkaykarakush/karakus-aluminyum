using System.Net;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class NotificationComposerTests
{
    private static readonly SiteSettings Site = new() { SiteName = "Karakuş Alüminyum", BaseUrl = "https://www.ornek.test", PhoneDisplay = "0232 000 00 00", Email = "info@ornek.test" };
    private static ContactMessage Message(string name = "Ayşe", string contact = "0555 111 22 33", string text = "Salon penceresi için teklif rica ediyorum.") => new() { Id = 7, Name = name, ContactInfo = contact, Service = "PVC Doğrama", ProjectLocation = "Bornova", Message = text };

    [Fact]
    public void AdminEmail_CannotBeUsedForHeaderInjection_AndEscapesHtml()
    {
        var mail = NotificationComposer.AdminEmail(Message("Ali\r\nBcc: kurban@example.com", text: "<script>alert(1)</script>"), "ekip@ornek.test", Site);
        Assert.DoesNotContain('\n', mail.Subject);
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.Contains("https://www.ornek.test/yonetim/mesajlar/7", mail.TextBody);
    }

    [Theory]
    [InlineData("musteri@example.com", "musteri@example.com")]
    [InlineData("0555 111 22 33", null)]
    public void AdminEmail_RepliesToCustomerWhenEmailIsKnown(string contact, string? replyTo)
        => Assert.Equal(replyTo, NotificationComposer.AdminEmail(Message(contact: contact), "ekip@ornek.test", Site).ReplyTo);

    // Adres doğrulanmamıştır: form, başkasına içerik göndermenin yolu olamaz.
    [Fact]
    public void CustomerEmail_CarriesNoFormText()
    {
        var mail = NotificationComposer.CustomerEmail("kurban@example.com", Site);
        Assert.Contains("Talebinizi aldık", mail.Subject);
        Assert.Contains("0232 000 00 00", mail.TextBody);
        Assert.Contains("siz oluşturmadıysanız", mail.TextBody);
        Assert.Equal("info@ornek.test", mail.ReplyTo);
    }

    [Fact]
    public void WhatsApp_UsesApprovedTemplate_WithCleanParameters()
    {
        var site = new SiteSettings { SiteName = "K", WhatsAppTemplateName = "yeni_teklif", WhatsAppTemplateLanguage = "tr" };
        var json = JsonSerializer.Serialize(WhatsAppPayload.Build(NotificationComposer.AdminWhatsApp(Message("Ali\n\n   Veli", text: "gizli"), "+90 555 000 00 00", site)));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("905550000000", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("template", doc.RootElement.GetProperty("type").GetString());
        var template = doc.RootElement.GetProperty("template");
        Assert.Equal("yeni_teklif", template.GetProperty("name").GetString());
        Assert.Equal("tr", template.GetProperty("language").GetProperty("code").GetString());
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(4, parameters.GetArrayLength());
        Assert.Equal("Ali Veli", parameters[0].GetProperty("text").GetString());
    }

    [Fact]
    public void WhatsApp_FallsBackToText_WithoutTemplate()
    {
        var json = JsonSerializer.Serialize(WhatsAppPayload.Build(NotificationComposer.AdminWhatsApp(Message(), "905550000000", Site)));
        Assert.Contains("\"type\":\"text\"", json);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData("  ", "-")]
    [InlineData("a\tb\n\nc    d", "a b c d")]
    public void WhatsApp_ParameterRules(string? value, string expected) => Assert.Equal(expected, WhatsAppPayload.Parameter(value));

    [Fact]
    public void WhatsApp_LongParameter_IsCut() => Assert.Equal(200, WhatsAppPayload.Parameter(new string('x', 500)).Length);

    [Theory]
    [InlineData("0555 111 22 33", "905551112233")]
    [InlineData("555 111 22 33", "905551112233")]
    [InlineData("+90 (555) 111-22-33", "905551112233")]
    [InlineData("0232 111 22 33", "902321112233")]
    [InlineData("musteri@example.com", null)]
    [InlineData("beni arayın", null)]
    [InlineData("123", null)]
    public void PhoneDigits_NormalizesTurkishNumbers(string value, string? expected) => Assert.Equal(expected, NotificationComposer.PhoneDigits(value));

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData(" a@b.co ", true)]
    [InlineData("a@b", false)]
    [InlineData("a@b.co,c@d.co", false)]
    [InlineData("0555", false)]
    public void IsEmail_Works(string value, bool expected) => Assert.Equal(expected, NotificationComposer.IsEmail(value));

    [Fact]
    public void Split_HandlesSeparatorsAndDuplicates() => Assert.Equal(new[] { "a@b.co", "c@d.co" }, NotificationComposer.Split("a@b.co, c@d.co;A@B.co\n"));

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Sent { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }

    private static WhatsAppCloudSender Sender(HttpMessageHandler handler, string? token = "gizli-anahtar")
        => new(Options.Create(new NotificationOptions { WhatsApp = new WhatsAppOptions { AccessToken = token, PhoneNumberId = "12345", GraphApiVersion = "v22.0" } }), new Clients(handler));

    private static readonly OutgoingWhatsApp Sample = new("905550000000", "yeni_teklif", "tr", new[] { "a" }, "t");

    [Fact]
    public async Task WhatsAppSender_CallsCloudApi_AndReturnsMessageId()
    {
        var handler = new Handler(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.1\"}]}");
        Assert.Equal("wamid.1", await Sender(handler).SendAsync(Sample, default));
        Assert.Equal("https://graph.facebook.com/v22.0/12345/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("gizli-anahtar", handler.Request.Headers.Authorization!.Parameter);
        Assert.Contains("yeni_teklif", handler.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task WhatsAppSender_SeparatesPermanentErrors(HttpStatusCode status, bool permanent)
    {
        var handler = new Handler(status, "{\"error\":{\"message\":\"Şablon bulunamadı\"}}");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Sender(handler).SendAsync(Sample, default));
        Assert.Equal(permanent, error is PermanentNotificationException);
        Assert.Contains("Şablon bulunamadı", error.Message);
        Assert.DoesNotContain("gizli-anahtar", error.Message);
    }

    [Fact]
    public async Task WhatsAppSender_WithoutSettings_SaysSo()
        => await Assert.ThrowsAsync<NotificationNotConfiguredException>(() => Sender(new Handler(HttpStatusCode.OK, "{}"), token: null).SendAsync(Sample, default));

    [Theory]
    [InlineData("smtp.test", "kullanici", "parola", "a@b.co", "auto", true)]
    [InlineData("smtp.test", "", "parola", "a@b.co", "auto", false)]
    [InlineData("smtp.test", "kullanici", "", "a@b.co", "auto", false)]
    [InlineData("smtp.test", "kullanici", "parola", "", "auto", false)]
    [InlineData("smtp.test", "", "", "a@b.co", "none", true)]
    public void SmtpSender_ReportsWhetherConfigured(string host, string user, string password, string from, string security, bool expected)
        => Assert.Equal(expected, new SmtpEmailSender(Options.Create(new NotificationOptions { Smtp = new SmtpOptions { Host = host, Username = user, Password = password, From = from, Security = security } })).IsConfigured);
}

// Gerçek SMTP konuşması: Türkçe konu, çok parçalı gövde ve yanıt adresi sunucuya doğru ulaşmalı.
public sealed class SmtpDeliveryTests
{
    [Fact]
    public async Task SmtpSender_DeliversRealMessage()
    {
        await using var server = new ContainerBuilder().WithImage("axllent/mailpit:v1.21").WithPortBinding(1025, true).WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info"))).Build();
        await server.StartAsync();

        var sender = new SmtpEmailSender(Options.Create(new NotificationOptions { Smtp = new SmtpOptions { Host = server.Hostname, Port = server.GetMappedPublicPort(1025), From = "bildirim@ornek.test", Security = "none" } }));
        var id = await sender.SendAsync(new OutgoingEmail("ekip@ornek.test", "Yeni teklif talebi: Şule Öztürk", "Düz metin", "<p>HTML gövde</p>", "musteri@example.com"), default);
        Assert.False(string.IsNullOrEmpty(id));

        using var http = new HttpClient { BaseAddress = new Uri($"http://{server.Hostname}:{server.GetMappedPublicPort(8025)}") };
        var list = JsonDocument.Parse(await http.GetStringAsync("/api/v1/messages")).RootElement.GetProperty("messages");
        Assert.Equal(1, list.GetArrayLength());
        var detail = JsonDocument.Parse(await http.GetStringAsync("/api/v1/message/" + list[0].GetProperty("ID").GetString())).RootElement;
        Assert.Equal("Yeni teklif talebi: Şule Öztürk", detail.GetProperty("Subject").GetString());
        Assert.Equal("Düz metin", detail.GetProperty("Text").GetString()!.Trim());
        Assert.Contains("HTML gövde", detail.GetProperty("HTML").GetString());
        Assert.Equal("musteri@example.com", detail.GetProperty("ReplyTo")[0].GetProperty("Address").GetString());
        Assert.Equal("ekip@ornek.test", detail.GetProperty("To")[0].GetProperty("Address").GetString());
    }
}

[Collection("postgres")]
public sealed class NotificationFlowTests(PostgresFixture postgres)
{
    private sealed class FakeEmail(Func<OutgoingEmail, Task<string?>>? behaviour = null) : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = new();
        public bool IsConfigured => true;
        public async Task<string?> SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
        {
            var id = behaviour is null ? "mail-id" : await behaviour(email);
            lock (Sent) Sent.Add(email);
            return id;
        }
    }

    private sealed class FakeWhatsApp(Exception? error = null) : IWhatsAppSender
    {
        public List<OutgoingWhatsApp> Sent { get; } = new();
        public bool IsConfigured => true;
        public Task<string?> SendAsync(OutgoingWhatsApp message, CancellationToken cancellationToken)
        {
            if (error is not null) throw error;
            lock (Sent) Sent.Add(message);
            return Task.FromResult<string?>("wamid");
        }
    }

    private static string Tag() => "n" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<int> RunAsync(AppFactory app, IEmailSender? email = null, IWhatsAppSender? whatsApp = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var s = scope.ServiceProvider;
        return await new NotificationDispatcher(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<SiteSettingsProvider>(), email ?? new FakeEmail(), whatsApp ?? new FakeWhatsApp(), NullLogger<NotificationDispatcher>.Instance).RunOnceAsync();
    }

    private static Task SetAsync(AppFactory app, params (string Key, string Value)[] values) => app.WithDbAsync(async db =>
    {
        foreach (var (key, value) in values) (await db.SiteSettingRecords.SingleAsync(x => x.Key == key)).Value = value;
        return await db.SaveChangesAsync();
    });

    private static async Task<HttpResponseMessage> SubmitAsync(AppFactory app, string contact, string? message = null)
    {
        var client = app.NewClient();
        var token = await client.GetTokenAsync("/iletisim");
        return await client.PostFormAsync("/iletisim", token, ("Name", "Ayşe Yılmaz"), ("ContactInfo", contact), ("Service", "PVC Doğrama"), ("ProjectLocation", "Bornova"), ("Message", message ?? "Pencere değişimi için teklif rica ediyorum " + Tag()), ("PrivacyAccepted", "true"));
    }

    private static Task<List<NotificationJob>> JobsAsync(AppFactory app) => app.WithDbAsync(db => db.NotificationJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync());

    private async Task<AppFactory> ConfiguredAsync(string database)
    {
        var app = postgres.CreateApp(database);
        app.NewClient();
        await SetAsync(app, ("NotifyEmailTo", "ekip@ornek.test, patron@ornek.test"), ("NotifyWhatsAppTo", "905550000000"));
        return app;
    }

    [Fact]
    public async Task Form_QueuesOneJobPerRecipient_WithoutSendingInTheRequest()
    {
        await using var app = await ConfiguredAsync("notify_queue");
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(app, "musteri@example.com")).StatusCode);

        var jobs = await JobsAsync(app);
        Assert.Equal(new[] { "ekip@ornek.test", "patron@ornek.test", "905550000000", "musteri@example.com" }, jobs.Select(x => x.Recipient));
        Assert.Equal(new[] { NotificationChannel.AdminEmail, NotificationChannel.AdminEmail, NotificationChannel.AdminWhatsApp, NotificationChannel.CustomerEmail }, jobs.Select(x => x.Channel));
        Assert.All(jobs, x => Assert.Equal(NotificationStatus.Pending, x.Status));
        Assert.Equal(1, await app.WithDbAsync(db => db.ContactMessages.CountAsync()));

        var email = new FakeEmail();
        var whatsApp = new FakeWhatsApp();
        Assert.Equal(4, await RunAsync(app, email, whatsApp));
        Assert.Equal(3, email.Sent.Count);
        Assert.Single(whatsApp.Sent);
        var done = await JobsAsync(app);
        Assert.All(done, x => Assert.Equal(NotificationStatus.Sent, x.Status));
        Assert.All(done, x => Assert.NotNull(x.SentUtc));
        Assert.Equal("wamid", done.Single(x => x.Channel == NotificationChannel.AdminWhatsApp).ProviderMessageId);

        Assert.Equal(0, await RunAsync(app, email, whatsApp)); // gönderilen tekrar gönderilmez
        Assert.Equal(3, email.Sent.Count);
    }

    [Fact]
    public async Task PhoneOnlyCustomer_GetsNoConfirmation_AndDisabledChannelsAreNotQueued()
    {
        await using var app = await ConfiguredAsync("notify_channels");
        await SubmitAsync(app, "0555 111 22 33");
        Assert.DoesNotContain(await JobsAsync(app), x => x.Channel == NotificationChannel.CustomerEmail);

        await SetAsync(app, ("NotifyEmailEnabled", "false"), ("NotifyWhatsAppEnabled", "false"), ("NotifyCustomerEmailEnabled", "false"));
        var before = (await JobsAsync(app)).Count;
        await SubmitAsync(app, "baska@example.com");
        Assert.Equal(before, (await JobsAsync(app)).Count);
        Assert.Equal(2, await app.WithDbAsync(db => db.ContactMessages.CountAsync())); // talep yine kaydedilir
    }

    [Fact]
    public async Task SameRequestTwice_IsSavedAndNotifiedOnce()
    {
        await using var app = await ConfiguredAsync("notify_double");
        const string text = "Çift tıklama ile iki kez gönderilen mesaj";
        await SubmitAsync(app, "cift@example.com", text);
        var jobs = (await JobsAsync(app)).Count;
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(app, "cift@example.com", text)).StatusCode);
        Assert.Equal(1, await app.WithDbAsync(db => db.ContactMessages.CountAsync()));
        Assert.Equal(jobs, (await JobsAsync(app)).Count);
    }

    [Fact]
    public async Task Confirmation_GoesToAnAddressOncePerDay_AndCarriesNoFormText()
    {
        await using var app = await ConfiguredAsync("notify_customer");
        var secret = "Reklam metni " + Tag();
        await SubmitAsync(app, "Kurban@Example.com", secret);
        await SubmitAsync(app, "kurban@example.com");

        var customer = (await JobsAsync(app)).Where(x => x.Channel == NotificationChannel.CustomerEmail).ToList();
        Assert.Equal(new[] { NotificationStatus.Pending, NotificationStatus.Skipped }, customer.Select(x => x.Status));
        var email = new FakeEmail();
        await RunAsync(app, email);
        var sent = Assert.Single(email.Sent, x => x.To.Equals("Kurban@Example.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(secret, sent.TextBody + sent.HtmlBody);
        Assert.DoesNotContain("Ayşe", sent.TextBody + sent.HtmlBody + sent.Subject);
    }

    [Fact]
    public async Task HourlyLimit_ProtectsQuota_ButKeepsTheRequest()
    {
        await using var app = await ConfiguredAsync("notify_limit");
        await SubmitAsync(app, "ilk@example.com");
        var messageId = await app.WithDbAsync(db => db.ContactMessages.Select(x => x.Id).FirstAsync());
        await app.WithDbAsync(async db =>
        {
            for (var i = 0; i < NotificationQueue.HourlyLimit; i++) db.NotificationJobs.Add(new NotificationJob { ContactMessageId = messageId, Recipient = "x@y.co", Status = NotificationStatus.Sent });
            return await db.SaveChangesAsync();
        });
        var before = (await JobsAsync(app)).Count;
        await SubmitAsync(app, "son@example.com");
        Assert.Equal(2, await app.WithDbAsync(db => db.ContactMessages.CountAsync()));
        var fresh = (await JobsAsync(app)).Skip(before).ToList();
        Assert.NotEmpty(fresh);
        Assert.All(fresh, x => Assert.Equal(NotificationStatus.Skipped, x.Status));
    }

    [Fact]
    public async Task TemporaryFailure_IsRetriedWithBackoff_ThenFailsAfterMaxAttempts()
    {
        await using var app = await ConfiguredAsync("notify_retry");
        await SetAsync(app, ("NotifyEmailEnabled", "false"), ("NotifyCustomerEmailEnabled", "false"));
        await SubmitAsync(app, "0555 111 22 33");
        var broken = new FakeWhatsApp(new HttpRequestException("Sunucuya ulaşılamadı"));

        for (var attempt = 1; attempt <= NotificationDispatcher.MaxAttempts; attempt++)
        {
            Assert.Equal(0, await RunAsync(app, whatsApp: broken));
            var job = Assert.Single(await JobsAsync(app));
            Assert.Equal(attempt, job.Attempts);
            Assert.Contains("ulaşılamadı", job.LastError);
            if (attempt < NotificationDispatcher.MaxAttempts)
            {
                Assert.Equal(NotificationStatus.Pending, job.Status);
                Assert.True(job.NextAttemptUtc > DateTime.UtcNow.AddSeconds(30)); // hemen tekrar denenmez
                Assert.Equal(0, await RunAsync(app, whatsApp: broken));
                Assert.Equal(attempt, Assert.Single(await JobsAsync(app)).Attempts);
                await app.WithDbAsync(db => db.NotificationJobs.ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptUtc, DateTime.UtcNow.AddSeconds(-1))));
            }
        }
        Assert.Equal(NotificationStatus.Failed, Assert.Single(await JobsAsync(app)).Status);
    }

    [Fact]
    public async Task PermanentFailure_StopsAtOnce_AndOneFailureDoesNotBlockOthers()
    {
        await using var app = await ConfiguredAsync("notify_permanent");
        await SubmitAsync(app, "0555 111 22 33");
        var email = new FakeEmail();
        Assert.Equal(2, await RunAsync(app, email, new FakeWhatsApp(new PermanentNotificationException("Numara geçersiz"))));
        var jobs = await JobsAsync(app);
        var whatsApp = jobs.Single(x => x.Channel == NotificationChannel.AdminWhatsApp);
        Assert.Equal((NotificationStatus.Failed, 1, "Numara geçersiz"), (whatsApp.Status, whatsApp.Attempts, whatsApp.LastError));
        Assert.Equal(2, jobs.Count(x => x.Status == NotificationStatus.Sent));
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task ConcurrentWorkers_DoNotSendTwice_AndCrashedJobsResume()
    {
        await using var app = await ConfiguredAsync("notify_concurrent");
        await SetAsync(app, ("NotifyWhatsAppEnabled", "false"), ("NotifyCustomerEmailEnabled", "false"));
        for (var i = 0; i < 6; i++) await SubmitAsync(app, $"m{i}@example.com");
        var email = new FakeEmail(async _ => { await Task.Delay(5); return "id"; });
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync(app, email)));
        Assert.Equal(12, email.Sent.Count);
        Assert.Equal(12, email.Sent.Select(x => x.To + x.TextBody).Distinct().Count());

        // Gönderim sırasında kapanmış uygulama: eski kilit bırakılır, yeni kilit beklenir.
        var id = (await JobsAsync(app))[0].Id;
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, NotificationStatus.Processing).SetProperty(j => j.ClaimedUtc, DateTime.UtcNow)));
        Assert.Equal(0, await RunAsync(app, email));
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(j => j.ClaimedUtc, DateTime.UtcNow.AddMinutes(-6))));
        Assert.Equal(1, await RunAsync(app, email));
    }

    [Fact]
    public async Task OldFinishedJobs_ArePurged_AndDeletingARequestRemovesItsJobs()
    {
        await using var app = await ConfiguredAsync("notify_purge");
        await SubmitAsync(app, "a@example.com");
        await RunAsync(app);
        var count = (await JobsAsync(app)).Count;
        var first = (await JobsAsync(app))[0].Id;
        await app.WithDbAsync(db => db.NotificationJobs.Where(x => x.Id == first).ExecuteUpdateAsync(x => x.SetProperty(j => j.CreatedUtc, DateTime.UtcNow.AddDays(-91))));
        await RunAsync(app);
        Assert.Equal(count - 1, (await JobsAsync(app)).Count);

        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        var id = await app.WithDbAsync(db => db.ContactMessages.Select(x => x.Id).FirstAsync());
        await admin.PostFormAsync($"/yonetim/mesajlar/sil/{id}", await admin.GetTokenAsync($"/yonetim/mesajlar/{id}"));
        Assert.Empty(await JobsAsync(app));
    }

    [Fact]
    public async Task Panel_ShowsStatus_RetriesFailures_AndHidesSecrets()
    {
        await using var app = await ConfiguredAsync("notify_panel");
        await SubmitAsync(app, "0555 111 22 33");
        await RunAsync(app, whatsApp: new FakeWhatsApp(new PermanentNotificationException("Şablon bulunamadı")));

        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim/bildirimler"));
        Assert.Contains("Şablon bulunamadı", html);
        Assert.Contains("Yeniden dene", html);
        Assert.Contains("{{1}}", html); // şablon kurulum metni
        Assert.Contains("Gönderilemeyen bildirim", WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim")));

        var failed = (await JobsAsync(app)).Single(x => x.Status == NotificationStatus.Failed);
        var token = await admin.GetTokenAsync("/yonetim/bildirimler");
        await admin.PostFormAsync($"/yonetim/bildirimler/yeniden/{failed.Id}", token);
        var requeued = (await JobsAsync(app)).Single(x => x.Id == failed.Id);
        Assert.Equal((NotificationStatus.Pending, 0, null), (requeued.Status, requeued.Attempts, requeued.LastError));
        var whatsApp = new FakeWhatsApp();
        Assert.Equal(1, await RunAsync(app, whatsApp: whatsApp));

        var sent = (await JobsAsync(app)).First(x => x.Status == NotificationStatus.Sent);
        await admin.PostFormAsync($"/yonetim/bildirimler/yeniden/{sent.Id}", token); // gönderilen tekrar sıraya girmez
        Assert.Equal(NotificationStatus.Sent, (await JobsAsync(app)).Single(x => x.Id == sent.Id).Status);

        var message = await app.WithDbAsync(db => db.ContactMessages.Select(x => x.Id).FirstAsync());
        var detail = WebUtility.HtmlDecode(await admin.GetStringAsync($"/yonetim/mesajlar/{message}"));
        Assert.Contains("https://wa.me/905551112233", detail);
        Assert.Contains("tel:+905551112233", detail);
        Assert.Contains("Ekip WhatsApp", detail);
    }

    [Fact]
    public async Task TestSend_ReportsResultInsteadOfCrashing_AndValidatesInput()
    {
        await using var app = postgres.CreateApp("notify_test");
        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        var token = await admin.GetTokenAsync("/yonetim/bildirimler");
        Assert.Contains("Ayar eksik", await admin.GetStringAsync("/yonetim/bildirimler"));

        foreach (var fields in new[] { new[] { ("kanal", "email"), ("alici", "gecersiz") }, new[] { ("kanal", "email"), ("alici", "a@b.co") }, new[] { ("kanal", "whatsapp"), ("alici", "905550000000") }, new[] { ("kanal", "email"), ("alici", "") } })
        {
            var response = await admin.PostFormAsync("/yonetim/bildirimler/test", token, fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        Assert.Contains("Deneme başarısız", WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim/bildirimler")));
    }

    [Theory]
    [InlineData("Editor")]
    [InlineData("Accountant")]
    public async Task NotificationPage_IsAdminOnly(string role)
    {
        var app = postgres.Shared;
        var email = Tag() + "@test.local";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var client = app.NewClient();
        await client.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/yonetim/bildirimler")).StatusCode);
    }

    [Fact]
    public async Task Settings_RejectBadRecipients_AndEnvironmentAddressIsFallback()
    {
        await using var app = postgres.CreateApp("notify_settings");
        var admin = app.NewClient();
        await admin.LoginAsync(AppFactory.AdminEmail);
        var token = await admin.GetTokenAsync("/yonetim/ayarlar");
        foreach (var (key, value) in new[] { ("NotifyEmailTo", "gecersiz adres"), ("NotifyWhatsAppTo", "abc"), ("WhatsAppTemplateName", "Büyük Harf") })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostFormAsync("/yonetim/ayarlar/kaydet", token, ($"values[{key}]", value))).StatusCode);
            Assert.Equal("", await app.WithDbAsync(db => db.SiteSettingRecords.Where(x => x.Key == key).Select(x => x.Value).SingleAsync()));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync("/yonetim/ayarlar/kaydet", token, ("values[NotifyEmailTo]", "a@b.co, c@d.co"), ("values[NotifyWhatsAppTo]", "905550000000,+905551111111"), ("values[WhatsAppTemplateName]", "yeni_teklif"))).StatusCode);
    }

    [Fact]
    public async Task ExistingDatabase_GetsTheQueueTable()
    {
        await using (var first = postgres.CreateApp("notify_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"NotificationJobs\""));
        }
        await using var second = postgres.CreateApp("notify_upgrade", schemaMode: "existing");
        second.NewClient();
        await SetAsync(second, ("NotifyEmailTo", "ekip@ornek.test"));
        await SubmitAsync(second, "0555 111 22 33");
        Assert.Contains(await JobsAsync(second), x => x.Recipient == "ekip@ornek.test");
    }
}
