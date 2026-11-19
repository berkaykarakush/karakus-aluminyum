using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Controllers;

// Belgedeki QR kodun açtığı sayfa: teklif, sözleşme veya çizim listesinin sistemdeki asıl hali.
// Giriş gerekmez; anahtar 128 bit rastgeledir ve panelden yenilenebilir. Maliyet, marj ve malzeme bilgisi gösterilmez.
// Sayfalar arama motorlarına kapalıdır ve önbelleğe alınmaz (yenilenen bağlantı hemen geçersiz olur).
[AllowAnonymous, Route("belge"), EnableRateLimiting(RateLimitPolicy)]
public sealed class DocumentLinksController : Controller
{
    public const string RateLimitPolicy = "documents";
    private readonly AppDbContext _db;
    public DocumentLinksController(AppDbContext db) => _db = db;

    [HttpGet("teklif/{token}")]
    public async Task<IActionResult> Quote(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var quote = await _db.Quotes.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).FirstOrDefaultAsync(x => x.PublicToken == token);
        if (quote is null) return NotFound();
        Private($"{quote.QuoteNumber} numaralı proforma teklif · {TurkishNumbers.Date(quote.DocumentDate)} · Durum: {Status(quote.Status)}", DocumentLinks.Quote, token);
        return View("~/Areas/Admin/Views/Documents/QuotePrint.cshtml", await DocumentViews.QuotePrintAsync(_db, quote));
    }

    [HttpGet("sozlesme/{token}")]
    public async Task<IActionResult> Contract(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var contract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.PublicToken == token);
        if (contract is null) return NotFound();
        Private($"{contract.ContractNumber} numaralı sözleşme · {TurkishNumbers.Date(contract.DocumentDate)} · Durum: {contract.Status switch { ContractStatus.Signed => "İmzalandı", ContractStatus.Cancelled => "İptal edildi", _ => "Taslak" }}", DocumentLinks.Contract, token);
        return View("~/Areas/Admin/Views/Documents/ContractPrint.cshtml", await DocumentViews.ContractPrintAsync(_db, contract));
    }

    [HttpGet("cizim/{token}")]
    public async Task<IActionResult> Drawing(string token)
    {
        if (!DocumentLinks.LooksValid(token)) return NotFound();
        var list = await _db.DrawingLists.AsNoTracking().Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Brand)
            .Include(x => x.Items).ThenInclude(x => x.ProfileSystem).AsSplitQuery().FirstOrDefaultAsync(x => x.PublicToken == token);
        if (list is null) return NotFound();
        Private($"Çizim listesi #{list.Id} · Son güncelleme {TurkishNumbers.Date(list.UpdatedUtc.ToLocalTime())}", DocumentLinks.Drawing, token);
        return View("~/Views/DocumentLinks/Drawing.cshtml", list);
    }

    private void Private(string note, string kind, string token)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ViewData["PublicNote"] = note + ". Bu sayfa belgenin Karakuş Alüminyum kayıtlarındaki asıl halidir.";
        ViewData["Qr"] = DocumentLinks.Qr(Request, kind, token);
    }

    private static string Status(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "Taslak", QuoteStatus.Sent => "Gönderildi", QuoteStatus.Accepted => "Kabul edildi", QuoteStatus.Rejected => "Reddedildi",
        QuoteStatus.Expired => "Süresi doldu", _ => "Sözleşmeye dönüştürüldü"
    };
}
