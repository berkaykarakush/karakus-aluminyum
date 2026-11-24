using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Production;

// README 7.19: ödeme planı hesabı. Siparişe bağlı tahsilatlar (iptal edilmemiş) taksitlere vade sırasıyla dağıtılır.
public static class PaymentPlans
{
    public static PaymentSummary Summarize(IEnumerable<OrderInstallment> installments, decimal collected, DateOnly today)
    {
        var rows = installments.OrderBy(x => x.DueOn ?? DateOnly.MaxValue).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        var left = collected;
        var list = new List<InstallmentStatus>();
        foreach (var row in rows)
        {
            var paid = Math.Min(left, row.Amount);
            left -= paid;
            var remaining = row.Amount - paid;
            var state = remaining <= 0 ? InstallmentState.Paid
                : row.DueOn is DateOnly due && due < today ? InstallmentState.Late
                : paid > 0 ? InstallmentState.Partial : InstallmentState.Waiting;
            list.Add(new InstallmentStatus(row, paid, remaining, state));
        }
        var planned = rows.Sum(x => x.Amount);
        return new PaymentSummary(planned, collected, Math.Max(planned - collected, 0), Math.Max(collected - planned, 0), list.OrderBy(x => x.Row.SortOrder).ThenBy(x => x.Row.Id).ToList());
    }

    // Kapora oranı ve teslim tarihinden öneri: kapora bugün, kalan teslimde (tarih verilmişse o gün).
    public static List<OrderInstallment> Suggest(decimal total, decimal depositPercent, DateOnly today, DateOnly? delivery)
    {
        if (total <= 0) return new();
        depositPercent = Math.Clamp(depositPercent, 0, 100);
        var deposit = decimal.Round(total * depositPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var list = new List<OrderInstallment>();
        if (deposit > 0) list.Add(new OrderInstallment { Label = "Kapora", DueOn = today, Amount = deposit, SortOrder = 0 });
        if (total - deposit > 0) list.Add(new OrderInstallment { Label = "Teslimde kalan", DueOn = delivery, Amount = total - deposit, SortOrder = list.Count });
        return list;
    }

    public static IQueryable<CashTransaction> Collections(AppDbContext db)
        => db.CashTransactions.Where(x => x.ProductionOrderId != null && !x.IsCancelled && x.IsIn && x.Kind == CashKind.Collection);

    public static async Task<Dictionary<int, decimal>> CollectedAsync(AppDbContext db, IEnumerable<int> orderIds)
    {
        var ids = orderIds.Distinct().ToList();
        return await Collections(db).Where(x => ids.Contains(x.ProductionOrderId!.Value)).GroupBy(x => x.ProductionOrderId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.AmountTry) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
    }

    public static async Task<PaymentSummary> SummaryAsync(AppDbContext db, int orderId, DateOnly today)
    {
        var rows = await db.OrderInstallments.AsNoTracking().Where(x => x.ProductionOrderId == orderId).ToListAsync();
        var collected = (await CollectedAsync(db, new[] { orderId })).GetValueOrDefault(orderId);
        return rows.Count == 0 && collected == 0 ? PaymentSummary.Empty : Summarize(rows, collected, today);
    }

    // Sipariş tutarı önerisi: onaylanan veya teklifin genel toplamı.
    public static decimal? OrderTotal(ProductionOrder order) => order.Quote?.AcceptedTotal ?? order.Quote?.GrandTotal;
}
