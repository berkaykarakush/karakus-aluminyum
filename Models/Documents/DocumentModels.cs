using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Models.Documents;

public static class DocumentKinds
{
    public const string Quote = "quote";
    public const string Contract = "contract";
}

// Teklif ve sözleşme şablonu ayarları. Belge oluşurken kopyası belgeye yazılır (bkz. SnapshotJson).
public sealed class DocumentTemplate
{
    public int Id { get; set; }
    [Required, StringLength(20)] public string Kind { get; set; } = DocumentKinds.Quote;
    [Required(ErrorMessage = "Belge başlığı yazın."), StringLength(80)] public string Title { get; set; } = "";
    [StringLength(500)] public string? LogoPath { get; set; } = "/images/transparent.webp";
    [StringLength(160)] public string? SellerName { get; set; }
    [StringLength(240)] public string? SellerLegalName { get; set; }
    [StringLength(120)] public string? Signatory { get; set; }
    [StringLength(60)] public string? SignatoryTitle { get; set; }
    [StringLength(300)] public string? SellerAddress { get; set; }
    [StringLength(160)] public string? SellerTaxInfo { get; set; }
    [StringLength(60)] public string? Phone { get; set; }
    [StringLength(300)] public string? FooterAddress { get; set; }
    [StringLength(4000)] public string? Intro { get; set; }
    [StringLength(4000)] public string? Closing { get; set; }
    [StringLength(2000)] public string? SignOff { get; set; }
    [StringLength(2000)] public string? DefaultNote { get; set; }
    [StringLength(40)] public string? BuyerLabel { get; set; }
    [StringLength(40)] public string? SellerLabel { get; set; }
    [StringLength(160)] public string? SellerEmail { get; set; }
    [StringLength(1000)] public string? BankInfo { get; set; } // proformada basılır: banka, şube, IBAN
    [StringLength(1000)] public string? AcceptanceText { get; set; } // proformanın onay kutusundaki metin
    public string TermsJson { get; set; } = "[]"; // yeni belgeye gelen şartlar: List<BlockClause>
    public string VariablesJson { get; set; } = "{}"; // yeni belgeye gelen değişken değerleri (ör. teslim_suresi)
    public string ArticlesJson { get; set; } = "[]";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

// Sözleşme maddesi. Kind, maddenin içine veriden üretilen bölümü belirler.
public sealed class ContractArticle
{
    // Boş bırakılabilir: bedel maddesinin metni yoktur, başlığı boş satır formda yok sayılır.
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string Kind { get; set; } = ContractArticleKinds.Text;
}

public static class ContractArticleKinds
{
    public const string Text = "text";
    public const string Subject = "subject"; // ardından bloklar ve maddeleri basılır
    public const string Price = "price";     // ardından toplam bedel basılır
    public const string Payment = "payment"; // ardından ödeme planı basılır
    public const string Items = "items";     // ardından fiyat tablosu (kalemler, iskonto, KDV) basılır
    public const string Terms = "terms";     // ardından belgenin özel şartları basılır
    public static readonly string[] All = { Text, Subject, Price, Payment, Items, Terms };
}

// Bloklarda seçilebilen hazır madde. Aynı iş kaleminin teklif ve sözleşme metni birlikte tutulur.
public sealed class TemplateClause
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Madde adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    [StringLength(2000)] public string? QuoteText { get; set; }
    [StringLength(2000)] public string? ContractText { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum ContractStatus { Draft = 0, Signed = 1, Cancelled = 2 }

public sealed class Contract
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string ContractNumber { get; set; } = "";
    public int? QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;
    [Required, StringLength(240)] public string BuyerName { get; set; } = "";
    [StringLength(500)] public string? BuyerAddress { get; set; }
    [StringLength(160)] public string? BuyerTaxInfo { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TotalAmount { get; set; } // kalem varsa kalemlerden hesaplanır
    public VatMode VatMode { get; set; } = VatMode.Included;
    [Column(TypeName = "numeric(5,2)")] public decimal TaxRate { get; set; } = 20m;
    [Column(TypeName = "numeric(5,2)")] public decimal DiscountRate { get; set; }
    public int PageCount { get; set; } = 1;
    public string DocumentJson { get; set; } = "{}";
    public string SnapshotJson { get; set; } = "{}";
    [StringLength(40)] public string? PublicToken { get; set; } // QR koddaki doğrulama bağlantısı: /belge/sozlesme/{token}
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

// Belgenin değişken içeriği: teklif ve sözleşme aynı yapıyı kullanır.
public sealed class DocumentContent
{
    public string? Intro { get; set; }
    public string? Closing { get; set; }
    public string? Note { get; set; }
    public string? Subject { get; set; }
    public string? PriceNote { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<DocumentBlock> Blocks { get; set; } = new();
    public List<PaymentLine> Payments { get; set; } = new();
    public List<BlockClause> Terms { get; set; } = new(); // ödeme, teslim, garanti gibi şartlar
    public List<int> DesignIds { get; set; } = new();
}

public sealed class DocumentBlock
{
    public string? Name { get; set; }
    public string? Footer { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
    public List<BlockClause> Clauses { get; set; } = new();
    public List<BlockItem> Items { get; set; } = new();
}

public sealed class BlockClause
{
    public string? Title { get; set; }
    public string? Text { get; set; }
}

public sealed class BlockItem
{
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Unit { get; set; } = "Adet";
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; } // satır iskontosu (%)
    public int? DesignId { get; set; }
    public decimal UnitCost { get; set; }
    public bool ManualPrice { get; set; } // fiyat elle yazıldıysa marj değişince korunur
    public decimal? ManualBaseCost { get; set; } // taşeron imalat maliyeti (birim)
    public decimal? ManualGlassCost { get; set; } // taşeron cam maliyeti (birim)
}

public static class PaymentKinds
{
    public const string Cash = "cash";
    public const string Check = "check";
    public const string Barter = "barter";
    public const string Other = "other";
    public static readonly string[] All = { Cash, Check, Barter, Other };
}

public sealed class PaymentLine
{
    public string Kind { get; set; } = PaymentKinds.Cash;
    public decimal Amount { get; set; }
    public int Count { get; set; } = 1;
    public string? Text { get; set; }
}

public sealed class ContractInputVm
{
    public int CustomerId { get; set; }
    [Required(ErrorMessage = "Sözleşme başlığı yazın."), StringLength(180)] public string Title { get; set; } = "";
    public DateTime DocumentDate { get; set; } = DateTime.UtcNow.Date;
    [Required(ErrorMessage = "Alıcı adını yazın."), StringLength(240)] public string BuyerName { get; set; } = "";
    [StringLength(500)] public string? BuyerAddress { get; set; }
    [StringLength(160)] public string? BuyerTaxInfo { get; set; }
    [Range(0, 100000000000)] public decimal TotalAmount { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Included;
    [Range(0, 100)] public decimal TaxRate { get; set; } = 20m;
    [Range(0, 100)] public decimal DiscountRate { get; set; }
    [Range(1, 500, ErrorMessage = "Sayfa sayısı en az 1 olmalıdır.")] public int PageCount { get; set; } = 1;
    public string DocumentJson { get; set; } = "{}";
}
