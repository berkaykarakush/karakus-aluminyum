using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models.Documents;
using Microsoft.AspNetCore.Html;

namespace KarakusAluminyum.Web.Services.Documents;

// {{degisken}} alanlı şablon metinleri. Boş kalan alan, elle doldurulabilsin diye çizgi olarak basılır.
public static partial class TemplateText
{
    public const string Blank = "______________";

    [GeneratedRegex(@"\{\{\s*([\p{L}\p{N}_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Singleline)]
    private static partial Regex BoldRegex();

    public static IReadOnlyList<string> Placeholders(string? text)
        => string.IsNullOrEmpty(text) ? Array.Empty<string>()
            : PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static string Render(string? text, params IReadOnlyDictionary<string, string>?[] scopes)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return PlaceholderRegex().Replace(text, match =>
        {
            foreach (var scope in scopes)
                if (scope is not null && scope.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            return Blank;
        });
    }

    // Önce HTML kodlanır, sonra yalnızca **kalın** ve satır sonu işlenir; kullanıcı metni HTML üretemez.
    public static IHtmlContent ToHtml(string? text, params IReadOnlyDictionary<string, string>?[] scopes)
    {
        var encoded = WebUtility.HtmlEncode(Render(text, scopes));
        encoded = BoldRegex().Replace(encoded, "<strong>$1</strong>");
        return new HtmlString(encoded.Replace("\r\n", "\n").Replace("\n", "<br>"));
    }
}

public static class TurkishNumbers
{
    private static readonly string[] Ones = { "", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz" };
    private static readonly string[] Tens = { "", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan" };
    private static readonly string[] Scales = { "", "Bin", "Milyon", "Milyar", "Trilyon" };
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");
    private static CultureInfo Turkish => Culture;

    public static string ToWords(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) return "Sıfır";

        var parts = new List<string>();
        for (var scale = 0; value > 0; scale++, value /= 1000)
        {
            var group = (int)(value % 1000);
            if (group == 0) continue;
            // Türkçede 1000 "Bin" okunur, "BirBin" değil.
            var words = scale == 1 && group == 1 ? "" : GroupWords(group);
            parts.Insert(0, words + Scales[scale]);
        }
        return string.Concat(parts);
    }

    private static string GroupWords(int group)
    {
        var builder = new StringBuilder();
        var hundreds = group / 100;
        if (hundreds > 0) builder.Append(hundreds == 1 ? "" : Ones[hundreds]).Append("Yüz");
        builder.Append(Tens[group / 10 % 10]).Append(Ones[group % 10]);
        return builder.ToString();
    }

    // Örnek: 3750000 -> ÜçMilyonYediYüzElliBinTürkLirası
    public static string MoneyWords(decimal amount)
    {
        var rounded = decimal.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var lira = (long)decimal.Truncate(rounded);
        var kurus = (int)((rounded - lira) * 100);
        var words = ToWords(lira) + "TürkLirası";
        return kurus > 0 ? words + ToWords(kurus) + "Kuruş" : words;
    }

    public static string Money(decimal amount) => amount.ToString("#,##0.00", Turkish) + " TL";

    public static string MoneyWithWords(decimal amount) => $"{Money(amount)} ({MoneyWords(amount)})";

    public static string CountWords(int value) => ToWords(value).ToLower(Turkish);

    public static string Date(DateTime date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

public static class DocumentJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    public static DocumentContent ParseContent(string? json) => Parse<DocumentContent>(json) ?? new DocumentContent();
    public static DocumentContent? TryParseContent(string? json) => Parse<DocumentContent>(json);
    public static List<ContractArticle> ParseArticles(string? json) => Parse<List<ContractArticle>>(json) ?? new();
    public static DocumentTemplate? ParseTemplate(string? json) => string.IsNullOrWhiteSpace(json) || json == "{}" ? null : Parse<DocumentTemplate>(json);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }
}

public static class PaymentPlan
{
    // Ödeme satırını, örnek sözleşmelerdeki cümle kalıbıyla yazar. isRemainder: planın son satırı "Kalan" diye başlar.
    public static string Describe(PaymentLine line, bool isRemainder = false)
    {
        var total = $"**{TurkishNumbers.MoneyWithWords(line.Amount)}**";
        var text = line.Text?.Trim();
        switch (line.Kind)
        {
            case PaymentKinds.Cash:
                return string.IsNullOrEmpty(text) ? $"{total} peşin olarak ödenecektir." : $"{total} {text}";
            case PaymentKinds.Check:
                // Açıklama yazılırsa çek ifadesinin yerine geçer: "sıralı 5 ay vadeli çeklerle" gibi.
                var count = Math.Max(line.Count, 1);
                var how = string.IsNullOrEmpty(text) ? $"{count} adet çekle" : text;
                var each = count > 1 ? $"her biri **{TurkishNumbers.MoneyWithWords(line.Amount / count)}** değerinde olmak üzere " : "";
                return $"{(isRemainder ? "Kalan " : "")}{total} tutar, {each}{how} tahsil edilecektir.";
            case PaymentKinds.Barter:
                return string.IsNullOrEmpty(text) ? $"**Barter:** {total} bedelle barter olarak verilecektir." : $"**Barter:** {text} {total} bedelle barter olarak verilecektir.";
            default:
                return line.Amount > 0 ? $"{total} {text}".TrimEnd() : text ?? "";
        }
    }

    public static IEnumerable<string> Validate(IReadOnlyCollection<PaymentLine> lines, decimal total)
    {
        foreach (var line in lines)
        {
            if (!PaymentKinds.All.Contains(line.Kind)) yield return "Geçersiz ödeme tipi.";
            if (line.Amount < 0) yield return "Ödeme tutarı negatif olamaz.";
            if (line.Kind == PaymentKinds.Check && line.Count < 1) yield return "Çek adedi en az 1 olmalıdır.";
        }
        var sum = lines.Sum(l => l.Amount);
        if (lines.Count > 0 && sum != total)
            yield return $"Ödeme planı toplamı ({TurkishNumbers.Money(sum)}) işin toplam bedeline ({TurkishNumbers.Money(total)}) eşit değil.";
    }
}
