using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// README 7.19: tüm siparişlerde ödenmemiş taksitler (geciken, yaklaşan). Tahsilat muhasebeden siparişe bağlanarak girilir.
[Area("Admin"), Authorize(Roles = "Admin,Editor,Accountant"), Route("yonetim/odemeler")]
public sealed class PaymentsController : Controller
{
    private readonly AppDbContext _db;
    public PaymentsController(AppDbContext db) => _db = db;

    public sealed record DueRow(ProductionOrder Order, InstallmentStatus Status);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? goster)
    {
        var today = KarakusAluminyum.Web.Services.Documents.PrintDates.Today;
        var rows = await DueAsync(_db, today);
        ViewBag.Counts = (Late: rows.Count(r => r.Status.State == InstallmentState.Late), Week: rows.Count(r => r.Status.Row.DueOn is DateOnly d && d >= today && d <= today.AddDays(7)), All: rows.Count);
        ViewBag.Show = goster ?? "hafta";
        rows = (goster ?? "hafta") switch
        {
            "geciken" => rows.Where(r => r.Status.State == InstallmentState.Late).ToList(),
            "tumu" => rows,
            _ => rows.Where(r => r.Status.State == InstallmentState.Late || r.Status.Row.DueOn is DateOnly d && d <= today.AddDays(7)).ToList()
        };
        ViewBag.Total = rows.Sum(r => r.Status.Remaining);
        return View(rows);
    }

    // Açık siparişlerde (iptal hariç) kalan tutarı olan taksitler, vadeye göre.
    public static async Task<List<DueRow>> DueAsync(AppDbContext db, DateOnly today)
    {
        var installments = await db.OrderInstallments.AsNoTracking().Include(x => x.ProductionOrder).ThenInclude(x => x!.Customer)
            .Where(x => x.ProductionOrder!.Status != OrderStatus.Cancelled).ToListAsync();
        var collected = await PaymentPlans.CollectedAsync(db, installments.Select(x => x.ProductionOrderId));
        return installments.GroupBy(x => x.ProductionOrderId)
            .SelectMany(g => PaymentPlans.Summarize(g, collected.GetValueOrDefault(g.Key), today).Rows.Where(r => r.Remaining > 0).Select(r => new DueRow(g.First().ProductionOrder!, r)))
            .OrderBy(r => r.Status.Row.DueOn ?? DateOnly.MaxValue).ThenBy(r => r.Order.OrderNumber).ToList();
    }
}
