using System.Net.Http.Json;
using System.Security.Cryptography;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Seo;

// Yeni ve değişen sayfaları IndexNow ile bildirir (Bing, Yandex ve diğer katılımcılar). Google IndexNow kullanmaz, site haritasını okur.
public sealed class IndexNowPublisher
{
    public const string KeySetting = "IndexNowKey";
    public const string MarkerSetting = "IndexNowSubmittedUtc";
    public const string ClientName = "IndexNow";
    private const string Endpoint = "https://api.indexnow.org/indexnow";

    private readonly AppDbContext _db;
    private readonly SiteContentIndex _index;
    private readonly SiteSettingsProvider _settings;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<IndexNowPublisher> _logger;
    public IndexNowPublisher(AppDbContext db, SiteContentIndex index, SiteSettingsProvider settings, IHttpClientFactory clients, ILogger<IndexNowPublisher> logger) { _db = db; _index = index; _settings = settings; _clients = clients; _logger = logger; }

    public static bool IsPublicAddress(string baseUrl) => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && !uri.IsLoopback
        && !uri.Host.Contains("DOMAININIZ", StringComparison.OrdinalIgnoreCase) && !uri.Host.Contains("ornek", StringComparison.OrdinalIgnoreCase) && uri.Host.Contains('.');

    public async Task<string> KeyAsync()
    {
        var record = await _db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == KeySetting);
        if (record is not null) return record.Value;
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _db.SiteSettingRecords.Add(new SiteSettingRecord { Key = KeySetting, Value = key, Description = "IndexNow anahtarı" });
        await _db.SaveChangesAsync();
        return key;
    }

    // Bildirim başarılı olmadan işaret ilerlemez; ağ hatasında aynı sayfalar sonraki turda yeniden gönderilir.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var site = await _settings.GetAsync(cancellationToken);
        if (site.IndexNowEnabled != "true" || !IsPublicAddress(site.BaseUrl)) return 0;

        var now = DateTime.UtcNow;
        var marker = await _db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == MarkerSetting, cancellationToken);
        DateTime? since = marker is not null && DateTime.TryParse(marker.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
        var baseUrl = site.BaseUrl.TrimEnd('/');
        var urls = (await _index.PagesAsync()).Where(x => since is null || x.ModifiedUtc > since).Select(x => baseUrl + x.Path).Take(10000).ToList();
        if (urls.Count == 0) return 0;

        var key = await KeyAsync();
        var response = await _clients.CreateClient(ClientName).PostAsJsonAsync(Endpoint, new { host = new Uri(baseUrl).Host, key, keyLocation = $"{baseUrl}/indexnow-{key}.txt", urlList = urls }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("IndexNow bildirimi reddedildi: {Status}", (int)response.StatusCode);
            return 0;
        }

        if (marker is null) _db.SiteSettingRecords.Add(new SiteSettingRecord { Key = MarkerSetting, Value = now.ToString("O"), Description = "Son IndexNow bildirimi" });
        else marker.Value = now.ToString("O");
        await _db.SaveChangesAsync(cancellationToken);
        return urls.Count;
    }
}

public sealed class IndexNowBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IndexNowBackgroundService> _logger;
    public IndexNowBackgroundService(IServiceScopeFactory scopes, ILogger<IndexNowBackgroundService> logger) { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<IndexNowPublisher>().RunOnceAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("IndexNow: {Count} adres bildirildi.", count);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _logger.LogWarning(error, "IndexNow bildirimi yapılamadı; sonraki turda denenecek.");
            }
        }
    }
}
