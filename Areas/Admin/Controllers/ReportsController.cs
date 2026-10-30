using System.Text;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/raporlar")]
public sealed class ReportsController : Controller
{
    private readonly ReportService _reports;
    private readonly SiteSettingsProvider _settings;
    public ReportsController(ReportService reports, SiteSettingsProvider settings) { _reports = reports; _settings = settings; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? aralik, string? bas, string? bit)
    {
        var settings = await _settings.GetAsync();
        ViewBag.Ga4Connected = !string.IsNullOrWhiteSpace(settings.Ga4MeasurementId);
        ViewBag.PixelConnected = !string.IsNullOrWhiteSpace(settings.MetaPixelId);
        return View(await _reports.BuildAsync(ReportRange.Parse(aralik, bas, bit)));
    }

    [HttpGet("csv")]
    public async Task<IActionResult> Csv(string? bolum, string? aralik, string? bas, string? bit)
    {
        var report = await _reports.BuildAsync(ReportRange.Parse(aralik, bas, bit));
        var (name, sections) = bolum switch
        {
            "uretim" => ("uretim", new (string, IReadOnlyList<BarRow>)[] { ("Ürün türüne göre adet", report.Production.UnitsByCategory), ("Cam tipine göre alan", report.Production.GlassByType) }),
            "iletisim" => ("iletisim", new (string, IReadOnlyList<BarRow>)[] { ("Hizmete göre mesaj", report.Contact.ByService) }),
            "web" => ("web", new (string, IReadOnlyList<BarRow>)[] { ("En çok görüntülenen sayfalar", report.Web.TopPages), ("Kaynaklar", report.Web.Sources), ("Cihazlar", report.Web.Devices), ("Olaylar", report.Web.Events), ("Kaynağa göre form", report.Web.LeadsBySource) }),
            _ => ("satis", new (string, IReadOnlyList<BarRow>)[] { ("Duruma göre teklif", report.Sales.QuotesByStatus), ("En yüksek tutarlı müşteriler", report.Sales.TopCustomers), ("Ödeme planı", report.Sales.PaymentsByKind), ("Duruma göre sözleşme", report.Sales.ContractsByStatus) })
        };

        var csv = new StringBuilder();
        csv.AppendLine($"Tarih aralığı;{report.Range.FromText};{report.Range.ToText}");
        foreach (var (title, rows) in sections)
        {
            csv.AppendLine().AppendLine(Cell(title));
            foreach (var row in rows) csv.AppendLine($"{Cell(row.Label)};{row.Value.ToString(TurkishNumbers.Culture)}");
        }
        // Excel'in Türkçe karakterleri doğru açması için BOM eklenir.
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"rapor-{name}-{report.Range.FromText}-{report.Range.ToText}.csv");
    }

    // Excel'de formül olarak çalışmasın diye kullanıcı verisi korunur.
    public static string Cell(string value)
    {
        var clean = value.Replace("\"", "\"\"");
        if (clean.Length > 0 && "=+-@\t\r".Contains(clean[0])) clean = "'" + clean;
        return clean.Contains(';') || clean.Contains('"') || clean.Contains('\n') ? $"\"{clean}\"" : clean;
    }
}
