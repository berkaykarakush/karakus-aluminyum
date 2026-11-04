using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services.Prospecting;

public sealed class ProspectSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public void Wake() { if (_signal.CurrentCount == 0) { try { _signal.Release(); } catch (SemaphoreFullException) { } } }
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

// Sektör taraması: anahtar kelime × açık ilçe için Google'da arar, firmaları havuza ekler veya günceller.
public sealed class ProspectScanner
{
    private readonly AppDbContext _db;
    private readonly IPlacesClient _places;
    private readonly IWebsiteReader _websites;
    private readonly ProspectingOptions _options;
    private readonly ILogger<ProspectScanner> _logger;

    public ProspectScanner(AppDbContext db, IPlacesClient places, IWebsiteReader websites, IOptions<ProspectingOptions> options, ILogger<ProspectScanner> logger)
    { _db = db; _places = places; _websites = websites; _options = options.Value; _logger = logger; }

    public static DateTime MonthStartUtc(DateTime now) => new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task<int> MonthlyUsageAsync(CancellationToken cancellationToken = default)
    {
        var start = MonthStartUtc(DateTime.UtcNow);
        return _db.ProspectScans.Where(x => x.CreatedUtc >= start).SumAsync(x => x.RequestCount, cancellationToken);
    }

    // Tahmini en çok istek: bir sektör taramasının bütçeden ne kadar harcayabileceği.
    public int Estimate(ProspectSector sector, int districtCount) => ProspectText.ParseKeywords(sector.KeywordsJson).Count * districtCount * Math.Max(1, _options.MaxPagesPerQuery);

    // Tarama yalnızca panelden elle başlatılır; otomatik yenileme yoktur, Google kotası kullanıcının kontrolündedir.
    public async Task<ProspectScan> EnqueueAsync(int sectorId, string? requestedBy)
    {
        var scan = new ProspectScan { SectorId = sectorId, RequestedBy = requestedBy };
        _db.ProspectScans.Add(scan);
        await _db.SaveChangesAsync();
        return scan;
    }

