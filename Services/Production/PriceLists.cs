using System.Globalization;
using System.Xml.Linq;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record RateInfo(decimal Rate, DateOnly Date, string Source);
public sealed record PriceListRow(int Line, string Code, decimal Price, string? Currency);

// Fiyat listeleri ve döviz: stok kartlarının alış fiyatı kendi para biriminde tutulur, metraj maliyeti güncel kurla TL'ye çevrilir.
public static class PriceLists
{
    public const string TcmbClient = "tcmb";
    public const string TcmbUrl = "https://www.tcmb.gov.tr/kurlar/today.xml";
    public static readonly string[] ForeignCurrencies = { "EUR", "USD" };

    public static async Task<Dictionary<string, RateInfo>> LatestRatesAsync(AppDbContext db)
    {
        var rows = await db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();
        return rows.GroupBy(x => x.Currency).ToDictionary(g => g.Key, g => new RateInfo(g.First().Rate, g.First().Date, g.First().Source));
    }

    public static decimal? ToTry(decimal amount, string? currency, IReadOnlyDictionary<string, RateInfo> rates)
    {
        if (string.IsNullOrEmpty(currency) || currency == "TRY") return amount;
        return rates.TryGetValue(currency, out var rate) ? amount * rate.Rate : null;
    }

    // TCMB today.xml: <Tarih_Date Tarih="21.11.2026"><Currency CurrencyCode="EUR"><Unit>1</Unit><ForexSelling>..</ForexSelling>
    public static (DateOnly Date, List<(string Currency, decimal Rate)> Rates) ParseTcmb(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("TCMB yanıtı boş.");
        var date = DateOnly.ParseExact((string?)root.Attribute("Tarih") ?? "", "dd.MM.yyyy", CultureInfo.InvariantCulture);
        var rates = new List<(string, decimal)>();
        foreach (var currency in root.Elements("Currency"))
        {
            var code = (string?)currency.Attribute("CurrencyCode");
            if (code is null || !ForeignCurrencies.Contains(code)) continue;
            var unit = decimal.TryParse((string?)currency.Element("Unit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var u) && u > 0 ? u : 1;
            if (decimal.TryParse((string?)currency.Element("ForexSelling"), NumberStyles.Number, CultureInfo.InvariantCulture, out var selling) && selling > 0)
                rates.Add((code, selling / unit));
        }
        return (date, rates);
    }

    // Satır başına "kod;fiyat[;para birimi]" (ayraç ; veya sekme). Fiyatta 1.234,56 ve 1234.56 yazımları kabul edilir.
    public static (List<PriceListRow> Rows, List<string> Errors) ParsePriceList(string? text)
    {
        var rows = new List<PriceListRow>();
        var errors = new List<string>();
        var lines = (text ?? "").Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var cells = line.Split(line.Contains('\t') ? '\t' : ';').Select(x => x.Trim()).ToArray();
            if (cells.Length < 2) { errors.Add($"Satır {i + 1}: kod ve fiyat ayraçla (; veya sekme) ayrılmalı."); continue; }
            if (!TryPrice(cells[1], out var price))
            {
                if (i == 0) continue; // başlık satırı
                errors.Add($"Satır {i + 1}: \"{cells[1]}\" fiyat olarak okunamadı.");
                continue;
            }
            var currency = cells.Length > 2 && cells[2].Length > 0 ? cells[2].ToUpperInvariant().Replace("TL", "TRY").Replace("€", "EUR").Replace("$", "USD") : null;
            if (currency is not null && !AccountingLabels.Currencies.Contains(currency)) { errors.Add($"Satır {i + 1}: para birimi TRY, EUR veya USD olmalı."); continue; }
            if (cells[0].Length == 0) { errors.Add($"Satır {i + 1}: kod boş."); continue; }
            rows.Add(new PriceListRow(i + 1, cells[0], price, currency));
        }
        return (rows, errors);
    }

    public static bool TryPrice(string text, out decimal price)
    {
        var value = text.Replace(" ", "").Replace("₺", "").Replace("€", "").Replace("$", "");
        var comma = value.LastIndexOf(',');
        var dot = value.LastIndexOf('.');
        if (comma >= 0 && dot >= 0) value = comma > dot ? value.Replace(".", "").Replace(',', '.') : value.Replace(",", "");
        else if (comma >= 0) value = value.Replace(',', '.');
        else if (value.Count(c => c == '.') > 1) value = value.Replace(".", "");
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price) && price >= 0;
    }
}
