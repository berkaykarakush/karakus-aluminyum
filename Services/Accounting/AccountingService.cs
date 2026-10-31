using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Accounting;

public sealed class AccountingException(string message) : Exception(message);

// Muhasebe kayıtlarını değiştiren tek yer. Her işlem tek veritabanı işlemidir: ya tamamı olur ya hiçbiri.
public sealed class AccountingService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public AccountingService(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // Geçici bağlantı hatasında tüm işlem baştan denenir; yarım kalan takip kaydı taşınmaz.
    private Task InTransactionAsync(Func<Task> action) => _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await action();
        await transaction.CommitAsync();
    });

    private static string SequenceKey(InvoiceKind kind, int year) => $"{(kind == InvoiceKind.Sale ? "FTR" : "IAD")}-{year}";

    private async Task<string> NextNumberAsync(InvoiceKind kind, int year)
    {
        var key = SequenceKey(kind, year);
        // Satır kilidi ile artırılır; aynı anda onaylanan iki fatura aynı numarayı alamaz.
        var next = (await _db.Database.SqlQueryRaw<int>(
            "INSERT INTO \"AccountingSequences\" (\"Key\", \"Next\") VALUES ({0}, 1) ON CONFLICT (\"Key\") DO UPDATE SET \"Next\" = \"AccountingSequences\".\"Next\" + 1 RETURNING \"Next\" AS \"Value\"", key).ToListAsync()).Single();
        return $"{key}-{next:000000}";
    }

    public async Task ApproveAsync(int id)
    {
        await InTransactionAsync(async () =>
        {
        var invoice = await _db.Invoices.Include(x => x.Lines).ThenInclude(x => x.Product).FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Fatura bulunamadı.");
        if (invoice.Status != InvoiceStatus.Draft) throw new AccountingException("Yalnızca taslak fatura onaylanabilir.");
        if (invoice.Lines.Count == 0) throw new AccountingException("Satırı olmayan fatura onaylanamaz.");
        InvoiceRules.Calculate(invoice);

        if (invoice.Kind is InvoiceKind.Sale or InvoiceKind.SaleReturn)
        {
            invoice.Number = await NextNumberAsync(invoice.Kind, invoice.Date.Year);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(invoice.Number)) throw new AccountingException("Alış belgesinin numarasını yazın.");
            if (await _db.Invoices.AnyAsync(x => x.Id != id && x.Kind == invoice.Kind && x.CustomerId == invoice.CustomerId && x.Number == invoice.Number && x.Status != InvoiceStatus.Cancelled))
                throw new AccountingException("Bu tedarikçinin aynı numaralı belgesi zaten kayıtlı.");
        }

        var sign = AccountingLabels.StockSign(invoice.Kind);
        foreach (var line in invoice.Lines.Where(x => x.Product is { Kind: ProductKind.Stock }))
            _db.StockMovements.Add(new StockMovement { ProductId = line.ProductId!.Value, Date = invoice.Date, Kind = StockMovementKind.Invoice, Quantity = sign * line.Quantity, InvoiceId = id, Note = invoice.Number });

        invoice.Status = InvoiceStatus.Approved;
        invoice.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("approve", "Invoice", id.ToString(), $"{invoice.Number}; {invoice.PayableTry} TL");
        });
    }

    public async Task CancelInvoiceAsync(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AccountingException("İptal gerekçesi yazın.");
        await InTransactionAsync(async () =>
        {
        var invoice = await _db.Invoices.Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Fatura bulunamadı.");
        if (invoice.Status != InvoiceStatus.Approved) throw new AccountingException("Yalnızca onaylı fatura iptal edilebilir.");
        if (invoice.Allocations.Count > 0) throw new AccountingException("Bu faturaya bağlı tahsilat veya ödeme var; önce onları iptal edin.");

        // Stok geçmişi silinmez: ters hareket yazılır.
        foreach (var movement in await _db.StockMovements.AsNoTracking().Where(x => x.InvoiceId == id && x.Kind == StockMovementKind.Invoice).ToListAsync())
            _db.StockMovements.Add(new StockMovement { ProductId = movement.ProductId, Date = invoice.Date, Kind = StockMovementKind.InvoiceCancel, Quantity = -movement.Quantity, InvoiceId = id, Note = "İptal: " + invoice.Number });

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelReason = reason.Trim();
        invoice.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("cancel", "Invoice", id.ToString(), $"{invoice.Number}; {reason}");
        });
    }

    private async Task<FinanceAccount> ActiveAccountAsync(int id)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Hesap bulunamadı.");
        if (!account.IsActive) throw new AccountingException("Bu hesap pasif.");
        return account;
    }

    private static decimal CheckRate(FinanceAccount account, decimal rate)
    {
        if (account.Currency == "TRY") return 1m;
        if (rate is <= 0 or > 100000) throw new AccountingException("Geçerli bir kur girin.");
        return rate;
    }

    // Tutar, seçilen faturaya; seçilmediyse vadesi en eski açık faturalara dağıtılır. Artan kısım caride alacak/borç kalır.
    private async Task AllocateAsync(int customerId, bool isCollection, decimal amountTry, int? invoiceId, Action<PaymentAllocation> link)
    {
        var invoiceKinds = isCollection ? new[] { InvoiceKind.Sale, InvoiceKind.PurchaseReturn } : new[] { InvoiceKind.Purchase, InvoiceKind.SaleReturn };
        var open = await _db.Invoices.Include(x => x.Allocations)
            .Where(x => x.CustomerId == customerId && x.Status == InvoiceStatus.Approved && invoiceKinds.Contains(x.Kind) && (invoiceId == null || x.Id == invoiceId))
            .OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync();
        if (invoiceId is not null && open.Count == 0) throw new AccountingException("Seçilen fatura bu cariye ait değil veya açık değil.");
        var left = amountTry;
        foreach (var invoice in open)
        {
            var due = invoice.PayableTry - invoice.Allocations.Sum(x => x.Amount);
            if (due <= 0 || left <= 0) continue;
            var part = Math.Min(due, left);
            var allocation = new PaymentAllocation { InvoiceId = invoice.Id, Amount = part };
            link(allocation);
            _db.PaymentAllocations.Add(allocation);
            left -= part;
        }
    }

    public async Task RecordPaymentAsync(bool isCollection, int customerId, int accountId, DateOnly date, decimal amount, decimal rate, int? invoiceId, string? description)
    {
        if (amount <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        if (!await _db.Customers.AnyAsync(x => x.Id == customerId)) throw new AccountingException("Cari bulunamadı.");
        await InTransactionAsync(async () =>
        {
        var account = await ActiveAccountAsync(accountId);
        rate = CheckRate(account, rate);
        var row = new CashTransaction
        {
            AccountId = accountId, Date = date, Kind = isCollection ? CashKind.Collection : CashKind.Payment, IsIn = isCollection, Amount = amount, ExchangeRate = rate,
            AmountTry = InvoiceRules.Money(amount * rate), CustomerId = customerId, Description = description?.Trim()
        };
        _db.CashTransactions.Add(row);
        await _db.SaveChangesAsync();
        await AllocateAsync(customerId, isCollection, row.AmountTry, invoiceId, a => a.CashTransactionId = row.Id);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(isCollection ? "collection" : "payment", "CashTransaction", row.Id.ToString(), $"{row.AmountTry} TL");
        });
    }

    public async Task RecordOtherAsync(bool isIn, int accountId, DateOnly date, decimal amount, decimal rate, string? description)
    {
        if (amount <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(description)) throw new AccountingException("Açıklama yazın.");
        var account = await ActiveAccountAsync(accountId);
        rate = CheckRate(account, rate);
        var row = new CashTransaction { AccountId = accountId, Date = date, Kind = isIn ? CashKind.OtherIn : CashKind.OtherOut, IsIn = isIn, Amount = amount, ExchangeRate = rate, AmountTry = InvoiceRules.Money(amount * rate), Description = description.Trim() };
        _db.CashTransactions.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("other", "CashTransaction", row.Id.ToString(), $"{row.AmountTry} TL");
    }

    public async Task TransferAsync(int fromId, int toId, DateOnly date, decimal amount, decimal rate, string? description)
    {
        if (fromId == toId) throw new AccountingException("Kaynak ve hedef hesap aynı olamaz.");
        if (amount <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        var from = await ActiveAccountAsync(fromId);
        var to = await ActiveAccountAsync(toId);
        if (from.Currency != to.Currency) throw new AccountingException("Virman yalnızca aynı para birimindeki hesaplar arasında yapılır.");
        rate = CheckRate(from, rate);
        var group = Guid.NewGuid();
        var amountTry = InvoiceRules.Money(amount * rate);
        CashTransaction Leg(int account, bool isIn) => new() { AccountId = account, Date = date, Kind = CashKind.Transfer, IsIn = isIn, Amount = amount, ExchangeRate = rate, AmountTry = amountTry, TransferGroup = group, Description = description?.Trim() };
        _db.CashTransactions.AddRange(Leg(fromId, false), Leg(toId, true));
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("transfer", "CashTransaction", group.ToString(), $"{from.Name} > {to.Name}: {amountTry} TL");
    }

    public async Task SalaryAsync(int employeeId, int accountId, DateOnly date, decimal amount, decimal rate, string? description)
    {
        if (amount <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId) ?? throw new AccountingException("Personel bulunamadı.");
        var account = await ActiveAccountAsync(accountId);
        rate = CheckRate(account, rate);
        var row = new CashTransaction { AccountId = accountId, Date = date, Kind = CashKind.Salary, IsIn = false, Amount = amount, ExchangeRate = rate, AmountTry = InvoiceRules.Money(amount * rate), EmployeeId = employeeId, Description = string.IsNullOrWhiteSpace(description) ? employee.Name : description.Trim() };
        _db.CashTransactions.Add(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("salary", "CashTransaction", row.Id.ToString(), $"{employee.Name}: {row.AmountTry} TL");
    }

    public async Task CancelCashAsync(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AccountingException("İptal gerekçesi yazın.");
        await InTransactionAsync(async () =>
        {
        var row = await _db.CashTransactions.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Hareket bulunamadı.");
        if (row.IsCancelled) throw new AccountingException("Bu hareket zaten iptal edilmiş.");
        if (row.Kind is CashKind.Expense or CashKind.ChequeCollected or CashKind.ChequePaid)
            throw new AccountingException("Bu hareket kendi kaydından iptal edilir (gider veya çek/senet).");
        var legs = row.TransferGroup is null ? new List<CashTransaction> { row } : await _db.CashTransactions.Where(x => x.TransferGroup == row.TransferGroup).ToListAsync();
        foreach (var leg in legs) { leg.IsCancelled = true; leg.CancelReason = reason.Trim(); }
        _db.PaymentAllocations.RemoveRange(_db.PaymentAllocations.Where(x => x.CashTransactionId == id));
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("cancel", "CashTransaction", id.ToString(), reason);
        });
    }

    public async Task<Cheque> AddChequeAsync(Cheque input)
    {
        if (input.Amount <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        if (input.DueDate < input.IssueDate) throw new AccountingException("Vade, düzenleme tarihinden önce olamaz.");
        if (!await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) throw new AccountingException("Cari bulunamadı.");
        await InTransactionAsync(async () =>
        {
        input.Id = 0;
        input.Status = ChequeStatus.Portfolio;
        input.StatusDate = input.IssueDate;
        _db.Cheques.Add(input);
        await _db.SaveChangesAsync();
        await AllocateAsync(input.CustomerId, input.Direction == ChequeDirection.Received, input.Amount, null, a => a.ChequeId = input.Id);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "Cheque", input.Id.ToString(), $"{input.Number}; {input.Amount} TL");
        });
        return input;
    }

    public async Task ChangeChequeAsync(int id, ChequeStatus next, DateOnly date, int? accountId, int? endorsedTo)
    {
        await InTransactionAsync(async () =>
        {
        var cheque = await _db.Cheques.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Belge bulunamadı.");
        if (!InvoiceRules.CanTransition(cheque.Direction, cheque.Status, next))
            throw new AccountingException($"Belge {AccountingLabels.Label(cheque.Status).ToLowerInvariant()} durumundayken {AccountingLabels.Label(next).ToLowerInvariant()} yapılamaz.");

        switch (next)
        {
            case ChequeStatus.Collected or ChequeStatus.Paid:
                var targetId = accountId ?? cheque.AccountId ?? throw new AccountingException("Hesap seçin.");
                var account = await ActiveAccountAsync(targetId);
                if (account.Currency != "TRY") throw new AccountingException("Çek/senet TL hesapla işlenir.");
                _db.CashTransactions.Add(new CashTransaction
                {
                    AccountId = targetId, Date = date, Kind = next == ChequeStatus.Collected ? CashKind.ChequeCollected : CashKind.ChequePaid, IsIn = next == ChequeStatus.Collected,
                    Amount = cheque.Amount, AmountTry = cheque.Amount, ChequeId = id, Description = $"{AccountingLabels.Label(cheque.Kind)} {cheque.Number}"
                });
                cheque.AccountId = targetId;
                break;
            case ChequeStatus.Deposited:
                cheque.AccountId = accountId ?? throw new AccountingException("Tahsile verilen bankayı seçin.");
                await ActiveAccountAsync(cheque.AccountId.Value);
                break;
            case ChequeStatus.Endorsed:
                if (endorsedTo is null || endorsedTo == cheque.CustomerId || !await _db.Customers.AnyAsync(x => x.Id == endorsedTo)) throw new AccountingException("Ciro edilecek cariyi seçin.");
                cheque.EndorsedToCustomerId = endorsedTo;
                await AllocateAsync(endorsedTo.Value, false, cheque.Amount, null, a => a.ChequeId = id);
                break;
            case ChequeStatus.Bounced or ChequeStatus.Cancelled:
                // Karşılıksız ve iptal belge cariden düşer; kapattığı faturalar yeniden açılır.
                _db.PaymentAllocations.RemoveRange(_db.PaymentAllocations.Where(x => x.ChequeId == id));
                break;
        }
        cheque.Status = next;
        cheque.StatusDate = date;
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("status", "Cheque", id.ToString(), $"{cheque.Number}: {AccountingLabels.Label(next)}");
        });
    }

    public async Task<Expense> AddExpenseAsync(Expense input)
    {
        if (input.Net <= 0) throw new AccountingException("Tutar sıfırdan büyük olmalıdır.");
        if (input.VatRate is < 0 or > 100) throw new AccountingException("KDV oranı geçersiz.");
        if (input.AccountId is null && input.CustomerId is null) throw new AccountingException("Ödenen hesabı veya veresiye için tedarikçiyi seçin.");
        if (!await _db.ExpenseCategories.AnyAsync(x => x.Id == input.CategoryId)) throw new AccountingException("Kategori seçin.");
        if (input.CustomerId is not null && !await _db.Customers.AnyAsync(x => x.Id == input.CustomerId)) throw new AccountingException("Cari bulunamadı.");
        await InTransactionAsync(async () =>
        {
        input.Id = 0;
        input.Net = InvoiceRules.Money(input.Net);
        input.Vat = InvoiceRules.Money(input.Net * input.VatRate / 100m);
        input.Total = input.Net + input.Vat;
        _db.Expenses.Add(input);
        await _db.SaveChangesAsync();
        if (input.AccountId is not null)
        {
            var account = await ActiveAccountAsync(input.AccountId.Value);
            if (account.Currency != "TRY") throw new AccountingException("Gider TL hesaptan ödenir.");
            _db.CashTransactions.Add(new CashTransaction { AccountId = account.Id, Date = input.Date, Kind = CashKind.Expense, IsIn = false, Amount = input.Total, AmountTry = input.Total, ExpenseId = input.Id, Description = input.Description });
            await _db.SaveChangesAsync();
        }
        await _audit.RecordAsync("create", "Expense", input.Id.ToString(), $"{input.Description}; {input.Total} TL");
        });
        return input;
    }

    public async Task CancelExpenseAsync(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AccountingException("İptal gerekçesi yazın.");
        await InTransactionAsync(async () =>
        {
        var expense = await _db.Expenses.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AccountingException("Gider bulunamadı.");
        if (expense.IsCancelled) throw new AccountingException("Bu gider zaten iptal edilmiş.");
        expense.IsCancelled = true;
        expense.CancelReason = reason.Trim();
        foreach (var leg in await _db.CashTransactions.Where(x => x.ExpenseId == id).ToListAsync()) { leg.IsCancelled = true; leg.CancelReason = reason.Trim(); }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("cancel", "Expense", id.ToString(), reason);
        });
    }
}