    // Uygulama kapanırken yarım kalan tarama sonsuza dek "Çalışıyor" görünmesin.
    public async Task RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        await _db.ProspectScans.Where(x => x.Status == ProspectScanStatus.Running).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, ProspectScanStatus.Failed)
            .SetProperty(x => x.Message, "Uygulama yeniden başladığı için yarıda kaldı; bulunan firmalar kaydedildi.")
            .SetProperty(x => x.FinishedUtc, DateTime.UtcNow), cancellationToken);
    }

    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var scan = await _db.ProspectScans.Include(x => x.Sector).Where(x => x.Status == ProspectScanStatus.Queued).OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (scan is null) return false;
        await RunAsync(scan, cancellationToken);
        return true;
    }

    public async Task RunAsync(ProspectScan scan, CancellationToken cancellationToken)
    {
        scan.Status = ProspectScanStatus.Running;
        scan.StartedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            if (scan.Sector is null) throw new PlacesApiException("Sektör silinmiş.");
            if (!_options.HasApiKey) throw new PlacesApiException("Google Places API anahtarı tanımlı değil. Kurulum adımları tarama sayfasında.");
            var keywords = ProspectText.ParseKeywords(scan.Sector.KeywordsJson);
            var districts = await _db.ProspectDistricts.Where(x => x.IsActive).OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync(cancellationToken);
            if (keywords.Count == 0 || districts.Count == 0) throw new PlacesApiException("Sektörde anahtar kelime veya açık ilçe yok.");

            var used = await MonthlyUsageAsync(cancellationToken);
            var seen = new HashSet<string>();
            var stopped = false;
            foreach (var keyword in keywords)
            foreach (var district in districts)
            {
                string? token = null;
                for (var page = 0; page < Math.Max(1, _options.MaxPagesPerQuery); page++)
                {
                    if (used >= _options.MonthlyRequestLimit) { stopped = true; break; }
                    var result = await _places.SearchAsync(ProspectText.Query(keyword, district, _options.City), token, cancellationToken);
                    used++; scan.RequestCount++;
                    foreach (var place in result.Places.Where(p => seen.Add(p.Id)))
                    {
                        scan.FoundCount++;
                        if (await UpsertAsync(place, scan.Sector, district, cancellationToken)) scan.NewCount++;
                    }
                    // Her sayfadan sonra kaydedilir: hata olursa harcanan istek ve bulunan firmalar kaybolmaz.
                    await _db.SaveChangesAsync(cancellationToken);
                    token = result.NextPageToken;
                    if (token is null) break;
                }
                if (stopped) break;
            }

            scan.Sector.LastScanUtc = DateTime.UtcNow;
            scan.Status = ProspectScanStatus.Done;
            scan.Message = stopped ? $"Aylık istek sınırına ({_options.MonthlyRequestLimit}) ulaşıldı; tarama yarıda bırakıldı." : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Firma taraması {ScanId} başarısız.", scan.Id);
            _db.ChangeTracker.Entries<Prospect>().Where(e => e.State == EntityState.Added).ToList().ForEach(e => e.State = EntityState.Detached);
            scan.Status = ProspectScanStatus.Failed;
            scan.Message = ProspectText.Truncate(error is PlacesApiException or HttpRequestException ? error.Message : "Beklenmeyen hata: " + error.Message, 1000);
        }
        scan.FinishedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // Aynı firma Google kimliğiyle, yoksa telefon veya web sitesiyle eşleşir. Elle düzeltilmiş alanların üzerine yazılmaz.
    private async Task<bool> UpsertAsync(PlaceResult place, ProspectSector sector, string district, CancellationToken cancellationToken)
    {
        if (place.City is not null && !place.City.Contains(_options.City, StringComparison.OrdinalIgnoreCase)) return false;
        var website = ProspectText.NormalizeWebsite(place.Website);
        var existing = await _db.Prospects.FirstOrDefaultAsync(x => x.GooglePlaceId == place.Id, cancellationToken)
            ?? _db.Prospects.Local.FirstOrDefault(x => x.GooglePlaceId == place.Id)
            ?? await FindByContactAsync(place.Phone, website, cancellationToken);
        if (existing is not null)
        {
            existing.GooglePlaceId ??= place.Id;
            existing.GoogleMapsUrl ??= ProspectText.Truncate(place.MapsUrl, 500);
            existing.Phone ??= ProspectText.Truncate(place.Phone, 40);
            if (existing.Website is null && website is not null) { existing.Website = website; existing.EnrichedUtc = null; }
            existing.Address ??= ProspectText.Truncate(place.Address, 500);
            existing.District ??= ProspectText.Truncate(place.District ?? district, 60);
            existing.SectorId ??= sector.Id;
            existing.Rating = place.Rating; existing.RatingCount = place.RatingCount;
            existing.LastSeenUtc = DateTime.UtcNow;
            // Kapanmış firma havuzda kalır ama elenir; elle ilerletilmiş kayda dokunulmaz.
            if (place.Closed && existing.Stage == ProspectStage.New) existing.Stage = ProspectStage.Unqualified;
            return false;
        }
        if (place.Closed) return false;
        _db.Prospects.Add(new Prospect
        {
            Name = ProspectText.Truncate(place.Name, 200), SectorId = sector.Id, Source = ProspectSources.Google, GooglePlaceId = ProspectText.Truncate(place.Id, 300),
            GoogleMapsUrl = ProspectText.Truncate(place.MapsUrl, 500), Phone = place.Phone is null ? null : ProspectText.Truncate(place.Phone, 40), Website = website,
            Address = place.Address is null ? null : ProspectText.Truncate(place.Address, 500), District = ProspectText.Truncate(place.District ?? district, 60),
            Rating = place.Rating, RatingCount = place.RatingCount, LastSeenUtc = DateTime.UtcNow
        });
        return true;
    }

    private async Task<Prospect?> FindByContactAsync(string? phone, string? website, CancellationToken cancellationToken)
    {
        var phoneKey = ProspectText.PhoneKey(phone);
        var host = ProspectText.HostKey(website);
        if (phoneKey is null && host is null) return null;
        // Telefon ve site serbest biçimde yazılabildiği için eşleştirme bellekte yapılır; havuz binlerce kayıtla sınırlı.
        var candidates = await _db.Prospects.Where(x => x.GooglePlaceId == null && (x.Phone != null || x.Website != null)).ToListAsync(cancellationToken);
        return candidates.Concat(_db.Prospects.Local.Where(x => x.GooglePlaceId == null))
            .FirstOrDefault(x => (phoneKey is not null && ProspectText.PhoneKey(x.Phone) == phoneKey) || (host is not null && ProspectText.HostKey(x.Website) == host));
    }

    // Sitesi olup henüz okunmamış firmalar. Bulunan bilgi yalnızca boş alanları doldurur.
    public async Task<int> EnrichPendingAsync(int max, CancellationToken cancellationToken)
    {
        var pending = await _db.Prospects.Where(x => x.Website != null && x.EnrichedUtc == null && !x.DoNotContact).OrderBy(x => x.Id).Take(max).ToListAsync(cancellationToken);
        foreach (var prospect in pending) await EnrichAsync(prospect, cancellationToken);
        return pending.Count;
    }

    public async Task EnrichAsync(Prospect prospect, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var found = await _websites.ReadAsync(prospect.Website!, timeout.Token);
            prospect.Email ??= found.Emails.FirstOrDefault();
            prospect.Instagram ??= found.Instagram;
            prospect.Facebook ??= found.Facebook;
            prospect.LinkedIn ??= found.LinkedIn;
            prospect.TikTok ??= found.TikTok;
            prospect.EnrichError = found.Emails.Count == 0 && found.Instagram is null && found.Facebook is null && found.LinkedIn is null && found.TikTok is null ? "Sitede e-posta veya sosyal medya bağlantısı bulunamadı." : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or OperationCanceledException or IOException)
        {
            prospect.EnrichError = ProspectText.Truncate(error is OperationCanceledException ? "Site zamanında yanıt vermedi." : error.Message, 300);
        }
        prospect.EnrichedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ProspectWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly ProspectSignal _signal;
    private readonly ILogger<ProspectWorker> _logger;
    public ProspectWorker(IServiceScopeFactory scopes, ProspectSignal signal, ILogger<ProspectWorker> logger) { _scopes = scopes; _signal = signal; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ProspectScanner>().RecoverInterruptedAsync(stoppingToken);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested) { _logger.LogError(error, "Yarım kalan firma taramaları kapatılamadı."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<ProspectScanner>();
                var busy = await scanner.RunNextAsync(stoppingToken);
                busy |= await scanner.EnrichPendingAsync(10, stoppingToken) > 0;
                if (!busy) await _signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                _logger.LogError(error, "Firma havuzu turu başarısız; sonraki turda devam edilecek.");
                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}

public static class ProspectSeed
{
    // İzmir'in 30 ilçesi. Kentsel dönüşüm ve yeni konut projelerinin yoğun olduğu ilçeler açık başlar.
    public static readonly (string Name, bool Active)[] Districts =
    {
        ("Bornova", true), ("Buca", true), ("Bayraklı", true), ("Karşıyaka", true), ("Konak", true), ("Karabağlar", true), ("Çiğli", true), ("Gaziemir", true),
        ("Narlıdere", true), ("Balçova", true), ("Güzelbahçe", true), ("Menemen", true), ("Torbalı", true), ("Menderes", true), ("Urla", true), ("Çeşme", true),
        ("Seferihisar", true), ("Kemalpaşa", true), ("Aliağa", false), ("Bayındır", false), ("Bergama", false), ("Beydağ", false), ("Dikili", false), ("Foça", false),
        ("Karaburun", false), ("Kınık", false), ("Kiraz", false), ("Ödemiş", false), ("Selçuk", false), ("Tire", false)
    };

    // Google Haritalar'da firmaların kendini tanımladığı ifadeler. Panelden değiştirilebilir.
    public static readonly (string Name, string[] Keywords, bool Active)[] Sectors =
    {
        ("Müteahhit ve inşaat firmaları", new[] { "inşaat firması", "müteahhit" }, true),
        ("Mimarlık ofisleri", new[] { "mimarlık ofisi" }, true),
        ("İç mimarlık ve dekorasyon", new[] { "iç mimarlık" }, true),
        ("İnşaat mühendisliği ve statik proje", new[] { "statik proje bürosu" }, true),
        ("Elektrik mühendisliği ve taahhüt", new[] { "elektrik mühendislik" }, true),
        ("Mekanik tesisat mühendisliği", new[] { "mekanik tesisat" }, true),
        ("Zemin etüdü ve jeoteknik", new[] { "zemin etüdü" }, true),
        ("Yapı denetim firmaları", new[] { "yapı denetim" }, true),
        ("Harita ve kadastro mühendisliği", new[] { "harita mühendislik bürosu" }, false),
        ("Site ve tesis yönetimi", new[] { "site yönetimi" }, false),
        ("Kafe, restoran ve oteller", new[] { "restoran", "otel" }, false)
    };

    public static void AddTo(AppDbContext db)
    {
        for (var i = 0; i < Districts.Length; i++) db.ProspectDistricts.Add(new ProspectDistrict { Name = Districts[i].Name, IsActive = Districts[i].Active, SortOrder = i });
        for (var i = 0; i < Sectors.Length; i++)
            db.ProspectSectors.Add(new ProspectSector { Name = Sectors[i].Name, KeywordsJson = JsonSerializer.Serialize(Sectors[i].Keywords), IsActive = Sectors[i].Active, SortOrder = i });
    }
}
