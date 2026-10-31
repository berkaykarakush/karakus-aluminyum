using System.Net;
using System.Text.Json;
using KarakusAluminyum.Web.Areas.Admin.Controllers;
using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services.Accounting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class InvoiceRulesTests
{
    private static Invoice Calc(decimal rate = 1m, params InvoiceLine[] lines)
    {
        var invoice = new Invoice { ExchangeRate = rate, Lines = lines.ToList() };
        InvoiceRules.Calculate(invoice);
        return invoice;
    }

    [Fact]
    public void Totals_ApplyDiscountThenVatThenWithholding()
    {
        // 2 x 100 = 200; %10 iskonto 20; matrah 180; %20 KDV 36; 5/10 tevkifat 18; ödenecek 198
        var x = Calc(lines: new InvoiceLine { Quantity = 2, UnitPrice = 100, DiscountRate = 10, VatRate = 20, WithholdingRate = 50 });
        Assert.Equal((20m, 180m, 36m, 18m, 216m, 198m, 198m), (x.DiscountTotal, x.Net, x.VatTotal, x.WithholdingTotal, x.GrandTotal, x.Payable, x.PayableTry));
    }

    [Fact]
    public void Vat_IsRoundedPerLine_AndTotalsAreLineSums()
    {
        // 0,05 x %20 = 0,01 satır başına: toplam 0,03. Toplam üzerinden hesap 0,03 olur, 3 x 0,33 x %10 = 0,099 → 0,10 x 3 ise 0,30.
        var x = Calc(lines: new[] { 1, 2, 3 }.Select(_ => new InvoiceLine { Quantity = 1, UnitPrice = 0.33m, VatRate = 10 }).ToArray());
        Assert.Equal((0.99m, 0.09m), (x.Net, x.VatTotal));
    }

    [Fact]
    public void ForeignInvoice_ConvertsPayableToTry()
    {
        var x = Calc(32.5m, new InvoiceLine { Quantity = 1, UnitPrice = 100, VatRate = 20 });
        Assert.Equal((120m, 3900m), (x.Payable, x.PayableTry));
    }

    [Theory]
    [InlineData(0, 100, 0, 20, 0)]
    [InlineData(1, -1, 0, 20, 0)]
    [InlineData(1, 100, 101, 20, 0)]
    [InlineData(1, 100, 0, 101, 0)]
    [InlineData(1, 100, 0, 20, 101)]
    public void InvalidLines_AreRejected(decimal quantity, decimal price, decimal discount, decimal vat, decimal withholding)
    {
        var line = new InvoiceLineInput { Description = "x", Quantity = quantity, UnitPrice = price, DiscountRate = discount, VatRate = vat, WithholdingRate = withholding };
        Assert.NotEmpty(InvoiceRules.Validate(new[] { line }, 1, "TRY", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData("TRY", 2, false)]
    [InlineData("USD", 0, false)]
    [InlineData("GBP", 1, false)]
    [InlineData("USD", 32.5, true)]
    [InlineData("TRY", 1, true)]
    public void Currency_AndRate_AreChecked(string currency, decimal rate, bool valid)
    {
        var line = new InvoiceLineInput { Description = "x", Quantity = 1, UnitPrice = 1 };
        Assert.Equal(valid, InvoiceRules.Validate(new[] { line }, rate, currency, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)).Count == 0);
    }

    [Fact]
    public void DueDateBeforeInvoiceDate_AndEmptyInvoice_AreRejected()
    {
        var line = new InvoiceLineInput { Description = "x", Quantity = 1, UnitPrice = 1 };
        Assert.NotEmpty(InvoiceRules.Validate(new[] { line }, 1, "TRY", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1)));
        Assert.NotEmpty(InvoiceRules.Validate(Array.Empty<InvoiceLineInput>(), 1, "TRY", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData(InvoiceKind.Sale, 1, -1)]
    [InlineData(InvoiceKind.Purchase, -1, 1)]
    [InlineData(InvoiceKind.SaleReturn, -1, 1)]
    [InlineData(InvoiceKind.PurchaseReturn, 1, -1)]
    public void Signs_FollowInvoiceKind(InvoiceKind kind, int cari, int stock)
        => Assert.Equal((cari, stock), (AccountingLabels.CariSign(kind), AccountingLabels.StockSign(kind)));

    [Theory]
    [InlineData(ChequeDirection.Received, ChequeStatus.Portfolio, ChequeStatus.Collected, true)]
    [InlineData(ChequeDirection.Received, ChequeStatus.Portfolio, ChequeStatus.Paid, false)]
    [InlineData(ChequeDirection.Received, ChequeStatus.Collected, ChequeStatus.Bounced, false)]
    [InlineData(ChequeDirection.Received, ChequeStatus.Bounced, ChequeStatus.Portfolio, false)]
    [InlineData(ChequeDirection.Received, ChequeStatus.Endorsed, ChequeStatus.Bounced, true)]
    [InlineData(ChequeDirection.Given, ChequeStatus.Portfolio, ChequeStatus.Paid, true)]
    [InlineData(ChequeDirection.Given, ChequeStatus.Portfolio, ChequeStatus.Endorsed, false)]
    public void ChequeTransitions_AreLimited(ChequeDirection direction, ChequeStatus from, ChequeStatus to, bool allowed)
        => Assert.Equal(allowed, InvoiceRules.CanTransition(direction, from, to));

    [Theory]
    [InlineData("1.250,50", 1250.50)]
    [InlineData("1250,50", 1250.50)]
    [InlineData("1250.50", 1250.50)]
    [InlineData("1,250.50", 1250.50)]
    [InlineData("1.250", 1250)]
    [InlineData("-300,00", -300)]
    [InlineData("1.250,50 TL", 1250.50)]
    [InlineData("₺99", 99)]
    public void ImportNumbers_ReadTurkishAndEnglishFormats(string raw, decimal expected)
    {
        Assert.True(ImportParser.TryNumber(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void ImportNumbers_RejectText(string raw) => Assert.False(ImportParser.TryNumber(raw, out _));
}

[Collection("postgres")]
public sealed class AccountingFlowTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/muhasebe/faturalar/ekle"));
    }

    private static string Tag() => "A" + Guid.NewGuid().ToString("N")[..8];

    private static Task<int> CustomerAsync(AppFactory app, PartyType type = PartyType.Customer) => app.WithDbAsync(async db =>
    {
        var row = new Customer { Name = "Cari " + Tag(), PartyType = type };
        db.Customers.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    });

    private static Task<int> ProductAsync(AppFactory app, ProductKind kind = ProductKind.Stock) => app.WithDbAsync(async db =>
    {
        var row = new Product { Name = "Ürün " + Tag(), Kind = kind, SalePrice = 100, PurchasePrice = 60 };
        db.Products.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    });

    private static Task<int> AccountAsync(AppFactory app, string currency = "TRY", decimal opening = 0, FinanceAccountKind kind = FinanceAccountKind.Bank) => app.WithDbAsync(async db =>
    {
        var row = new FinanceAccount { Name = "Hesap " + Tag(), Currency = currency, OpeningBalance = opening, Kind = kind };
        db.FinanceAccounts.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    });

    private static string Lines(params object[] lines) => JsonSerializer.Serialize(lines);
    private static object Line(decimal quantity = 1, decimal price = 1000, decimal vat = 20, int? product = null, decimal withholding = 0)
        => new { productId = product, description = "Kalem", quantity, unit = "Adet", unitPrice = price, discountRate = 0, vatRate = vat, withholdingRate = withholding };

    private static async Task<int> DraftAsync(AppFactory app, HttpClient client, string token, int customer, InvoiceKind kind = InvoiceKind.Sale, string? lines = null, string? number = null, DateOnly? date = null, DateOnly? due = null, string currency = "TRY", decimal rate = 1)
    {
        var mark = Tag();
        var response = await client.PostFormAsync("/yonetim/muhasebe/faturalar/ekle", token, ("Kind", kind.ToString()), ("CustomerId", customer.ToString()), ("Date", (date ?? Day).ToString("yyyy-MM-dd")), ("DueDate", (due ?? date ?? Day).ToString("yyyy-MM-dd")),
            ("Currency", currency), ("ExchangeRate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Number", number ?? ""), ("Notes", mark), ("LinesJson", lines ?? Lines(Line())));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await app.WithDbAsync(db => db.Invoices.Where(x => x.Notes == mark).Select(x => x.Id).SingleAsync());
    }

    private static async Task<int> ApprovedAsync(AppFactory app, HttpClient client, string token, int customer, InvoiceKind kind = InvoiceKind.Sale, string? lines = null, string? number = null, DateOnly? date = null, DateOnly? due = null, string currency = "TRY", decimal rate = 1)
    {
        var id = await DraftAsync(app, client, token, customer, kind, lines, number ?? (AccountingLabels.IsSaleSide(kind) ? null : Tag()), date, due, currency, rate);
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{id}", token);
        Assert.Equal(InvoiceStatus.Approved, await StatusAsync(app, id));
        return id;
    }

    private static Task<InvoiceStatus> StatusAsync(AppFactory app, int id) => app.WithDbAsync(db => db.Invoices.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
    private static Task<T> QueryAsync<T>(AppFactory app, Func<AccountingQueries, Task<T>> action) => app.WithServiceAsync(action);
    private static Task<decimal> BalanceAsync(AppFactory app, int customer) => QueryAsync(app, async q => (await q.BalancesAsync()).GetValueOrDefault(customer));
    private static Task<decimal> StockAsync(AppFactory app, int product) => QueryAsync(app, async q => (await q.StockAsync()).Single(x => x.Product.Id == product).Quantity);
    private static Task<decimal> CashAsync(AppFactory app, int account) => QueryAsync(app, async q => (await q.AccountBalancesAsync()).Single(x => x.Account.Id == account).Balance);
    private static Task<decimal> PaidAsync(AppFactory app, int invoice) => app.WithDbAsync(db => db.PaymentAllocations.Where(x => x.InvoiceId == invoice).SumAsync(x => x.Amount));

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, string token, string mode, int? customer, int account, decimal amount, int? invoice = null, decimal rate = 1, int? toAccount = null, string? description = null)
    {
        var fields = new List<(string, string)> { ("tur", mode), ("accountId", account.ToString()), ("date", Day.ToString("yyyy-MM-dd")), ("amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("exchangeRate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        if (customer is not null) fields.Add(("customerId", customer.ToString()!));
        if (invoice is not null) fields.Add(("invoiceId", invoice.ToString()!));
        if (toAccount is not null) fields.Add(("toAccountId", toAccount.ToString()!));
        if (description is not null) fields.Add(("description", description));
        return client.PostFormAsync("/yonetim/muhasebe/kasa-banka/yeni", token, fields.ToArray());
    }

    [Fact]
    public async Task Draft_HasNoEffect_ApprovalPostsCariStockAndNumber()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var product = await ProductAsync(app);
        var service = await ProductAsync(app, ProductKind.Service);

        var id = await DraftAsync(app, client, token, customer, lines: Lines(Line(3, 100, 20, product), Line(1, 50, 10, service)));
        Assert.Equal(0m, await BalanceAsync(app, customer));
        Assert.Equal(0m, await StockAsync(app, product));
        Assert.Null(await app.WithDbAsync(db => db.Invoices.Where(x => x.Id == id).Select(x => x.Number).SingleAsync()));

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{id}", token);
        var row = await app.WithDbAsync(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Matches("^FTR-2026-\\d{6}$", row.Number!);
        // 300 + %20 60; 50 + %10 5 = 415
        Assert.Equal((350m, 65m, 415m), (row.Net, row.VatTotal, row.PayableTry));
        Assert.Equal(415m, await BalanceAsync(app, customer));
        Assert.Equal(-3m, await StockAsync(app, product));
        Assert.Equal(0m, await StockAsync(app, service)); // hizmette stok tutulmaz
    }

    [Fact]
    public async Task ClientTotals_AreIgnored_ServerRecalculates()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var mark = Tag();
        await client.PostFormAsync("/yonetim/muhasebe/faturalar/ekle", token, ("Kind", "Sale"), ("CustomerId", customer.ToString()), ("Date", "2026-03-10"), ("DueDate", "2026-03-10"), ("Currency", "TRY"), ("ExchangeRate", "1"), ("Notes", mark),
            ("LinesJson", Lines(Line())), ("Payable", "1"), ("GrandTotal", "1"), ("Status", "Approved"), ("PayableTry", "1"));
        var row = await app.WithDbAsync(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Notes == mark));
        Assert.Equal((InvoiceStatus.Draft, 1200m), (row.Status, row.PayableTry));
    }

    [Fact]
    public async Task ApprovedInvoice_CannotBeEditedDeletedOrReapproved()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var product = await ProductAsync(app);
        var id = await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 1000, 20, product)));
        var number = await app.WithDbAsync(db => db.Invoices.Where(x => x.Id == id).Select(x => x.Number).SingleAsync());

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/duzenle/{id}", token, ("CustomerId", customer.ToString()), ("Date", "2026-03-10"), ("DueDate", "2026-03-10"), ("Currency", "TRY"), ("ExchangeRate", "1"), ("LinesJson", Lines(Line(1, 1))));
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/sil/{id}", token);
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{id}", token);

        var row = await app.WithDbAsync(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal((1200m, number), (row.PayableTry, row.Number));
        Assert.Equal(-1m, await StockAsync(app, product)); // ikinci onay stoktan düşmez
    }

    // Stok hareketi olmayan fatura: silmeyi veritabanı değil, uygulama kuralı engellemelidir.
    [Fact]
    public async Task ApprovedInvoiceWithoutStock_CannotBeDeleted()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var id = await ApprovedAsync(app, client, token, await CustomerAsync(app));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/muhasebe/faturalar/sil/{id}", token)).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Invoices.AnyAsync(x => x.Id == id)));
    }

    [Fact]
    public async Task DraftCanBeEdited_ButItsKindCannotChange_AndDraftCanBeDeleted()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var id = await DraftAsync(app, client, token, customer);

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/duzenle/{id}", token, ("Kind", "Purchase"), ("CustomerId", customer.ToString()), ("Date", "2026-03-10"), ("DueDate", "2026-03-10"), ("Currency", "TRY"), ("ExchangeRate", "1"), ("LinesJson", Lines(Line(2, 500))));
        var row = await app.WithDbAsync(db => db.Invoices.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == id));
        Assert.Equal((InvoiceKind.Sale, 1200m, 1), (row.Kind, row.PayableTry, row.Lines.Count));

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/sil/{id}", token);
        Assert.False(await app.WithDbAsync(db => db.Invoices.AnyAsync(x => x.Id == id)));
    }

    [Fact]
    public async Task InvalidInvoice_IsNotSaved()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        foreach (var (who, lines, due) in new[] { (customer.ToString(), "{bozuk", "2026-03-10"), (customer.ToString(), "[]", "2026-03-10"), ("999999", Lines(Line()), "2026-03-10"), (customer.ToString(), Lines(Line()), "2026-03-01"), (customer.ToString(), Lines(Line(product: 999999)), "2026-03-10") })
        {
            var mark = Tag();
            var response = await client.PostFormAsync("/yonetim/muhasebe/faturalar/ekle", token, ("Kind", "Sale"), ("CustomerId", who), ("Date", "2026-03-10"), ("DueDate", due), ("Currency", "TRY"), ("ExchangeRate", "1"), ("Notes", mark), ("LinesJson", lines));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.Invoices.AnyAsync(x => x.Notes == mark)));
        }
    }

    [Fact]
    public async Task Numbers_AreSequential_AndConcurrentApprovalsGetUniqueNumbers()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var ids = new List<int>();
        for (var i = 0; i < 8; i++) ids.Add(await DraftAsync(app, client, token, customer, date: new DateOnly(2031, 1, 1)));

        await Task.WhenAll(ids.Select(id => app.WithServiceAsync<AccountingService, bool>(async s => { await s.ApproveAsync(id); return true; })));

        var numbers = await app.WithDbAsync(db => db.Invoices.Where(x => ids.Contains(x.Id)).Select(x => x.Number!).ToListAsync());
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"FTR-2031-{i:000000}"), numbers.Order());
    }

    [Fact]
    public async Task PurchaseNeedsNumber_RejectsDuplicatesPerSupplier_AndAddsStock()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var supplier = await CustomerAsync(app, PartyType.Supplier);
        var other = await CustomerAsync(app, PartyType.Supplier);
        var product = await ProductAsync(app);

        var missing = await DraftAsync(app, client, token, supplier, InvoiceKind.Purchase);
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{missing}", token);
        Assert.Equal(InvoiceStatus.Draft, await StatusAsync(app, missing));

        var number = Tag();
        await ApprovedAsync(app, client, token, supplier, InvoiceKind.Purchase, Lines(Line(5, 100, 20, product)), number);
        Assert.Equal(-600m, await BalanceAsync(app, supplier));
        Assert.Equal(5m, await StockAsync(app, product));

        var duplicate = await DraftAsync(app, client, token, supplier, InvoiceKind.Purchase, number: number);
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{duplicate}", token);
        Assert.Equal(InvoiceStatus.Draft, await StatusAsync(app, duplicate));

        await ApprovedAsync(app, client, token, other, InvoiceKind.Purchase, number: number); // başka tedarikçide aynı numara olabilir
    }

    [Fact]
    public async Task Returns_ReverseCariAndStock_AndUseTheirOwnSequence()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app, PartyType.Both);
        var product = await ProductAsync(app);

        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(10, 100, 20, product)));
        var back = await ApprovedAsync(app, client, token, customer, InvoiceKind.SaleReturn, Lines(Line(2, 100, 20, product)));
        Assert.Equal(960m, await BalanceAsync(app, customer));
        Assert.Equal(-8m, await StockAsync(app, product));
        Assert.StartsWith("IAD-2026-", await app.WithDbAsync(db => db.Invoices.Where(x => x.Id == back).Select(x => x.Number!).SingleAsync()));

        await ApprovedAsync(app, client, token, customer, InvoiceKind.Purchase, Lines(Line(4, 100, 20, product)));
        await ApprovedAsync(app, client, token, customer, InvoiceKind.PurchaseReturn, Lines(Line(1, 100, 20, product)));
        Assert.Equal(960m - 480m + 120m, await BalanceAsync(app, customer));
        Assert.Equal(-5m, await StockAsync(app, product));
    }

    [Fact]
    public async Task Withholding_IsNotCollected_ButVatStaysOnTheInvoice()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 1000, 20, withholding: 50)));
        Assert.Equal(1100m, await BalanceAsync(app, customer)); // 1000 + 200 - 100
    }

    [Fact]
    public async Task Collection_ClosesOldestInvoiceFirst_LeavesRemainderOnCari_AndCancelRestoresAll()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var account = await AccountAsync(app, opening: 100);
        var later = await ApprovedAsync(app, client, token, customer, due: new DateOnly(2026, 6, 1));
        var older = await ApprovedAsync(app, client, token, customer, due: new DateOnly(2026, 4, 1));

        await PayAsync(client, token, "tahsilat", customer, account, 1500);
        Assert.Equal((1200m, 300m), (await PaidAsync(app, older), await PaidAsync(app, later)));
        Assert.Equal(900m, await BalanceAsync(app, customer));
        Assert.Equal(1600m, await CashAsync(app, account));

        await PayAsync(client, token, "tahsilat", customer, account, 1000);
        Assert.Equal(1200m, await PaidAsync(app, later));
        Assert.Equal(-100m, await BalanceAsync(app, customer)); // fazla tahsilat alacak kalır

        var first = await app.WithDbAsync(db => db.CashTransactions.Where(x => x.CustomerId == customer).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync());
        await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/iptal/{first}", token, ("reason", "Hatalı giriş"));
        Assert.Equal(1400m, await BalanceAsync(app, customer));
        Assert.Equal(1100m, await CashAsync(app, account));
        Assert.Equal(0m, await PaidAsync(app, older));
    }

    [Fact]
    public async Task Collection_ToChosenInvoice_IsLimitedToThatInvoiceAndCustomer()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var other = await CustomerAsync(app);
        var account = await AccountAsync(app);
        var older = await ApprovedAsync(app, client, token, customer, due: new DateOnly(2026, 4, 1));
        var chosen = await ApprovedAsync(app, client, token, customer, due: new DateOnly(2026, 6, 1));
        var foreign = await ApprovedAsync(app, client, token, other);

        await PayAsync(client, token, "tahsilat", customer, account, 500, chosen);
        Assert.Equal((0m, 500m), (await PaidAsync(app, older), await PaidAsync(app, chosen)));

        var rejected = await PayAsync(client, token, "tahsilat", customer, account, 500, foreign);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(0m, await PaidAsync(app, foreign));
        Assert.Equal(500m, await CashAsync(app, account)); // işlem bütünüyle geri alınır
    }

    [Fact]
    public async Task Payment_ClosesPurchaseInvoices_NotSales()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var both = await CustomerAsync(app, PartyType.Both);
        var account = await AccountAsync(app, opening: 5000);
        var sale = await ApprovedAsync(app, client, token, both);
        var purchase = await ApprovedAsync(app, client, token, both, InvoiceKind.Purchase);

        await PayAsync(client, token, "odeme", both, account, 1200);
        Assert.Equal((0m, 1200m), (await PaidAsync(app, sale), await PaidAsync(app, purchase)));
        Assert.Equal(1200m, await BalanceAsync(app, both));
        Assert.Equal(3800m, await CashAsync(app, account));
    }

    [Fact]
    public async Task InvalidPayments_AreRejected()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var account = await AccountAsync(app);
        var passive = await app.WithDbAsync(async db =>
        {
            var row = new FinanceAccount { Name = "Pasif " + Tag(), IsActive = false };
            db.FinanceAccounts.Add(row);
            await db.SaveChangesAsync();
            return row.Id;
        });
        await PayAsync(client, token, "tahsilat", customer, account, 0);
        await PayAsync(client, token, "tahsilat", customer, account, -5);
        await PayAsync(client, token, "tahsilat", null, account, 100);
        await PayAsync(client, token, "tahsilat", 999999, account, 100);
        await PayAsync(client, token, "tahsilat", customer, passive, 100);
        await PayAsync(client, token, "giris", null, account, 100); // açıklamasız
        Assert.Equal(0m, await CashAsync(app, account));
        Assert.Equal(0m, await CashAsync(app, passive));
        Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(client, token, "uydurma", null, account, 100)).StatusCode);
    }

    [Fact]
    public async Task CancelledInvoice_LeavesCariAndReversesStock_ButIsBlockedWhilePaid()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var product = await ProductAsync(app);
        var account = await AccountAsync(app);
        var id = await ApprovedAsync(app, client, token, customer, lines: Lines(Line(2, 1000, 20, product)));
        await PayAsync(client, token, "tahsilat", customer, account, 100);

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/iptal/{id}", token, ("reason", "Yanlış"));
        Assert.Equal(InvoiceStatus.Approved, await StatusAsync(app, id));

        var cash = await app.WithDbAsync(db => db.CashTransactions.Where(x => x.CustomerId == customer).Select(x => x.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/iptal/{cash}", token, ("reason", "Yanlış"));

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/iptal/{id}", token); // gerekçesiz
        Assert.Equal(InvoiceStatus.Approved, await StatusAsync(app, id));

        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/iptal/{id}", token, ("reason", "Yanlış"));
        Assert.Equal(InvoiceStatus.Cancelled, await StatusAsync(app, id));
        Assert.Equal(0m, await BalanceAsync(app, customer));
        Assert.Equal(0m, await StockAsync(app, product));
        Assert.Equal(2, await app.WithDbAsync(db => db.StockMovements.CountAsync(x => x.InvoiceId == id))); // geçmiş silinmez

        var number = await app.WithDbAsync(db => db.Invoices.Where(x => x.Id == id).Select(x => x.Number).SingleAsync());
        Assert.NotNull(number);
    }

    [Fact]
    public async Task ForeignCurrency_PostsCariInTry_AndForeignAccountKeepsItsOwnBalance()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var usd = await AccountAsync(app, "USD");
        var id = await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 100, 20)), currency: "USD", rate: 30);
        Assert.Equal(3600m, await BalanceAsync(app, customer));

        await PayAsync(client, token, "tahsilat", customer, usd, 60, rate: 32);
        Assert.Equal(60m, await CashAsync(app, usd));
        Assert.Equal(1920m, await PaidAsync(app, id));
        Assert.Equal(1680m, await BalanceAsync(app, customer));

        var noRate = await CustomerAsync(app);
        await PayAsync(client, token, "tahsilat", noRate, usd, 10, rate: 0);
        Assert.Equal(60m, await CashAsync(app, usd));

        // TL hesapta gönderilen kur yok sayılır.
        var tl = await AccountAsync(app);
        await PayAsync(client, token, "tahsilat", noRate, tl, 10, rate: 99);
        Assert.Equal(-10m, await BalanceAsync(app, noRate));
    }

    [Fact]
    public async Task Transfer_MovesMoney_RequiresSameCurrency_AndCancelsBothLegs()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var from = await AccountAsync(app, opening: 1000);
        var to = await AccountAsync(app);
        var usd = await AccountAsync(app, "USD");

        await PayAsync(client, token, "virman", null, from, 400, toAccount: to);
        Assert.Equal((600m, 400m), (await CashAsync(app, from), await CashAsync(app, to)));

        await PayAsync(client, token, "virman", null, from, 100, toAccount: usd);
        await PayAsync(client, token, "virman", null, from, 100, toAccount: from);
        Assert.Equal(600m, await CashAsync(app, from));

        var leg = await app.WithDbAsync(db => db.CashTransactions.Where(x => x.AccountId == to).Select(x => x.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/iptal/{leg}", token, ("reason", "Yanlış"));
        Assert.Equal((1000m, 0m), (await CashAsync(app, from), await CashAsync(app, to)));
    }

    private static async Task<int> ChequeAsync(AppFactory app, HttpClient client, string token, int customer, ChequeDirection direction, decimal amount = 1200)
    {
        var number = Tag();
        await client.PostFormAsync("/yonetim/muhasebe/cek-senet/ekle", token, ("Direction", direction.ToString()), ("Kind", "Cheque"), ("Number", number), ("CustomerId", customer.ToString()), ("IssueDate", "2026-03-10"), ("DueDate", "2026-05-10"), ("Amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return await app.WithDbAsync(db => db.Cheques.Where(x => x.Number == number).Select(x => x.Id).SingleAsync());
    }

    private static Task SetChequeAsync(HttpClient client, string token, int id, ChequeStatus status, int? account = null, int? endorsed = null)
    {
        var fields = new List<(string, string)> { ("status", status.ToString()), ("date", "2026-05-10") };
        if (account is not null) fields.Add(("accountId", account.ToString()!));
        if (endorsed is not null) fields.Add(("endorsedTo", endorsed.ToString()!));
        return client.PostFormAsync($"/yonetim/muhasebe/cek-senet/durum/{id}", token, fields.ToArray());
    }

    private static Task<ChequeStatus> ChequeStatusAsync(AppFactory app, int id) => app.WithDbAsync(db => db.Cheques.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());

    [Fact]
    public async Task ReceivedCheque_ClosesInvoice_CollectionHitsBankOnce()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var bank = await AccountAsync(app);
        var invoice = await ApprovedAsync(app, client, token, customer);
        var cheque = await ChequeAsync(app, client, token, customer, ChequeDirection.Received);
        Assert.Equal(1200m, await PaidAsync(app, invoice));
        Assert.Equal(0m, await BalanceAsync(app, customer));

        await SetChequeAsync(client, token, cheque, ChequeStatus.Deposited, bank);
        Assert.Equal(0m, await CashAsync(app, bank));
        await SetChequeAsync(client, token, cheque, ChequeStatus.Collected);
        Assert.Equal(1200m, await CashAsync(app, bank));
        Assert.Equal(0m, await BalanceAsync(app, customer)); // tahsil cariyi ikinci kez etkilemez

        await SetChequeAsync(client, token, cheque, ChequeStatus.Bounced); // tahsil edilmişse olamaz
        Assert.Equal(ChequeStatus.Collected, await ChequeStatusAsync(app, cheque));
    }

    [Fact]
    public async Task BouncedCheque_ReopensInvoice_AndStatementShowsBothLines()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var invoice = await ApprovedAsync(app, client, token, customer);
        var cheque = await ChequeAsync(app, client, token, customer, ChequeDirection.Received);

        await SetChequeAsync(client, token, cheque, ChequeStatus.Bounced);
        Assert.Equal(0m, await PaidAsync(app, invoice));
        Assert.Equal(1200m, await BalanceAsync(app, customer));

        var lines = await QueryAsync(app, q => q.StatementAsync(customer));
        Assert.Contains(lines, x => x.Type == "Alınan çek/senet" && x.Credit == 1200);
        Assert.Contains(lines, x => x.Type == "Karşılıksız" && x.Debit == 1200);
        Assert.Equal(1200m, lines[^1].Balance);
    }

    [Fact]
    public async Task EndorsedCheque_PaysSupplier_AndBounceRestoresBothSides()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var supplier = await CustomerAsync(app, PartyType.Supplier);
        var purchase = await ApprovedAsync(app, client, token, supplier, InvoiceKind.Purchase);
        var cheque = await ChequeAsync(app, client, token, customer, ChequeDirection.Received);
        Assert.Equal(-1200m, await BalanceAsync(app, customer));

        await SetChequeAsync(client, token, cheque, ChequeStatus.Endorsed, endorsed: customer); // kendisine ciro edilemez
        Assert.Equal(ChequeStatus.Portfolio, await ChequeStatusAsync(app, cheque));

        await SetChequeAsync(client, token, cheque, ChequeStatus.Endorsed, endorsed: supplier);
        Assert.Equal(0m, await BalanceAsync(app, supplier));
        Assert.Equal(1200m, await PaidAsync(app, purchase));

        await SetChequeAsync(client, token, cheque, ChequeStatus.Bounced);
        Assert.Equal((0m, -1200m), (await BalanceAsync(app, customer), await BalanceAsync(app, supplier)));
        Assert.Equal(0m, await PaidAsync(app, purchase));
        var lines = await QueryAsync(app, q => q.StatementAsync(supplier));
        Assert.Equal(-1200m, lines[^1].Balance);
    }

    [Fact]
    public async Task GivenCheque_IsPaidFromBank()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var supplier = await CustomerAsync(app, PartyType.Supplier);
        var bank = await AccountAsync(app, opening: 2000);
        await ApprovedAsync(app, client, token, supplier, InvoiceKind.Purchase);
        var cheque = await ChequeAsync(app, client, token, supplier, ChequeDirection.Given);
        Assert.Equal(0m, await BalanceAsync(app, supplier));
        Assert.Equal(2000m, await CashAsync(app, bank)); // vade gelene kadar çıkmaz

        await SetChequeAsync(client, token, cheque, ChequeStatus.Paid); // hesap seçilmedi
        Assert.Equal(ChequeStatus.Portfolio, await ChequeStatusAsync(app, cheque));
        await SetChequeAsync(client, token, cheque, ChequeStatus.Paid, bank);
        Assert.Equal(800m, await CashAsync(app, bank));
        Assert.Equal(0m, await BalanceAsync(app, supplier));

        var movement = await app.WithDbAsync(db => db.CashTransactions.Where(x => x.ChequeId == cheque).Select(x => x.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/iptal/{movement}", token, ("reason", "x"));
        Assert.Equal(800m, await CashAsync(app, bank)); // çek hareketi tek başına iptal edilemez
    }

    private static async Task<int> CategoryAsync(AppFactory app) => await app.WithDbAsync(db => db.ExpenseCategories.Select(x => x.Id).FirstAsync());

    private static Task<HttpResponseMessage> ExpenseAsync(HttpClient client, string token, int category, decimal net, int? account, int? supplier, string? description = null, string date = "2026-03-10", decimal vat = 20)
    {
        var fields = new List<(string, string)> { ("CategoryId", category.ToString()), ("Description", description ?? Tag()), ("Date", date), ("DueDate", date), ("Net", net.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("VatRate", vat.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        if (account is not null) fields.Add(("AccountId", account.ToString()!));
        if (supplier is not null) fields.Add(("CustomerId", supplier.ToString()!));
        return client.PostFormAsync("/yonetim/muhasebe/giderler/ekle", token, fields.ToArray());
    }

    [Fact]
    public async Task Expense_PaidFromAccount_OrOnCredit_AndCancelRestores()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var category = await CategoryAsync(app);
        var account = await AccountAsync(app, opening: 1000);
        var supplier = await CustomerAsync(app, PartyType.Supplier);
        var paid = Tag();

        await ExpenseAsync(client, token, category, 100, account, null, paid);
        Assert.Equal(880m, await CashAsync(app, account));
        await ExpenseAsync(client, token, category, 100, null, supplier);
        Assert.Equal(-120m, await BalanceAsync(app, supplier));
        await ExpenseAsync(client, token, category, 100, account, supplier); // peşin ödendiyse cariye yazılmaz
        Assert.Equal(-120m, await BalanceAsync(app, supplier));
        Assert.Equal(760m, await CashAsync(app, account));

        var before = await app.WithDbAsync(db => db.Expenses.CountAsync());
        Assert.Equal(HttpStatusCode.OK, (await ExpenseAsync(client, token, category, 100, null, null)).StatusCode); // ne hesap ne tedarikçi
        Assert.Equal(HttpStatusCode.OK, (await ExpenseAsync(client, token, 999999, 100, account, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ExpenseAsync(client, token, category, 0, account, null)).StatusCode);
        Assert.Equal(before, await app.WithDbAsync(db => db.Expenses.CountAsync()));

        var id = await app.WithDbAsync(db => db.Expenses.Where(x => x.Description == paid).Select(x => x.Id).SingleAsync());
        var leg = await app.WithDbAsync(db => db.CashTransactions.Where(x => x.ExpenseId == id).Select(x => x.Id).SingleAsync());
        await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/iptal/{leg}", token, ("reason", "x")); // giderden iptal edilir
        Assert.Equal(760m, await CashAsync(app, account));
        await client.PostFormAsync($"/yonetim/muhasebe/giderler/iptal/{id}", token, ("reason", "Yanlış"));
        Assert.Equal(880m, await CashAsync(app, account));
    }

    [Fact]
    public async Task Salary_IsPaidFromAccount_AndAppearsInProfit()
    {
        await using var app = postgres.CreateApp("accounting_salary");
        var (client, token) = await AdminAsync(app);
        var account = await AccountAsync(app, opening: 50000);
        await client.PostFormAsync("/yonetim/muhasebe/personel/ekle", token, ("Name", "Personel Bir"), ("NetSalary", "20000"), ("IsActive", "true"));
        var employee = await app.WithDbAsync(db => db.Employees.Select(x => x.Id).SingleAsync());
        await client.PostFormAsync("/yonetim/muhasebe/personel/odeme", token, ("employeeId", employee.ToString()), ("accountId", account.ToString()), ("date", "2026-03-31"), ("amount", "20000"));
        await client.PostFormAsync("/yonetim/muhasebe/personel/odeme", token, ("employeeId", employee.ToString()), ("accountId", account.ToString()), ("date", "2026-04-01"), ("amount", "0"));
        Assert.Equal(30000m, await CashAsync(app, account));
        Assert.Equal(20000m, (await QueryAsync(app, q => q.ProfitAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)))).Salaries);
        Assert.Contains("Personel Bir", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/muhasebe/personel")));
    }

    [Fact]
    public async Task Statement_RunsInDateOrder_WithOpeningBalanceAndPeriod()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var account = await AccountAsync(app);
        await client.PostFormAsync($"/yonetim/muhasebe/cariler/{customer}/duzeltme", token, ("date", "2026-01-01"), ("amount", "500"), ("description", "Açılış"));
        await client.PostFormAsync($"/yonetim/muhasebe/cariler/{customer}/duzeltme", token, ("date", "2026-01-01"), ("amount", "0"), ("description", "Boş"));
        await ApprovedAsync(app, client, token, customer, date: new DateOnly(2026, 2, 1));
        await PayAsync(client, token, "tahsilat", customer, account, 700);

        var all = await QueryAsync(app, q => q.StatementAsync(customer));
        Assert.Equal(new[] { 500m, 1700m, 1000m }, all.Select(x => x.Balance));
        Assert.Equal(1000m, await BalanceAsync(app, customer));

        var period = await QueryAsync(app, q => q.StatementAsync(customer, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        Assert.Equal("Devreden bakiye", period[0].Type);
        Assert.Equal(new[] { 500m, 1700m }, period.Select(x => x.Balance));

        var csv = await client.GetByteArrayAsync($"/yonetim/muhasebe/cariler/{customer}?csv=true");
        Assert.Contains("Açılış", System.Text.Encoding.UTF8.GetString(csv));
        Assert.Contains("1000", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/cariler/{customer}")).Replace(".", ""));
    }

    // Tutarlar elle hesaplanmıştır: raporlar yalnızca bu veritabanındaki kayıtları görür.
    [Fact]
    public async Task Reports_MatchHandCalculatedNumbers()
    {
        await using var app = postgres.CreateApp("accounting_reports");
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var supplier = await CustomerAsync(app, PartyType.Supplier);
        var category = await CategoryAsync(app);
        var account = await AccountAsync(app, opening: 10000);
        var march = new DateOnly(2026, 3, 15);

        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 10000, 20)), date: march, due: new DateOnly(2026, 3, 20));                        // satış 10000 + 2000
        await ApprovedAsync(app, client, token, customer, InvoiceKind.SaleReturn, Lines(Line(1, 1000, 20)), date: march);                                         // iade -1000, -200
        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 2000, 20, withholding: 50)), date: march, due: new DateOnly(2026, 3, 20));         // 2000 + 400, tevkifat 200
        await ApprovedAsync(app, client, token, supplier, InvoiceKind.Purchase, Lines(Line(1, 3000, 20)), date: march, due: new DateOnly(2026, 8, 1));            // alış 3000 + 600
        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 100, 20, withholding: 0)), currency: "USD", rate: 30, date: march, due: new DateOnly(2026, 3, 20)); // 3000 + 600 TL
        var cancelled = await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 99999)), date: march);
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/iptal/{cancelled}", token, ("reason", "x"));
        await DraftAsync(app, client, token, customer, lines: Lines(Line(1, 88888)), date: march);
        await ExpenseAsync(client, token, category, 500, account, null, date: "2026-03-20");                                                                     // gider 500 + 100
        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 7777)), date: new DateOnly(2026, 4, 1));                                           // başka dönem

        var vat = (await QueryAsync(app, q => q.VatAsync(2026)))[2];
        Assert.Equal(14000m, vat.SaleNet);        // 10000 - 1000 + 2000 + 3000
        Assert.Equal(2800m, vat.SaleVat);         // 2000 - 200 + 400 + 600
        Assert.Equal(200m, vat.SaleWithholding);
        Assert.Equal((3000m, 600m, 100m), (vat.PurchaseNet, vat.PurchaseVat, vat.ExpenseVat));
        Assert.Equal(700m, vat.Deductible);
        Assert.Equal(1900m, vat.Payable);         // 2800 - 200 - 700

        var profit = await QueryAsync(app, q => q.ProfitAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        Assert.Equal(14000m, profit.Revenue);
        Assert.Equal(3500m, profit.Costs);
        Assert.Equal(10500m, profit.Profit);

        // Bugün 2026-04-25 kabul edilir: 20 Mart vadeli üç fatura 36 gün gecikmiştir.
        var aging = Assert.Single(await QueryAsync(app, q => q.AgingAsync(true, new DateOnly(2026, 4, 25))), x => x.CustomerId == customer);
        Assert.Equal(12000m + 2200m + 3600m, aging.Days60);
        Assert.Equal(0m, aging.Older);
        var notDue = Assert.Single(await QueryAsync(app, q => q.AgingAsync(false, new DateOnly(2026, 4, 25))), x => x.CustomerId == supplier);
        Assert.Equal((supplier, 3600m), (notDue.CustomerId, notDue.Current));

        var flow = await QueryAsync(app, q => q.CashFlowAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        Assert.Contains(flow, x => x.Out == 3600m && x.Party.StartsWith("Cari"));
        Assert.All(flow, x => Assert.True(x.Date >= new DateOnly(2026, 8, 1)));

        foreach (var page in new[] { "/yonetim/muhasebe", "/yonetim/muhasebe/raporlar/kdv?yil=2026", "/yonetim/muhasebe/raporlar/kar-zarar", "/yonetim/muhasebe/raporlar/yaslandirma", "/yonetim/muhasebe/raporlar/yaslandirma?tur=borc", "/yonetim/muhasebe/raporlar/nakit-akisi", "/yonetim/muhasebe/raporlar/kdv?csv=true", "/yonetim/muhasebe/raporlar/yaslandirma?csv=true", "/yonetim/muhasebe/raporlar/nakit-akisi?csv=true", "/yonetim/muhasebe/faturalar", "/yonetim/muhasebe/faturalar?acik=true&tur=Sale", "/yonetim/muhasebe/cariler", "/yonetim/muhasebe/cariler?bakiye=borclu", "/yonetim/muhasebe/kasa-banka", $"/yonetim/muhasebe/kasa-banka/hareketler/{account}", "/yonetim/muhasebe/kasa-banka/yeni?tur=virman", $"/yonetim/muhasebe/kasa-banka/yeni?tur=tahsilat&cari={customer}", "/yonetim/muhasebe/cek-senet", "/yonetim/muhasebe/cek-senet/ekle", "/yonetim/muhasebe/giderler", "/yonetim/muhasebe/giderler/ekle", "/yonetim/muhasebe/urunler", "/yonetim/muhasebe/urunler/ekle", "/yonetim/muhasebe/personel", "/yonetim/muhasebe/aktar", $"/yonetim/muhasebe/faturalar/yazdir/{cancelled}" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode);
    }

    [Fact]
    public async Task Quote_BecomesInvoiceDraft_WithPricesExcludingVat()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var id = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "Q-" + Tag(), Title = "T", CustomerId = customer, Status = QuoteStatus.Accepted, TaxRate = 20, VatMode = VatMode.Included, DiscountRate = 10, Items = { new QuoteItem { Description = "Doğrama", Quantity = 2, Unit = "Adet", UnitPrice = 1200 } } };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });
        Assert.Contains($"faturalar/ekle?teklif={id}", await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{id}"));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/muhasebe/faturalar/ekle?teklif={id}"));
        Assert.Contains("\"unitPrice\":1000", html); // 1200 KDV dahil
        Assert.Contains("\"discountRate\":10", html);
        Assert.Matches($"<option selected=\"selected\" value=\"{customer}\"", html);
    }

    [Fact]
    public async Task CustomerWithAccounting_AndProductWithMovements_CannotBeDeleted()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var customer = await CustomerAsync(app);
        var product = await ProductAsync(app);
        await ApprovedAsync(app, client, token, customer, lines: Lines(Line(1, 100, 20, product)));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/yonetim/crm/musteriler/sil/{customer}", token)).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Customers.AnyAsync(x => x.Id == customer)));
        await client.PostFormAsync($"/yonetim/muhasebe/urunler/sil/{product}", token);
        Assert.True(await app.WithDbAsync(db => db.Products.AnyAsync(x => x.Id == product)));

        var free = await ProductAsync(app);
        await client.PostFormAsync($"/yonetim/muhasebe/urunler/sil/{free}", token);
        Assert.False(await app.WithDbAsync(db => db.Products.AnyAsync(x => x.Id == free)));
    }

    [Fact]
    public async Task StockAdjustment_AddsMovement_AndCriticalFilterFindsIt()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var product = await ProductAsync(app);
        await client.PostFormAsync($"/yonetim/muhasebe/urunler/stok/{product}", token, ("quantity", "12.5"), ("date", "2026-03-10"), ("note", "Sayım"));
        Assert.Equal(12.5m, await StockAsync(app, product));
        var service = await ProductAsync(app, ProductKind.Service);
        await client.PostFormAsync($"/yonetim/muhasebe/urunler/stok/{service}", token, ("quantity", "5"), ("date", "2026-03-10"));
        Assert.Equal(0m, await StockAsync(app, service));

        var name = await app.WithDbAsync(db => db.Products.Where(x => x.Id == product).Select(x => x.Name).SingleAsync());
        Assert.DoesNotContain(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/muhasebe/urunler?kritik=true")));
        await app.WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(x => x.Id == product)).CriticalStock = 20;
            return await db.SaveChangesAsync();
        });
        Assert.Contains(name, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/muhasebe/urunler?kritik=true")));
    }

    [Fact]
    public async Task Account_CurrencyIsLocked_AfterMovements()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var account = await AccountAsync(app);
        var customer = await CustomerAsync(app);
        await PayAsync(client, token, "tahsilat", customer, account, 10);
        var name = await app.WithDbAsync(db => db.FinanceAccounts.Where(x => x.Id == account).Select(x => x.Name).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync($"/yonetim/muhasebe/kasa-banka/hesap/duzenle/{account}", token, ("Name", name), ("Kind", "Bank"), ("Currency", "USD"), ("IsActive", "true"))).StatusCode);
        Assert.Equal("TRY", await app.WithDbAsync(db => db.FinanceAccounts.Where(x => x.Id == account).Select(x => x.Currency).SingleAsync()));
    }

    [Fact]
    public async Task Import_AddsCustomersWithOpeningBalance_SkipsKnownOnes_AndIsAllOrNothing()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var a = Tag(); var b = Tag(); var c = Tag();
        var table = $"Unvan\tVergi No\tTelefon\tTür\tBakiye\n{a}\t1111111111\t0212\tMüşteri\t1.250,50\n{b}\t22222222222\t\tTedarikçi\t-300,00\n{a}\t1111111111\t\t\t999";
        await client.PostFormAsync("/yonetim/muhasebe/aktar", token, ("tip", "cari"), ("text", table), ("openingDate", "2026-01-01"));

        var first = await app.WithDbAsync(db => db.Customers.AsNoTracking().SingleAsync(x => x.Name == a));
        var second = await app.WithDbAsync(db => db.Customers.AsNoTracking().SingleAsync(x => x.Name == b));
        Assert.Equal((CustomerKind.Company, PartyType.Customer), (first.Kind, first.PartyType));
        Assert.Equal((CustomerKind.Individual, PartyType.Supplier), (second.Kind, second.PartyType));
        Assert.Equal((1250.50m, -300m), (await BalanceAsync(app, first.Id), await BalanceAsync(app, second.Id)));

        var bad = await client.PostFormAsync("/yonetim/muhasebe/aktar", token, ("tip", "cari"), ("text", $"Unvan;Bakiye\n{c};abc\n{Tag()};10"));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("2. satır", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        Assert.False(await app.WithDbAsync(db => db.Customers.AnyAsync(x => x.Name == c)));

        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync("/yonetim/muhasebe/aktar", token, ("tip", "cari"), ("text", "Telefon\n0212"))).StatusCode); // ad sütunu yok
    }

    [Fact]
    public async Task Import_AddsProductsWithOpeningStock()
    {
        var app = postgres.Shared;
        var (client, token) = await AdminAsync(app);
        var name = Tag();
        await client.PostFormAsync("/yonetim/muhasebe/aktar", token, ("tip", "urun"), ("text", $"Ürün Adı\tKod\tBirim\tAlış Fiyatı\tSatış Fiyatı\tKDV\tStok\n{name}\tK1\tMetre\t1.000,50\t1.500\t%10\t25"), ("openingDate", "2026-01-01"));
        var row = await app.WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(x => x.Name == name));
        Assert.Equal(("K1", "Metre", 1000.50m, 1500m, 10m), (row.Code, row.Unit, row.PurchasePrice, row.SalePrice, row.VatRate));
        Assert.Equal(25m, await StockAsync(app, row.Id));

        var service = Tag();
        await client.PostFormAsync("/yonetim/muhasebe/aktar", token, ("tip", "urun"), ("text", $"Ürün Adı\tTür\tStok\n{service}\tHizmet\t5"));
        var serviceRow = await app.WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(x => x.Name == service));
        Assert.Equal(ProductKind.Service, serviceRow.Kind);
        Assert.Equal(0m, await StockAsync(app, serviceRow.Id));
    }

    private static async Task<HttpClient> UserAsync(AppFactory app, string role)
    {
        var email = $"{Tag()}@test.local";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var client = app.NewClient();
        await client.LoginAsync(email);
        return client;
    }

    [Theory]
    [InlineData("/yonetim/muhasebe")]
    [InlineData("/yonetim/muhasebe/faturalar")]
    [InlineData("/yonetim/muhasebe/cariler")]
    [InlineData("/yonetim/muhasebe/kasa-banka")]
    [InlineData("/yonetim/muhasebe/cek-senet")]
    [InlineData("/yonetim/muhasebe/giderler")]
    [InlineData("/yonetim/muhasebe/urunler")]
    [InlineData("/yonetim/muhasebe/personel")]
    [InlineData("/yonetim/muhasebe/raporlar/kdv")]
    [InlineData("/yonetim/muhasebe/aktar")]
    public async Task Finance_IsForAdminAndAccountantOnly(string url)
    {
        var app = postgres.Shared;
        Assert.Equal(HttpStatusCode.OK, (await (await UserAsync(app, "Accountant")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await (await UserAsync(app, "Editor")).GetAsync(url)).StatusCode);
        var anonymous = await app.NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("yonetim-giris", anonymous.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accountant_SeesOnlyAccountingMenu_AndIsSentThereFromThePanelHome()
    {
        var app = postgres.Shared;
        var accountant = await UserAsync(app, "Accountant");
        var home = await accountant.GetAsync("/yonetim");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Contains("/yonetim/muhasebe", home.Headers.Location!.ToString());

        var page = WebUtility.HtmlDecode(await accountant.GetStringAsync("/yonetim/muhasebe"));
        Assert.Contains("Ön muhasebe", page);
        foreach (var hidden in new[] { "Site ayarları", "Galeri", "/yonetim/raporlar", "/yonetim/ara" }) Assert.DoesNotContain(hidden, page);
        foreach (var denied in new[] { "/yonetim/ayarlar", "/yonetim/kullanicilar", "/yonetim/galeri", "/yonetim/crm/teklifler" }) Assert.Equal(HttpStatusCode.Redirect, (await accountant.GetAsync(denied)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/yonetim/crm/musteriler")).StatusCode);

        Assert.DoesNotContain("Ön muhasebe", WebUtility.HtmlDecode(await (await UserAsync(app, "Editor")).GetStringAsync("/yonetim")));
        var (admin, _) = await AdminAsync(app);
        Assert.Contains("Ön muhasebe", WebUtility.HtmlDecode(await admin.GetStringAsync("/yonetim")));
    }

    [Fact]
    public async Task Users_CanBeCreatedWithAccountantRole()
    {
        var app = postgres.Shared;
        var (client, _) = await AdminAsync(app);
        var token = await client.GetTokenAsync("/yonetim/kullanicilar/ekle");
        var email = $"{Tag()}@test.local";
        await client.PostFormAsync("/yonetim/kullanicilar/ekle", token, ("Email", email), ("Password", AppFactory.Password), ("Role", "Accountant"));
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync(email))!, "Accountant"));
    }

    [Fact]
    public async Task ExistingDatabase_GetsAccountingTables_AndKeepsCustomers()
    {
        await using (var first = postgres.CreateApp("accounting_upgrade"))
        {
            first.NewClient();
            await first.WithDbAsync(async db =>
            {
                db.Customers.Add(new Customer { Name = "Eski müşteri" });
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TABLE "PaymentAllocations"; DROP TABLE "CashTransactions"; DROP TABLE "Cheques"; DROP TABLE "Expenses"; DROP TABLE "ExpenseCategories"; DROP TABLE "StockMovements"; DROP TABLE "InvoiceLines"; DROP TABLE "Invoices";
                    DROP TABLE "Products"; DROP TABLE "FinanceAccounts"; DROP TABLE "Employees"; DROP TABLE "CariAdjustments"; DROP TABLE "AccountingSequences";
                    ALTER TABLE "Customers" DROP COLUMN "PartyType";
                    DELETE FROM "SiteSettingRecords" WHERE "Key" = 'AccountingSeeded';
                    """);
                return 0;
            });
        }

        await using var second = postgres.CreateApp("accounting_upgrade", schemaMode: "existing");
        var (client, token) = await AdminAsync(second);
        var customer = await second.WithDbAsync(db => db.Customers.AsNoTracking().SingleAsync(x => x.Name == "Eski müşteri"));
        Assert.Equal(PartyType.Customer, customer.PartyType);
        Assert.Contains("Merkez Kasa", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/muhasebe/kasa-banka")));
        Assert.True(await second.WithDbAsync(db => db.ExpenseCategories.AnyAsync()));
        await ApprovedAsync(second, client, token, customer.Id);
        Assert.Equal(1200m, await BalanceAsync(second, customer.Id));
    }
}
