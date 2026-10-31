using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Accounting;

public sealed record StatementLine(DateOnly Date, string Type, string Document, string? Description, decimal Debit, decimal Credit, decimal Balance, string? Url);
public sealed record OpenInvoice(int Id, InvoiceKind Kind, string? Number, int CustomerId, string CustomerName, DateOnly Date, DateOnly DueDate, decimal PayableTry, decimal Paid)
{
    public decimal Open => PayableTry - Paid;
}
public sealed record AccountBalance(FinanceAccount Account, decimal Balance);
public sealed record StockRow(Product Product, decimal Quantity, decimal Value);
public sealed record AgingRow(int CustomerId, string Name, decimal Current, decimal Days30, decimal Days60, decimal Days90, decimal Older)
{
    public decimal Total => Current + Days30 + Days60 + Days90 + Older;
}
public sealed record VatRow(string Period, decimal SaleNet, decimal SaleVat, decimal SaleWithholding, decimal PurchaseNet, decimal PurchaseVat, decimal PurchaseWithholding, decimal ExpenseVat)
{
    public decimal Deductible => PurchaseVat - PurchaseWithholding + ExpenseVat;
    public decimal Payable => SaleVat - SaleWithholding - Deductible;
}
public sealed record ProfitRow(decimal Sales, decimal SaleReturns, decimal Purchases, decimal PurchaseReturns, decimal Expenses, decimal Salaries, IReadOnlyList<(string Name, decimal Amount)> ByCategory)
{
    public decimal Revenue => Sales - SaleReturns;
    public decimal Costs => Purchases - PurchaseReturns + Expenses + Salaries;
    public decimal Profit => Revenue - Costs;
}
public sealed record CashFlowRow(DateOnly Date, string Type, string Document, string Party, decimal In, decimal Out, string? Url);

// Tüm bakiyeler hareketlerden hesaplanır, hiçbir yerde saklanmaz.
public sealed class AccountingQueries
{
    private readonly AppDbContext _db;
    public AccountingQueries(AppDbContext db) => _db = db;

    private static decimal Try(decimal amount, decimal rate) => InvoiceRules.Money(amount * rate);

