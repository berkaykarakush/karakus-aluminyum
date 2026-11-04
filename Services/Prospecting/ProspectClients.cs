using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services.Prospecting;

public sealed class ProspectingOptions
{
    public const string SectionName = "Prospecting";
    public string? GooglePlacesApiKey { get; set; }
    public bool WorkerEnabled { get; set; } = true;
    // Ücretli istek sınırı. Google'ın aylık ücretsiz kotasının altında tutulur; dolunca tarama durur.
    public int MonthlyRequestLimit { get; set; } = 900;
    // Bir arama en çok kaç sayfa (sayfa başına 20 firma) okunsun. Varsayılan sektörlerin tam turu 1 sayfayla
    // yaklaşık 160, 2 sayfayla 320 istek eder.
    public int MaxPagesPerQuery { get; set; } = 1;
    public string City { get; set; } = "İzmir";
    public bool HasApiKey => !string.IsNullOrWhiteSpace(GooglePlacesApiKey);
}

public sealed record PlaceResult(string Id, string Name, string? Address, string? District, string? City, string? Phone, string? Website, string? MapsUrl, double? Rating, int? RatingCount, bool Closed);
public sealed record PlacePage(IReadOnlyList<PlaceResult> Places, string? NextPageToken);

public interface IPlacesClient
{
    Task<PlacePage> SearchAsync(string query, string? pageToken, CancellationToken cancellationToken);
}

public sealed class PlacesApiException(string message) : Exception(message);

// Places API (New) Text Search. Alan maskesi yalnızca kullanılan alanları ister; ücret kademesi buna göre belirlenir.
public sealed class GooglePlacesClient : IPlacesClient
{
    public const string ClientName = "google-places";
    private const string Endpoint = "https://places.googleapis.com/v1/places:searchText";
    private const string FieldMask = "places.id,places.displayName,places.formattedAddress,places.addressComponents,places.nationalPhoneNumber,places.websiteUri,places.googleMapsUri,places.rating,places.userRatingCount,places.businessStatus,nextPageToken";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly ProspectingOptions _options;
    public GooglePlacesClient(IHttpClientFactory http, IOptions<ProspectingOptions> options) { _http = http; _options = options.Value; }

    public async Task<PlacePage> SearchAsync(string query, string? pageToken, CancellationToken cancellationToken)
    {
        if (!_options.HasApiKey) throw new PlacesApiException("Google Places API anahtarı tanımlı değil.");
        // İzmir ilini kapsayan dikdörtgen: sonuçlar il dışına taşmaz.
        var body = new Dictionary<string, object?>
        {
            ["textQuery"] = query, ["languageCode"] = "tr", ["regionCode"] = "TR", ["pageSize"] = 20,
            ["locationRestriction"] = new { rectangle = new { low = new { latitude = 37.80, longitude = 26.20 }, high = new { latitude = 39.40, longitude = 28.50 } } }
        };
        if (pageToken is not null) body["pageToken"] = pageToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Goog-Api-Key", _options.GooglePlacesApiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);
        using var response = await _http.CreateClient(ClientName).SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try { message = JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("message").GetString(); } catch (Exception) { }
            throw new PlacesApiException($"Google Places API {(int)response.StatusCode}: {message ?? response.ReasonPhrase}");
        }
        return Parse(text);
    }

    public static PlacePage Parse(string json)
    {
        var data = JsonSerializer.Deserialize<SearchResponse>(json, Json) ?? new SearchResponse();
        var places = (data.Places ?? new()).Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.DisplayName?.Text)).Select(p =>
        {
            string? Component(string type) => p.AddressComponents?.FirstOrDefault(c => c.Types?.Contains(type) == true)?.LongText;
            return new PlaceResult(p.Id!, p.DisplayName!.Text!.Trim(), p.FormattedAddress, Component("administrative_area_level_2"), Component("administrative_area_level_1"),
                p.NationalPhoneNumber, p.WebsiteUri, p.GoogleMapsUri, p.Rating, p.UserRatingCount, p.BusinessStatus is "CLOSED_PERMANENTLY");
        }).ToList();
        return new PlacePage(places, string.IsNullOrWhiteSpace(data.NextPageToken) ? null : data.NextPageToken);
    }

    private sealed class SearchResponse { public List<ApiPlace>? Places { get; set; } public string? NextPageToken { get; set; } }
    private sealed class ApiPlace
    {
        public string? Id { get; set; }
        public LocalizedText? DisplayName { get; set; }
        public string? FormattedAddress { get; set; }
        public List<AddressComponent>? AddressComponents { get; set; }
        public string? NationalPhoneNumber { get; set; }
        public string? WebsiteUri { get; set; }
        public string? GoogleMapsUri { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingCount { get; set; }
        public string? BusinessStatus { get; set; }
    }
    private sealed class LocalizedText { public string? Text { get; set; } }
    private sealed class AddressComponent { public string? LongText { get; set; } public List<string>? Types { get; set; } }
}

public interface IWebsiteReader
{
    // Firmanın kendi sitesinden e-posta ve sosyal medya bağlantıları.
    Task<SiteContacts> ReadAsync(string website, CancellationToken cancellationToken);
}

// Firma sitesini okur: robots.txt'e uyar, en çok 3 sayfa, sayfa başına 1,5 MB, yalnızca internetteki adresler.
public sealed class WebsiteReader : IWebsiteReader
{
    public const string ClientName = "prospect-website";
    public const string UserAgent = "KarakusAluminyumBot/1.0 (+https://www.karakusaluminyum.com)";
    private const int MaxBytes = 1_500_000;
    private readonly IHttpClientFactory _http;
    public WebsiteReader(IHttpClientFactory http) => _http = http;

    // Bağlantı kurulan her IP denetlenir: DNS sonradan iç ağa çevrilse (rebinding) veya yönlendirme olsa bile.
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 4,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var target = addresses.FirstOrDefault(ProspectText.IsPublicAddress) ?? throw new HttpRequestException("İç ağ adresine istek engellendi.");
            if (addresses.Any(a => !ProspectText.IsPublicAddress(a))) throw new HttpRequestException("İç ağ adresine istek engellendi.");
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };

    public async Task<SiteContacts> ReadAsync(string website, CancellationToken cancellationToken)
    {
        var url = ProspectText.NormalizeWebsite(website) ?? throw new InvalidOperationException("Web sitesi adresi geçersiz.");
        var home = new Uri(url);
        if (!home.IsDefaultPort) throw new InvalidOperationException("Yalnızca standart web portları okunur.");
        var client = _http.CreateClient(ClientName);
        var robots = await GetAsync(client, new Uri(home, "/robots.txt"), cancellationToken, quiet: true);
        if (!ProspectText.RobotsAllows(robots, home.AbsolutePath)) throw new InvalidOperationException("Site robots.txt ile taramaya kapalı.");

        var html = await GetAsync(client, home, cancellationToken) ?? throw new InvalidOperationException("Site açılamadı.");
        var pages = new StringBuilder(html);
        foreach (var page in ProspectText.ContactPages(html, home))
            if (ProspectText.RobotsAllows(robots, page.AbsolutePath) && await GetAsync(client, page, cancellationToken, quiet: true) is { } more)
                pages.Append('\n').Append(more);
        return ProspectText.ExtractContacts(pages.ToString(), url);
    }

    private static async Task<string?> GetAsync(HttpClient client, Uri uri, CancellationToken cancellationToken, bool quiet = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !type.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxBytes];
            var total = 0;
            int read;
            while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), cancellationToken)) > 0) total += read;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception) when (quiet && !cancellationToken.IsCancellationRequested) { return null; }
    }
}
