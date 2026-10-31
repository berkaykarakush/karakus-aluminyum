using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Models.Accounting;

public enum PartyType { Customer = 0, Supplier = 1, Both = 2 }
public enum ProductKind { Stock = 0, Service = 1 }
public enum InvoiceKind { Sale = 0, Purchase = 1, SaleReturn = 2, PurchaseReturn = 3 }
public enum InvoiceStatus { Draft = 0, Approved = 1, Cancelled = 2 }
public enum StockMovementKind { Opening = 0, Invoice = 1, InvoiceCancel = 2, Adjustment = 3 }
public enum FinanceAccountKind { Cash = 0, Bank = 1, Pos = 2, CreditCard = 3 }
public enum CashKind { Collection = 0, Payment = 1, Expense = 2, Salary = 3, Transfer = 4, ChequeCollected = 5, ChequePaid = 6, OtherIn = 7, OtherOut = 8 }
public enum ChequeKind { Cheque = 0, PromissoryNote = 1 }
public enum ChequeDirection { Received = 0, Given = 1 }
public enum ChequeStatus { Portfolio = 0, Deposited = 1, Collected = 2, Endorsed = 3, Bounced = 4, Paid = 5, Cancelled = 6 }

public sealed class Product
{
    public int Id { get; set; }
    [StringLength(60)] public string? Code { get; set; }
    [Required(ErrorMessage = "Ürün adı yazın."), StringLength(200)] public string Name { get; set; } = "";
    public ProductKind Kind { get; set; } = ProductKind.Stock;
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Column(TypeName = "numeric(18,2)"), Range(0, 1_000_000_000)] public decimal SalePrice { get; set; }
    [Column(TypeName = "numeric(18,2)"), Range(0, 1_000_000_000)] public decimal PurchasePrice { get; set; }
    [Column(TypeName = "numeric(5,2)"), Range(0, 100)] public decimal VatRate { get; set; } = 20m;
    [Column(TypeName = "numeric(18,3)"), Range(0, 1_000_000_000)] public decimal CriticalStock { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateOnly Date { get; set; }
    public StockMovementKind Kind { get; set; }
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; } // giriş artı, çıkış eksi
    public int? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    [StringLength(300)] public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Invoice
{
    public int Id { get; set; }
    public InvoiceKind Kind { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    [StringLength(40)] public string? Number { get; set; } // satışta onayda verilir, alışta tedarikçinin numarasıdır
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    [StringLength(3)] public string Currency { get; set; } = "TRY";
    [Column(TypeName = "numeric(18,6)")] public decimal ExchangeRate { get; set; } = 1m;
    [Column(TypeName = "numeric(18,2)")] public decimal Net { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DiscountTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal VatTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal WithholdingTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GrandTotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Payable { get; set; } // tevkifat düşülmüş tutar
    [Column(TypeName = "numeric(18,2)")] public decimal PayableTry { get; set; }
    [StringLength(60)] public string? EDocumentNumber { get; set; } // e-Fatura / e-Arşiv numarası: resmi belge entegratörde kesilir
    public int? QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public int? ContractId { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [StringLength(500)] public string? CancelReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<InvoiceLine> Lines { get; set; } = new();
    public List<PaymentAllocation> Allocations { get; set; } = new();
}

public sealed class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int SortOrder { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    [Required, StringLength(300)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; } = 1;
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Column(TypeName = "numeric(18,4)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(5,2)")] public decimal DiscountRate { get; set; }
    [Column(TypeName = "numeric(5,2)")] public decimal VatRate { get; set; } = 20m;
    [Column(TypeName = "numeric(5,2)")] public decimal WithholdingRate { get; set; } // KDV'nin yüzdesi: 2/10 tevkifat = 20
    [Column(TypeName = "numeric(18,2)")] public decimal Discount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Net { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Vat { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Withholding { get; set; }
}

public sealed class FinanceAccount
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Hesap adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    public FinanceAccountKind Kind { get; set; }
    [StringLength(3)] public string Currency { get; set; } = "TRY";
    [StringLength(40)] public string? Iban { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CashTransaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public FinanceAccount? Account { get; set; }
    public DateOnly Date { get; set; }
    public CashKind Kind { get; set; }
    public bool IsIn { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; } // hesabın para biriminde, her zaman artı
    [Column(TypeName = "numeric(18,6)")] public decimal ExchangeRate { get; set; } = 1m;
    [Column(TypeName = "numeric(18,2)")] public decimal AmountTry { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? ExpenseId { get; set; }
    public int? ChequeId { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid? TransferGroup { get; set; }
    [StringLength(500)] public string? Description { get; set; }
    public bool IsCancelled { get; set; }
    [StringLength(500)] public string? CancelReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

// Tahsilat, ödeme veya çekin hangi faturayı kapattığı. Tutar TL'dir.
public sealed class PaymentAllocation
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int? CashTransactionId { get; set; }
    public CashTransaction? CashTransaction { get; set; }
    public int? ChequeId { get; set; }
    public Cheque? Cheque { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
}

public sealed class Cheque
{
    public int Id { get; set; }
    public ChequeKind Kind { get; set; }
    public ChequeDirection Direction { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.Portfolio;
    [Required(ErrorMessage = "Belge numarası yazın."), StringLength(60)] public string Number { get; set; } = "";
    [StringLength(120)] public string? BankName { get; set; }
    [StringLength(160)] public string? Drawer { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? EndorsedToCustomerId { get; set; }
    public Customer? EndorsedToCustomer { get; set; }
    public int? AccountId { get; set; } // tahsil için verilen veya ödenecek banka
    public FinanceAccount? Account { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? StatusDate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; } // TL
    [StringLength(500)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ExpenseCategory
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Kategori adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class Expense
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    [Required(ErrorMessage = "Açıklama yazın."), StringLength(300)] public string Description { get; set; } = "";
    [StringLength(60)] public string? DocumentNumber { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Net { get; set; }
    [Column(TypeName = "numeric(5,2)")] public decimal VatRate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Vat { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Total { get; set; }
    public int? CustomerId { get; set; } // veresiye gider: tedarikçi carisine yazılır
    public Customer? Customer { get; set; }
    public int? AccountId { get; set; } // peşin gider: bu hesaptan çıkar
    public FinanceAccount? Account { get; set; }
    public bool IsCancelled { get; set; }
    [StringLength(500)] public string? CancelReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Employee
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Ad yazın."), StringLength(160)] public string Name { get; set; } = "";
    [StringLength(120)] public string? Position { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(40)] public string? Iban { get; set; }
    [Column(TypeName = "numeric(18,2)"), Range(0, 100_000_000)] public decimal NetSalary { get; set; }
    public DateOnly? StartDate { get; set; }
    public bool IsActive { get; set; } = true;
}

// Açılış bakiyesi ve düzeltme: artı müşteri bize borçlu, eksi biz borçluyuz.
public sealed class CariAdjustment
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
    [StringLength(300)] public string? Description { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AccountingSequence
{
    [Key, StringLength(40)] public string Key { get; set; } = "";
    public int Next { get; set; }
}

public sealed class InvoiceInputVm
{
    public int Id { get; set; }
    public InvoiceKind Kind { get; set; }
    public int CustomerId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    [StringLength(3)] public string Currency { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    [StringLength(40)] public string? Number { get; set; }
    [StringLength(60)] public string? EDocumentNumber { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public int? QuoteId { get; set; }
    public int? ContractId { get; set; }
    public string LinesJson { get; set; } = "[]";
}

public sealed class InvoiceLineInput
{
    public int? ProductId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "Adet";
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal VatRate { get; set; } = 20m;
    public decimal WithholdingRate { get; set; }
}

public static class AccountingLabels
{
    public static readonly string[] Currencies = { "TRY", "USD", "EUR" };
    public static readonly decimal[] VatRates = { 0m, 1m, 10m, 20m };
    public static string Label(InvoiceKind kind) => kind switch { InvoiceKind.Sale => "Satış faturası", InvoiceKind.Purchase => "Alış faturası", InvoiceKind.SaleReturn => "Satış iadesi", _ => "Alış iadesi" };
    public static string Label(InvoiceStatus status) => status switch { InvoiceStatus.Draft => "Taslak", InvoiceStatus.Approved => "Onaylı", _ => "İptal" };
    public static string Label(FinanceAccountKind kind) => kind switch { FinanceAccountKind.Cash => "Kasa", FinanceAccountKind.Bank => "Banka", FinanceAccountKind.Pos => "POS", _ => "Kredi kartı" };
    public static string Label(CashKind kind) => kind switch
    {
        CashKind.Collection => "Tahsilat", CashKind.Payment => "Ödeme", CashKind.Expense => "Gider", CashKind.Salary => "Maaş / avans", CashKind.Transfer => "Virman",
        CashKind.ChequeCollected => "Çek/senet tahsili", CashKind.ChequePaid => "Çek/senet ödemesi", CashKind.OtherIn => "Diğer giriş", _ => "Diğer çıkış"
    };
    public static string Label(ChequeKind kind) => kind == ChequeKind.Cheque ? "Çek" : "Senet";
    public static string Label(ChequeStatus status) => status switch
    {
        ChequeStatus.Portfolio => "Portföyde", ChequeStatus.Deposited => "Tahsile verildi", ChequeStatus.Collected => "Tahsil edildi", ChequeStatus.Endorsed => "Ciro edildi",
        ChequeStatus.Bounced => "Karşılıksız", ChequeStatus.Paid => "Ödendi", _ => "İptal"
    };
    public static string Label(PartyType type) => type switch { PartyType.Customer => "Müşteri", PartyType.Supplier => "Tedarikçi", _ => "Müşteri ve tedarikçi" };
    // İade faturası cariyi ana türün tersine çalıştırır.
    public static bool IsSaleSide(InvoiceKind kind) => kind is InvoiceKind.Sale or InvoiceKind.SaleReturn;
    public static int CariSign(InvoiceKind kind) => kind is InvoiceKind.Sale or InvoiceKind.PurchaseReturn ? 1 : -1;
    public static int StockSign(InvoiceKind kind) => kind is InvoiceKind.Purchase or InvoiceKind.SaleReturn ? 1 : -1;
}