    // Artı: cari bize borçlu. Eksi: biz cariye borçluyuz.
    public async Task<Dictionary<int, decimal>> BalancesAsync()
    {
        var balances = new Dictionary<int, decimal>();
        void Add(int id, decimal amount) => balances[id] = balances.GetValueOrDefault(id) + amount;
        foreach (var x in await _db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved).Select(x => new { x.CustomerId, x.Kind, x.PayableTry }).ToListAsync())
            Add(x.CustomerId, AccountingLabels.CariSign(x.Kind) * x.PayableTry);
        foreach (var x in await _db.CashTransactions.AsNoTracking().Where(x => !x.IsCancelled && x.CustomerId != null).Select(x => new { x.CustomerId, x.IsIn, x.AmountTry }).ToListAsync())
            Add(x.CustomerId!.Value, x.IsIn ? -x.AmountTry : x.AmountTry);
        foreach (var x in await _db.Cheques.AsNoTracking().Where(x => x.Status != ChequeStatus.Bounced && x.Status != ChequeStatus.Cancelled).Select(x => new { x.CustomerId, x.EndorsedToCustomerId, x.Direction, x.Amount }).ToListAsync())
        {
            Add(x.CustomerId, x.Direction == ChequeDirection.Received ? -x.Amount : x.Amount);
            if (x.EndorsedToCustomerId is not null) Add(x.EndorsedToCustomerId.Value, x.Amount);
        }
        foreach (var x in await _db.Expenses.AsNoTracking().Where(x => !x.IsCancelled && x.CustomerId != null && x.AccountId == null).Select(x => new { x.CustomerId, x.Total }).ToListAsync())
            Add(x.CustomerId!.Value, -x.Total);
        foreach (var x in await _db.CariAdjustments.AsNoTracking().Select(x => new { x.CustomerId, x.Amount }).ToListAsync())
            Add(x.CustomerId, x.Amount);
        return balances;
    }

    public async Task<List<StatementLine>> StatementAsync(int customerId, DateOnly? from = null, DateOnly? to = null)
    {
        var rows = new List<(DateOnly Date, int Order, string Type, string Doc, string? Note, decimal Debit, decimal Credit, string? Url)>();
        foreach (var x in await _db.CariAdjustments.AsNoTracking().Where(x => x.CustomerId == customerId).ToListAsync())
            rows.Add((x.Date, 0, "Açılış / düzeltme", "", x.Description, Math.Max(x.Amount, 0), Math.Max(-x.Amount, 0), null));
        foreach (var x in await _db.Invoices.AsNoTracking().Where(x => x.CustomerId == customerId && x.Status == InvoiceStatus.Approved).ToListAsync())
        {
            var debit = AccountingLabels.CariSign(x.Kind) > 0;
            rows.Add((x.Date, 1, AccountingLabels.Label(x.Kind), x.Number ?? "", x.Notes, debit ? x.PayableTry : 0, debit ? 0 : x.PayableTry, $"/yonetim/muhasebe/faturalar/detay/{x.Id}"));
        }
        foreach (var x in await _db.CashTransactions.AsNoTracking().Include(x => x.Account).Where(x => x.CustomerId == customerId && !x.IsCancelled).ToListAsync())
            rows.Add((x.Date, 2, AccountingLabels.Label(x.Kind), x.Account!.Name, x.Description, x.IsIn ? 0 : x.AmountTry, x.IsIn ? x.AmountTry : 0, null));
        foreach (var x in await _db.Cheques.AsNoTracking().Where(x => x.CustomerId == customerId || x.EndorsedToCustomerId == customerId).ToListAsync())
        {
            var doc = $"{AccountingLabels.Label(x.Kind)} {x.Number}";
            var url = $"/yonetim/muhasebe/cek-senet/detay/{x.Id}";
            if (x.EndorsedToCustomerId == customerId)
            {
                rows.Add((x.StatusDate ?? x.IssueDate, 3, "Ciro edilen belge", doc, null, x.Amount, 0, url));
                if (x.Status == ChequeStatus.Bounced) rows.Add((x.StatusDate ?? x.IssueDate, 4, "Karşılıksız", doc, null, 0, x.Amount, url));
                continue;
            }
            var received = x.Direction == ChequeDirection.Received;
            rows.Add((x.IssueDate, 3, received ? "Alınan çek/senet" : "Verilen çek/senet", doc, x.Notes, received ? 0 : x.Amount, received ? x.Amount : 0, url));
            // Geçmiş silinmez: karşılıksız ve iptal ters kayıt olarak görünür.
            if (x.Status is ChequeStatus.Bounced or ChequeStatus.Cancelled)
                rows.Add((x.StatusDate ?? x.IssueDate, 4, AccountingLabels.Label(x.Status), doc, null, received ? x.Amount : 0, received ? 0 : x.Amount, url));
        }
        foreach (var x in await _db.Expenses.AsNoTracking().Where(x => x.CustomerId == customerId && !x.IsCancelled && x.AccountId == null).ToListAsync())
            rows.Add((x.Date, 2, "Gider", x.DocumentNumber ?? "", x.Description, 0, x.Total, null));

        var balance = 0m;
        var lines = new List<StatementLine>();
        foreach (var row in rows.OrderBy(x => x.Date).ThenBy(x => x.Order))
        {
            balance += row.Debit - row.Credit;
            lines.Add(new StatementLine(row.Date, row.Type, row.Doc, row.Note, row.Debit, row.Credit, balance, row.Url));
        }
        var opening = from is null ? 0m : lines.LastOrDefault(x => x.Date < from)?.Balance ?? 0m;
        var visible = lines.Where(x => (from is null || x.Date >= from) && (to is null || x.Date <= to)).ToList();
        if (from is not null && opening != 0) visible.Insert(0, new StatementLine(from.Value, "Devreden bakiye", "", null, Math.Max(opening, 0), Math.Max(-opening, 0), opening, null));
        return visible;
    }

    public async Task<List<OpenInvoice>> OpenInvoicesAsync(int? customerId = null)
    {
        var query = _db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved);
        if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
        var rows = await query.Select(x => new OpenInvoice(x.Id, x.Kind, x.Number, x.CustomerId, x.Customer!.Name, x.Date, x.DueDate, x.PayableTry, x.Allocations.Sum(a => a.Amount))).ToListAsync();
        return rows.Where(x => x.Open > 0).OrderBy(x => x.DueDate).ToList();
    }

    public async Task<List<AccountBalance>> AccountBalancesAsync()
    {
        var totals = (await _db.CashTransactions.AsNoTracking().Where(x => !x.IsCancelled).Select(x => new { x.AccountId, x.IsIn, x.Amount }).ToListAsync())
            .GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.IsIn ? x.Amount : -x.Amount));
        return (await _db.FinanceAccounts.AsNoTracking().OrderBy(x => x.Kind).ThenBy(x => x.Name).ToListAsync())
            .Select(a => new AccountBalance(a, a.OpeningBalance + totals.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<List<StockRow>> StockAsync()
    {
        var quantities = (await _db.StockMovements.AsNoTracking().Select(x => new { x.ProductId, x.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return (await _db.Products.AsNoTracking().OrderBy(x => x.Name).ToListAsync())
            .Select(p => new StockRow(p, quantities.GetValueOrDefault(p.Id), InvoiceRules.Money(Math.Max(quantities.GetValueOrDefault(p.Id), 0) * p.PurchasePrice))).ToList();
    }

    public async Task<List<AgingRow>> AgingAsync(bool receivable, DateOnly today)
    {
        var kinds = receivable ? new[] { InvoiceKind.Sale, InvoiceKind.PurchaseReturn } : new[] { InvoiceKind.Purchase, InvoiceKind.SaleReturn };
        return (await OpenInvoicesAsync()).Where(x => kinds.Contains(x.Kind)).GroupBy(x => (x.CustomerId, x.CustomerName)).Select(g =>
        {
            decimal Band(Func<int, bool> match) => g.Where(x => match(today.DayNumber - x.DueDate.DayNumber)).Sum(x => x.Open);
            return new AgingRow(g.Key.CustomerId, g.Key.CustomerName, Band(d => d <= 0), Band(d => d is > 0 and <= 30), Band(d => d is > 30 and <= 60), Band(d => d is > 60 and <= 90), Band(d => d > 90));
        }).OrderByDescending(x => x.Total).ToList();
    }

    public async Task<List<VatRow>> VatAsync(int year)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var invoices = await _db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved && x.Date >= from && x.Date <= to)
            .Select(x => new { x.Date.Month, x.Kind, x.Net, x.VatTotal, x.WithholdingTotal, x.ExchangeRate }).ToListAsync();
        var expenses = await _db.Expenses.AsNoTracking().Where(x => !x.IsCancelled && x.Date >= from && x.Date <= to).Select(x => new { x.Date.Month, x.Vat }).ToListAsync();
        return Enumerable.Range(1, 12).Select(month =>
        {
            // İade, ilgili tarafın matrah ve KDV'sini azaltır.
            decimal Sum(Func<dynamic, decimal> pick, InvoiceKind main, InvoiceKind back)
                => invoices.Where(x => x.Month == month && x.Kind == main).Sum(x => Try(pick(x), x.ExchangeRate)) - invoices.Where(x => x.Month == month && x.Kind == back).Sum(x => Try(pick(x), x.ExchangeRate));
            return new VatRow($"{month:00}/{year}", Sum(x => x.Net, InvoiceKind.Sale, InvoiceKind.SaleReturn), Sum(x => x.VatTotal, InvoiceKind.Sale, InvoiceKind.SaleReturn), Sum(x => x.WithholdingTotal, InvoiceKind.Sale, InvoiceKind.SaleReturn),
                Sum(x => x.Net, InvoiceKind.Purchase, InvoiceKind.PurchaseReturn), Sum(x => x.VatTotal, InvoiceKind.Purchase, InvoiceKind.PurchaseReturn), Sum(x => x.WithholdingTotal, InvoiceKind.Purchase, InvoiceKind.PurchaseReturn),
                expenses.Where(x => x.Month == month).Sum(x => x.Vat));
        }).ToList();
    }

    public async Task<ProfitRow> ProfitAsync(DateOnly from, DateOnly to)
    {
        var invoices = await _db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.Approved && x.Date >= from && x.Date <= to).Select(x => new { x.Kind, x.Net, x.ExchangeRate }).ToListAsync();
        decimal Total(InvoiceKind kind) => invoices.Where(x => x.Kind == kind).Sum(x => Try(x.Net, x.ExchangeRate));
        var expenses = await _db.Expenses.AsNoTracking().Where(x => !x.IsCancelled && x.Date >= from && x.Date <= to).Select(x => new { x.Category!.Name, x.Net }).ToListAsync();
        var salaries = await _db.CashTransactions.AsNoTracking().Where(x => !x.IsCancelled && x.Kind == CashKind.Salary && x.Date >= from && x.Date <= to).SumAsync(x => x.AmountTry);
        return new ProfitRow(Total(InvoiceKind.Sale), Total(InvoiceKind.SaleReturn), Total(InvoiceKind.Purchase), Total(InvoiceKind.PurchaseReturn), expenses.Sum(x => x.Net), salaries,
            expenses.GroupBy(x => x.Name).Select(g => (g.Key, g.Sum(x => x.Net))).OrderByDescending(x => x.Item2).ToList());
    }

    // Vadesi gelecek faturalar ve portföydeki çek/senetler. Vadesi geçenler bugüne yazılır.
    public async Task<List<CashFlowRow>> CashFlowAsync(DateOnly today, DateOnly until)
    {
        var rows = new List<CashFlowRow>();
        foreach (var x in (await OpenInvoicesAsync()).Where(x => x.DueDate <= until))
        {
            var isIn = AccountingLabels.CariSign(x.Kind) > 0;
            rows.Add(new CashFlowRow(x.DueDate < today ? today : x.DueDate, AccountingLabels.Label(x.Kind), x.Number ?? "", x.CustomerName, isIn ? x.Open : 0, isIn ? 0 : x.Open, $"/yonetim/muhasebe/faturalar/detay/{x.Id}"));
        }
        var cheques = await _db.Cheques.AsNoTracking().Include(x => x.Customer).Where(x => x.DueDate <= until && (x.Status == ChequeStatus.Portfolio || x.Status == ChequeStatus.Deposited)).ToListAsync();
        foreach (var x in cheques)
        {
            var isIn = x.Direction == ChequeDirection.Received;
            rows.Add(new CashFlowRow(x.DueDate < today ? today : x.DueDate, AccountingLabels.Label(x.Kind), x.Number, x.Customer!.Name, isIn ? x.Amount : 0, isIn ? 0 : x.Amount, $"/yonetim/muhasebe/cek-senet/detay/{x.Id}"));
        }
        return rows.OrderBy(x => x.Date).ToList();
    }
}
