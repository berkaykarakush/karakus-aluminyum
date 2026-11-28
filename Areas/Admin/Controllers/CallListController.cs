using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Prospecting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

public sealed record CallProspect(Prospect Prospect, ProspectScore Score, string Message);
public sealed record WinBackRow(Quote Quote, string CustomerName, string? Phone, string? Email, string Message, int DaysSinceQuote);
public sealed record CallListVm(IReadOnlyList<CallProspect> Prospects, int CallableTotal, IReadOnlyList<WinBackRow> WinBacks, int Take);

// Günlük arama listesi: önceliğe göre aranacak firmalar ve fiyat/erteleme yüzünden kaybedilip yeniden aranacak teklifler.
// WhatsApp ve e-posta yalnızca elle gönderilir: metin hazır açılır, gönderen kişi gönder tuşuna basar.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/crm/arama-listesi")]
public sealed class CallListController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public CallListController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    [HttpGet("")]
    public async Task<IActionResult> Index(int adet = 20)
    {
        var take = Math.Clamp(adet, 5, 100);
        var today = Today;
        var closed = ProspectPriority.Closed;
        var open = await _db.Prospects.AsNoTracking().Include(x => x.Sector)
            .Where(x => !x.DoNotContact && !closed.Contains(x.Stage) && x.Phone != null && (x.NextFollowUp != null ? x.NextFollowUp <= today : x.Stage == ProspectStage.New))
            .ToListAsync();
        var rates = await ProspectYield.SectorWinRatesAsync(_db);
        var sender = (await DocumentPeople.CurrentAsync(_db, User))?.FullName;
        var scored = open.Select(p => new CallProspect(p, ProspectPriority.Score(p, today, p.SectorId is int s && rates.TryGetValue(s, out var r) ? r : null), ProspectPriority.Message(p.Sector?.MessageTemplate, p, sender)))
            .OrderByDescending(x => x.Score.Total).ThenBy(x => x.Prospect.NextFollowUp ?? DateOnly.MaxValue).ThenBy(x => x.Prospect.Id).ToList();
        return View(new CallListVm(scored.Take(take).ToList(), scored.Count, await WinBacksAsync(today, sender), take));
    }

    public static Task<int> DueCountAsync(AppDbContext db, DateOnly today)
        => db.Quotes.CountAsync(x => x.Status == QuoteStatus.Rejected && x.WinBackOn != null && x.WinBackOn <= today);

    private async Task<List<WinBackRow>> WinBacksAsync(DateOnly today, string? sender)
    {
        var quotes = await _db.Quotes.AsNoTracking().Include(x => x.Customer)
            .Where(x => x.Status == QuoteStatus.Rejected && x.WinBackOn != null && x.WinBackOn <= today)
            .OrderBy(x => x.WinBackOn).ThenByDescending(x => x.GrandTotal).Take(100).ToListAsync();
        return quotes.Select(q =>
        {
            var name = q.Customer?.ContactPerson ?? q.Customer?.Name ?? "";
            var text = $"Merhaba {name}, Karakuş Alüminyum'dan yazıyoruz. {TurkishNumbers.Date(q.DocumentDate)} tarihli \"{q.Title}\" teklifimizle ilgili size güncel fiyat ve teslim seçeneklerimizi iletmek isteriz. Uygun olduğunuz bir zamanda kısa bir görüşme yapabilir miyiz?\n\n{(string.IsNullOrWhiteSpace(sender) ? "Karakuş Alüminyum" : sender + ", Karakuş Alüminyum")}";
            return new WinBackRow(q, q.Customer?.Name ?? "", q.Customer?.Phone, q.Customer?.Email, text, today.DayNumber - DateOnly.FromDateTime(q.DocumentDate).DayNumber);
        }).ToList();
    }

    // Geri arama sonucu: "sonra" teklifi 30 gün sonra yeniden listeye alır, "kapat" listeden çıkarır. Not teklife yazılır.
    [HttpPost("teklif/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> WinBack(int id, string sonuc, string? note)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(x => x.Id == id);
        if (quote is null) return NotFound();
        if (quote.Status != QuoteStatus.Rejected) { TempData["Notice"] = "Yalnızca reddedilen teklifler geri arama listesindedir."; return RedirectToAction(nameof(Index)); }
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var stamp = $"{Today:dd.MM.yyyy} {(sonuc == "kapat" ? "kapatıldı" : "arandı")}";
        quote.WinBackNote = ProspectText.Truncate(text is null ? stamp : $"{stamp}: {text}", 300);
        quote.WinBackOn = sonuc == "kapat" ? null : Today.AddDays(30);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("win-back", "Quote", id.ToString(), quote.WinBackNote);
        TempData["Notice"] = sonuc == "kapat" ? $"{quote.QuoteNumber} geri arama listesinden çıkarıldı." : $"{quote.QuoteNumber} 30 gün sonra yeniden listeye gelecek.";
        return RedirectToAction(nameof(Index));
    }
}
