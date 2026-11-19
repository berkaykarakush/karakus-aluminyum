using System.Security.Cryptography;
using KarakusAluminyum.Web.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace KarakusAluminyum.Web.Services.Documents;

// Belgedeki QR kodu: okutulunca belgenin sistemdeki asıl hali açılır (/belge/{tür}/{anahtar}).
// Anahtar 128 bit rastgeledir, tahmin edilemez; panelden yenilenince eski QR çalışmaz.
public sealed record DocumentQr(string Url, string Svg);

public static class DocumentLinks
{
    public const string Quote = "teklif";
    public const string Contract = "sozlesme";
    public const string Drawing = "cizim";

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool LooksValid(string? token) => token is { Length: 22 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Adres, belgeyi açan isteğin alan adından kurulur (nginx arkasında iletilen başlıklarla doğru alan adı gelir).
    public static string Url(HttpRequest request, string kind, string token) => $"{request.Scheme}://{request.Host}{request.PathBase}/belge/{kind}/{token}";

    public static DocumentQr Qr(HttpRequest request, string kind, string token)
    {
        var url = Url(request, kind, token);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(1, "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        return new DocumentQr(url, svg);
    }

    // Belgenin anahtarı yoksa ilk basımda oluşturulur. Aynı anda iki istek gelirse ilk yazılan anahtar kullanılır.
    public static async Task<string> EnsureQuoteAsync(AppDbContext db, int id, string? current)
    {
        if (current is not null) return current;
        await db.Quotes.Where(x => x.Id == id && x.PublicToken == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublicToken, NewToken()));
        return await db.Quotes.Where(x => x.Id == id).Select(x => x.PublicToken!).SingleAsync();
    }

    public static async Task<string> EnsureContractAsync(AppDbContext db, int id, string? current)
    {
        if (current is not null) return current;
        await db.Contracts.Where(x => x.Id == id && x.PublicToken == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublicToken, NewToken()));
        return await db.Contracts.Where(x => x.Id == id).Select(x => x.PublicToken!).SingleAsync();
    }

    public static async Task<string> EnsureDrawingAsync(AppDbContext db, int id, string? current)
    {
        if (current is not null) return current;
        await db.DrawingLists.Where(x => x.Id == id && x.PublicToken == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublicToken, NewToken()));
        return await db.DrawingLists.Where(x => x.Id == id).Select(x => x.PublicToken!).SingleAsync();
    }
}
