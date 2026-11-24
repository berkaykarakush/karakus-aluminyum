using System.Security.Cryptography;
using System.Text;
using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Services.Documents;

// README 7.18: müşterinin QR'lı teklif sayfasından onayı. Onay anındaki tutar ve belge içeriğinin özeti saklanır;
// teklif sonradan değişse bile neyin onaylandığı kanıtlanabilir.
public sealed record QuoteAcceptanceVm(string Token, string? Blocked, DateTime? AcceptedUtc, string? AcceptedName, decimal? AcceptedTotal);

public static class QuoteAcceptance
{
    public const int MinNameLength = 3;

    // Online onay yalnızca gönderilmiş ve süresi geçmemiş teklifte açıktır; aksi hâlde nedeni döner.
    public static string? Blocked(Quote quote, DateTime nowUtc) => quote.Status switch
    {
        QuoteStatus.Accepted or QuoteStatus.Converted => "Bu teklif onaylandı.",
        QuoteStatus.Rejected => "Bu teklif reddedildi; güncel teklif için bize ulaşın.",
        QuoteStatus.Expired => "Bu teklifin geçerlilik süresi doldu; güncel teklif için bize ulaşın.",
        QuoteStatus.Draft => "Teklif henüz hazırlanıyor.",
        _ when quote.ValidUntilUtc is DateTime until && until.Date < nowUtc.AddHours(3).Date => "Bu teklifin geçerlilik süresi doldu; güncel teklif için bize ulaşın.",
        _ => null
    };

    public static string Hash(Quote quote)
    {
        var text = $"{quote.QuoteNumber}|{quote.GrandTotal:0.00}|{quote.DocumentJson}|{quote.SnapshotJson}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static void Accept(Quote quote, string name, string? ip, string? userAgent, DateTime nowUtc)
    {
        quote.Status = QuoteStatus.Accepted;
        quote.AcceptedUtc = nowUtc;
        quote.AcceptedName = name.Trim() is { Length: > 120 } longName ? longName[..120] : name.Trim();
        quote.AcceptedIp = ip is { Length: > 64 } ? ip[..64] : ip;
        quote.AcceptedUserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        quote.AcceptedTotal = quote.GrandTotal;
        quote.AcceptedHash = Hash(quote);
    }
}
