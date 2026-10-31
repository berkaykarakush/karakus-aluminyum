using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services.Accounting;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services;

public sealed record ExpiringQuote(int Id, string Title, string Customer, decimal Total, DateTime ValidUntilUtc);
public sealed record DashboardPipeline(int OpenCount, decimal OpenTotal, IReadOnlyList<BarRow> Funnel, IReadOnlyList<ExpiringQuote> Expiring);
public sealed record DashboardFinance(decimal Cash, decimal Receivable, decimal Payable, decimal OverdueTotal, int OverdueCount,
    decimal Revenue, decimal PreviousRevenue, decimal Costs, decimal PreviousCosts, decimal Profit, decimal PreviousProfit,
    decimal ChequesIn, decimal ChequesOut, int CriticalStock, decimal DueIn, decimal DueOut,
    TimeChart Chart, IReadOnlyList<BarRow> Aging, IReadOnlyList<BarRow> TopDebtors, IReadOnlyList<BarRow> ExpenseCategories, IReadOnlyList<CashFlowRow> Upcoming);

// Ana sayfa analizinde raporlarda bulunmayan göstergeler. Finans bölümü yalnızca yöneticiye istenir.
public sealed class DashboardService
{
    public const int DueWindowDays = 30;
    private readonly AppDbContext _db;
    private readonly AccountingQueries _accounting;
    public DashboardService(AppDbContext db, AccountingQueries accounting) { _db = db; _accounting = accounting; }

