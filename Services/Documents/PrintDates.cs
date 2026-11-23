using System.Globalization;

namespace KarakusAluminyum.Web.Services.Documents;

// Çıktılardaki belge tarihi: varsayılan yazdırma anı (Türkiye saati), "?tarih=yyyy-MM-dd" ile başka gün seçilir.
// Kayıtlı belge tarihleri (teklif, sözleşme, fatura, irsaliye) değişmez; yalnızca basılan tarih değişir.
public sealed record PrintDateVm(DateOnly Date, DateOnly? Recorded = null, string RecordedLabel = "Kayıtlı tarih");

public static class PrintDates
{
    public const string Query = "tarih";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    public static DateOnly From(HttpRequest request)
        => DateOnly.TryParseExact(request.Query[Query].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : Today;

    public static DateTime AsDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public static string Text(DateOnly date) => TurkishNumbers.Date(AsDateTime(date));
}
