using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KarakusAluminyum.Web.Services.Prospecting;

public sealed record SiteContacts(IReadOnlyList<string> Emails, string? Instagram, string? Facebook, string? LinkedIn, string? TikTok);

// Firma bilgilerini karşılaştırma ve web sayfasından iletişim bilgisi çıkarma. Ağ erişimi yoktur; test edilebilir.
public static partial class ProspectText
{
    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,24}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"href\s*=\s*[""']([^""'#\s]+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex(@"https?://(?:[a-z0-9-]+\.)*(?:instagram|facebook|fb|linkedin|tiktok)\.com/[^\s""'<>\\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex SocialUrlRegex();

    // Görsel adları (logo@2x.png) ve site altyapılarının kendi adresleri e-posta sayılmaz.
    private static readonly string[] IgnoredEmailEndings = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".css", ".js" };
    private static readonly string[] IgnoredEmailDomains = { "example.com", "ornek.com", "domain.com", "sentry.io", "wixpress.com", "sentry-next.wixpress.com", "godaddy.com", "yourdomain.com" };

    public static IReadOnlyList<string> ParseKeywords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json)?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList() ?? new(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    // Panelde her satıra bir anahtar kelime yazılır; tekrarlar ve boş satırlar atılır.
    public static List<string> KeywordsFromLines(string? text) => (text ?? "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string Query(string keyword, string district, string city) => $"{keyword.Trim()} {district.Trim()} {city.Trim()}";

    // Karşılaştırma için son 10 hane: "0232 123 45 67", "+90 232 123 4567" aynı numaradır.
    public static string? PhoneKey(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : null;
    }

    // WhatsApp yalnızca cep numarasında anlamlı (5 ile başlar).
    public static string? WhatsAppNumber(string? phone) => PhoneKey(phone) is { } key && key.StartsWith('5') ? "90" + key : null;

    public static string? NormalizeWebsite(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.')) return null;
        var url = uri.GetLeftPart(UriPartial.Path);
        return url.Length > 500 ? null : url;
    }

    public static string? HostKey(string? website)
    {
        var url = NormalizeWebsite(website);
        if (url is null) return null;
        var host = new Uri(url).Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static SiteContacts ExtractContacts(string html, string? siteUrl)
    {
        var decoded = WebUtility.HtmlDecode(html ?? "");
        var siteHost = HostKey(siteUrl);
        var emails = EmailRegex().Matches(decoded).Select(m => m.Value.Trim('.').ToLowerInvariant())
            .Where(e => !IgnoredEmailEndings.Any(e.EndsWith) && !IgnoredEmailDomains.Any(d => e.EndsWith("@" + d) || e.EndsWith("." + d)) && e.Length <= 180)
            .Distinct()
            // Sitenin kendi alan adındaki adres öne alınır: info@firma.com, gmail adresinden önce gelir.
            .OrderByDescending(e => siteHost is not null && e.EndsWith("@" + siteHost))
            .ToList();

        // Bağlantılar yalnızca href'ten değil, yapılandırılmış veriden (JSON-LD "sameAs") de toplanır.
        var candidates = new Dictionary<string, List<string>> { ["instagram"] = new(), ["facebook"] = new(), ["linkedin"] = new(), ["tiktok"] = new() };
        foreach (var raw in SocialUrlRegex().Matches(decoded.Replace("\\/", "/")).Select(m => m.Value))
        {
            if (!Uri.TryCreate(raw.TrimEnd('.', ','), UriKind.Absolute, out var uri)) continue;
            var host = uri.Host.ToLowerInvariant();
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            var first = segments[0];
            if (host.EndsWith("instagram.com") && !IsReserved(first, "p", "reel", "reels", "explore", "accounts", "stories", "share", "tv"))
                candidates["instagram"].Add($"https://www.instagram.com/{first}/");
            else if ((host.EndsWith("facebook.com") || host == "fb.com") && !IsReserved(first, "sharer", "sharer.php", "share", "plugins", "dialog", "tr", "login", "watch", "events", "groups", "photo", "photo.php", "story.php", "permalink.php", "hashtag", "policies", "tr_tr", "people"))
                candidates["facebook"].Add(first.Equals("profile.php", StringComparison.OrdinalIgnoreCase) && uri.Query.Length > 0 ? $"https://www.facebook.com/profile.php{uri.Query}" : $"https://www.facebook.com/{first}");
            else if (host.EndsWith("linkedin.com") && segments.Length >= 2 && first is ("company" or "in"))
                candidates["linkedin"].Add($"https://www.linkedin.com/{first}/{segments[1]}");
            else if (host.EndsWith("tiktok.com") && first.StartsWith('@') && first.Length > 1)
                candidates["tiktok"].Add($"https://www.tiktok.com/{first}");
        }
        var label = siteHost?.Split('.')[0];
        return new SiteContacts(emails, Limit(Best(candidates["instagram"], label)), Limit(Best(candidates["facebook"], label)), Limit(Best(candidates["linkedin"], label)), Limit(Best(candidates["tiktok"], label)));

        static bool IsReserved(string segment, params string[] reserved) => reserved.Contains(segment, StringComparer.OrdinalIgnoreCase);
        static string? Limit(string? url) => url is { Length: <= 300 } ? url : null;
    }

    // Sitede başka firmaların (referans, tedarikçi) hesapları da olabilir. Alan adına benzeyen hesap seçilir:
    // karakusaluminyum.com için "karakusaluminyumizmir", referans logosundaki "beevillas"tan önce gelir.
    private static string? Best(List<string> urls, string? siteLabel)
    {
        if (urls.Count == 0) return null;
        var label = Ascii(siteLabel);
        return urls.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((url, index) => (url, index, score: Similarity(Ascii(Handle(url)), label)))
            .OrderByDescending(x => x.score).ThenBy(x => x.index).First().url;

        static string Handle(string url) => url.TrimEnd('/').Split('/').Last().TrimStart('@');
        static int Similarity(string handle, string label)
        {
            if (label.Length < 4 || handle.Length < 4) return 0;
            if (handle.Contains(label) || label.Contains(handle)) return 2;
            var prefix = 0;
            while (prefix < Math.Min(handle.Length, label.Length) && handle[prefix] == label[prefix]) prefix++;
            return prefix >= 5 ? 1 : 0;
        }
    }

    // Türkçe harfler sadeleştirilir; yalnızca harf ve rakam kalır: "Karakuş-Alüminyum" -> "karakusaluminyum".
    public static string Ascii(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var map = new Dictionary<char, char> { ['ç'] = 'c', ['ğ'] = 'g', ['ı'] = 'i', ['İ'] = 'i', ['ö'] = 'o', ['ş'] = 's', ['ü'] = 'u', ['Ç'] = 'c', ['Ğ'] = 'g', ['Ö'] = 'o', ['Ş'] = 's', ['Ü'] = 'u' };
        return new string(text.Select(c => map.TryGetValue(c, out var r) ? r : char.ToLowerInvariant(c)).Where(char.IsAsciiLetterOrDigit).ToArray());
    }

    // Aynı sitedeki iletişim / hakkımızda sayfaları: e-posta çoğunlukla ana sayfada değil oradadır.
    public static IReadOnlyList<Uri> ContactPages(string html, Uri page, int max = 2)
    {
        var words = new[] { "iletisim", "iletişim", "contact", "bize-ulasin", "bize-ulaşın", "hakkimizda", "hakkımızda", "about" };
        return HrefRegex().Matches(html ?? "").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Select(h => Uri.TryCreate(page, h, out var u) ? u : null)
            .Where(u => u is not null && u.Scheme is ("http" or "https") && u.Host.Equals(page.Host, StringComparison.OrdinalIgnoreCase)
                && words.Any(w => Uri.UnescapeDataString(u.AbsolutePath).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(u => new Uri(u!.GetLeftPart(UriPartial.Path)))
            .Where(u => u != new Uri(page.GetLeftPart(UriPartial.Path)))
            .DistinctBy(u => u.AbsoluteUri).Take(max).ToList()!;
    }

    // robots.txt: "User-agent: *" grubundaki Disallow kuralları. Allow ayrıntıları basitlik için yok sayılır.
    public static bool RobotsAllows(string? robots, string path)
    {
        if (string.IsNullOrWhiteSpace(robots)) return true;
        var applies = false;
        var groupStarted = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                if (groupStarted) { applies = false; groupStarted = false; }
                applies |= value == "*";
            }
            else
            {
                groupStarted = true;
                if (applies && key == "disallow" && value.Length > 0 && path.StartsWith(value, StringComparison.Ordinal)) return false;
            }
        }
        return true;
    }

    // Sunucunun kendi ağına (localhost, iç ağ, bulut meta veri adresi) istek atılmasını engeller.
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);
        return false;
    }

    // Excel'de açılan CSV: noktalı virgül ayraç; "=" ile başlayan hücre formül olarak çalışmasın.
    public static string CsvCell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static string Truncate(string? text, int max) => text is null ? "" : text.Length <= max ? text : text[..max];

    public static string Csv(IEnumerable<IEnumerable<string?>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows) builder.Append(string.Join(';', row.Select(CsvCell))).Append("\r\n");
        return builder.ToString();
    }
}