    private static string Count(int value) => value.ToString("N0", TurkishNumbers.Culture);
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc + ReportRange.Offset);
    private static DateTime Utc(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - ReportRange.Offset, DateTimeKind.Utc);

    public async Task<DashboardPipeline> PipelineAsync(ReportRange range, DateTime nowUtc)
    {
        var statuses = await _db.Quotes.AsNoTracking().Where(q => q.CreatedUtc >= range.FromUtc && q.CreatedUtc < range.ToUtc).Select(q => q.Status).ToListAsync();
        // Aynı dönemde oluşturulan tekliflerin bugün geldiği aşama; her adım bir öncekinin alt kümesidir.
        var funnel = new (string Label, int Count)[]
        {
            ("Oluşturulan", statuses.Count),
            ("Gönderilen", statuses.Count(s => s != QuoteStatus.Draft)),
            ("Kabul edilen", statuses.Count(s => s is QuoteStatus.Accepted or QuoteStatus.Converted)),
            ("Sözleşmeye dönüşen", statuses.Count(s => s == QuoteStatus.Converted))
        };
        var open = await _db.Quotes.AsNoTracking().Where(q => q.Status == QuoteStatus.Sent || q.Status == QuoteStatus.Accepted).Select(q => q.GrandTotal).ToListAsync();
        var horizon = nowUtc.AddDays(14);
        var expiring = await _db.Quotes.AsNoTracking()
            .Where(q => q.Status == QuoteStatus.Sent && q.ValidUntilUtc >= nowUtc && q.ValidUntilUtc <= horizon)
            .OrderBy(q => q.ValidUntilUtc).Take(6)
            .Select(q => new ExpiringQuote(q.Id, q.Title, q.Customer!.Name, q.GrandTotal, q.ValidUntilUtc!.Value)).ToListAsync();
        return new DashboardPipeline(open.Count, open.Sum(), statuses.Count == 0 ? Array.Empty<BarRow>() : funnel.Select(x => new BarRow(x.Label, x.Count, Count(x.Count))).ToList(), expiring);
    }

    public async Task<DashboardFinance> FinanceAsync(ReportRange range, DateOnly today)
    {
        var (from, to) = (LocalDate(range.FromUtc), LocalDate(range.ToUtc).AddDays(-1));
        var profit = await _accounting.ProfitAsync(from, to);
        var previous = await _accounting.ProfitAsync(LocalDate(range.Previous.FromUtc), from.AddDays(-1));
        var balances = await _accounting.BalancesAsync();
        var accounts = await _accounting.AccountBalancesAsync();
        var open = await _accounting.OpenInvoicesAsync();
        var overdue = open.Where(x => x.DueDate < today && AccountingLabels.CariSign(x.Kind) > 0).ToList();
        var cheques = await _db.Cheques.AsNoTracking().Where(x => x.Status == ChequeStatus.Portfolio || x.Status == ChequeStatus.Deposited).Select(x => new { x.Direction, x.Amount }).ToListAsync();
        var stock = await _accounting.StockAsync();
        var flow = await _accounting.CashFlowAsync(today, today.AddDays(DueWindowDays));
        var aging = await _accounting.AgingAsync(true, today);

        var bands = new (string Label, decimal Value)[]
        {
            ("Vadesi gelmemiş", aging.Sum(x => x.Current)), ("1-30 gün", aging.Sum(x => x.Days30)), ("31-60 gün", aging.Sum(x => x.Days60)),
            ("61-90 gün", aging.Sum(x => x.Days90)), ("90 günden eski", aging.Sum(x => x.Older))
        };
        var debtors = balances.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Take(5).ToList();
        var debtorIds = debtors.Select(x => x.Key).ToList();
        var names = await _db.Customers.AsNoTracking().Where(x => debtorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);

        return new DashboardFinance(
            accounts.Where(x => x.Account.Currency == "TRY" && x.Account.IsActive).Sum(x => x.Balance), balances.Values.Where(x => x > 0).Sum(), -balances.Values.Where(x => x < 0).Sum(),
            overdue.Sum(x => x.Open), overdue.Count,
            profit.Revenue, previous.Revenue, profit.Costs, previous.Costs, profit.Profit, previous.Profit,
            cheques.Where(x => x.Direction == ChequeDirection.Received).Sum(x => x.Amount), cheques.Where(x => x.Direction == ChequeDirection.Given).Sum(x => x.Amount),
            stock.Count(x => x.Product.Kind == ProductKind.Stock && x.Product.IsActive && x.Quantity <= x.Product.CriticalStock),
            flow.Sum(x => x.In), flow.Sum(x => x.Out),
            await ChartAsync(range, from, to),
            bands.Any(x => x.Value > 0) ? bands.Select(x => new BarRow(x.Label, x.Value, TurkishNumbers.Money(x.Value))).ToList() : Array.Empty<BarRow>(),
            debtors.Select(x => new BarRow(names.GetValueOrDefault(x.Key, "-"), x.Value, TurkishNumbers.Money(x.Value))).ToList(),
            profit.ByCategory.Take(6).Select(x => new BarRow(x.Name, x.Amount, TurkishNumbers.Money(x.Amount))).ToList(),
            flow.Take(8).ToList());
    }

    // Tutarlar kâr-zarar raporuyla aynı kalemlerden gelir; iadeler düşülür.
    private async Task<TimeChart> ChartAsync(ReportRange range, DateOnly from, DateOnly to)
    {
        var invoices = await _db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved && x.Date >= from && x.Date <= to).Select(x => new { x.Kind, x.Date, x.Net, x.ExchangeRate }).ToListAsync();
        var expenses = await _db.Expenses.AsNoTracking().Where(x => !x.IsCancelled && x.Date >= from && x.Date <= to).Select(x => new { x.Date, x.Net }).ToListAsync();
        var salaries = await _db.CashTransactions.AsNoTracking().Where(x => !x.IsCancelled && x.Kind == CashKind.Salary && x.Date >= from && x.Date <= to).Select(x => new { x.Date, x.AmountTry }).ToListAsync();
        IEnumerable<(DateTime, decimal)> Lines(InvoiceKind kind, int sign) => invoices.Where(x => x.Kind == kind).Select(x => (Utc(x.Date), sign * InvoiceRules.Money(x.Net * x.ExchangeRate)));
        return ReportService.Chart(range, true, true,
            ("Gelir", Lines(InvoiceKind.Sale, 1).Concat(Lines(InvoiceKind.SaleReturn, -1))),
            ("Gider", Lines(InvoiceKind.Purchase, 1).Concat(Lines(InvoiceKind.PurchaseReturn, -1)).Concat(expenses.Select(x => (Utc(x.Date), x.Net))).Concat(salaries.Select(x => (Utc(x.Date), x.AmountTry)))));
    }
}
