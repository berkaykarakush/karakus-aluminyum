using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Analytics;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KarakusAluminyum.Web.Controllers;

// Çerez onayı veren ziyaretçilerin anonim hareketlerini kaydeder. IP adresi saklanmaz.
[ApiController, Route("t")]
public sealed partial class AnalyticsController : ControllerBase
{
    public const string RateLimitPolicy = "analytics";
    private readonly AppDbContext _db;
    private readonly SiteSettingsProvider _settings;
    public AnalyticsController(AppDbContext db, SiteSettingsProvider settings) { _db = db; _settings = settings; }

    [GeneratedRegex("bot|crawl|spider|slurp|headless|lighthouse|facebookexternalhit|preview", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{8,40}$")]
    private static partial Regex SessionRegex();

    [HttpPost, EnableRateLimiting(RateLimitPolicy), RequestSizeLimit(4096)]
    public async Task<IActionResult> Post([FromBody] AnalyticsInput input)
    {
        // Geçersiz istekler sessizce yok sayılır: ziyaretçiye ve saldırgana bilgi verilmez.
        var userAgent = Request.Headers.UserAgent.ToString();
        if (!AnalyticsEventTypes.All.Contains(input.Type) || !SessionRegex().IsMatch(input.SessionId ?? "")) return NoContent();
        if (userAgent.Length == 0 || BotRegex().IsMatch(userAgent) || !IsSameSite()) return NoContent();

        var path = CleanPath(input.Path);
        if (path is null) return NoContent();
        if ((await _settings.GetAsync()).FirstPartyAnalyticsEnabled != "true") return NoContent();

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Type = input.Type!, Path = path, SessionId = input.SessionId!, Device = DeviceOf(userAgent), ReferrerHost = ReferrerHost(input.Referrer),
            UtmSource = Clip(input.UtmSource), UtmMedium = Clip(input.UtmMedium), UtmCampaign = Clip(input.UtmCampaign)
        });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private bool IsSameSite()
    {
        var origin = Request.Headers.Origin.ToString();
        return origin.Length == 0 || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Authority.Equals(Request.Host.Value, StringComparison.OrdinalIgnoreCase));
    }

    public static string? CleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.StartsWith("//")) return null;
        var clean = path.Split('?', '#')[0];
        if (clean.StartsWith("/yonetim", StringComparison.OrdinalIgnoreCase)) return null;
        return clean.Length > 300 ? clean[..300] : clean;
    }

    public static string DeviceOf(string userAgent)
    {
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)) return "tablet";
        return userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) ? "mobile" : "desktop";
    }

    private string? ReferrerHost(string? referrer)
    {
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        var own = Request.Host.Host.ToLowerInvariant();
        if (own.StartsWith("www.")) own = own[4..];
        if (host == own) return null;
        return host.Length > 120 ? host[..120] : host;
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        return clean.Length > 80 ? clean[..80] : clean;
    }
}
