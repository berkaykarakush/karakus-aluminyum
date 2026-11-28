using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KarakusAluminyum.Web.Models.Crm;

public enum QuoteStatus { Draft = 0, Sent = 1, Accepted = 2, Rejected = 3, Expired = 4, Converted = 5 }

// Added: KDV toplama eklenir. Included: tutarlar KDV dahildir. None: belgede KDV belirtilmez.
public enum VatMode { Added = 0, Included = 1, None = 2 }

public static class QuoteLoss
{
    public static readonly string[] Reasons = { "Fiyat yüksek", "Teslim süresi uzun", "Rakip tercih edildi", "Vazgeçti / ertelendi", "Ulaşılamadı", "Ürün / sistem uymadı", "Diğer" };

    // Kayıp nedenine göre teklifin yeniden aranacağı gün sayısı; null: geri arama listesine girmez (rakip seçildi, ürün uymadı).
    public static int? WinBackDays(string? reason) => reason switch
    {
        "Fiyat yüksek" => 30,
        "Teslim süresi uzun" => 30,
        "Vazgeçti / ertelendi" => 60,
        "Ulaşılamadı" => 7,
        _ => null
    };
}

public sealed class Quote
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string QuoteNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, StringLength(180)] public string Title { get; set; } = "";
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntilUtc { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Added;
    public string DocumentJson { get; set; } = "{}";
    public string SnapshotJson { get; set; } = "{}";
    [StringLength(40)] public string? PublicToken { get; set; } // QR koddaki doğrulama bağlantısı: /belge/teklif/{token}
    [StringLength(2000)] public string? Notes { get; set; }
    // Belgeyi imzalayacak kişi: teklifi hazırlayan kullanıcının adıyla gelir; boşsa şablondaki yetkili basılır.
    [StringLength(120)] public string? Signatory { get; set; }
    [StringLength(60)] public string? SignatoryTitle { get; set; }
    // Müşterinin QR'lı teklif sayfasından onayı (README 7.18): kanıt olarak ad, zaman, IP, tarayıcı, tutar ve belge özeti saklanır.
    public DateTime? AcceptedUtc { get; set; }
    [StringLength(120)] public string? AcceptedName { get; set; }
    [StringLength(64)] public string? AcceptedIp { get; set; }
    [StringLength(300)] public string? AcceptedUserAgent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal? AcceptedTotal { get; set; }
    [StringLength(64)] public string? AcceptedHash { get; set; }
    // Reddedilen teklifin kayıp nedeni (README 7.15): satış dönüşüm raporunda gruplanır.
    [StringLength(40)] public string? LostReason { get; set; }
    [StringLength(120)] public string? LostCompetitor { get; set; }
    [StringLength(300)] public string? LostNote { get; set; }
    // Geri kazanma: fiyat, teslim süresi veya erteleme yüzünden kaybedilen teklif bu gün arama listesine düşer.
    public DateOnly? WinBackOn { get; set; }
    [StringLength(300)] public string? WinBackNote { get; set; } // son geri arama notu
    [Column(TypeName = "numeric(5,2)")] public decimal TaxRate { get; set; } = 20m;
    [Column(TypeName = "numeric(5,2)")] public decimal DiscountRate { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal GrandTotal { get; set; }
    // İç fiyatlandırma: müşteri çıktısında görünmez.
    public int? DrawingListId { get; set; }
    public Drawing.DrawingList? DrawingList { get; set; }
    public int? GlassTypeId { get; set; }
    public Drawing.GlassType? GlassType { get; set; }
    [Column(TypeName = "numeric(6,2)")] public decimal MarginPercent { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal CostTotal { get; set; }
    public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
}

public sealed class QuoteItem
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public Quote? Quote { get; set; }
    [StringLength(120)] public string? BlockName { get; set; }
    public int SortOrder { get; set; }
    [Required, StringLength(200)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; } = 1;
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    [Column(TypeName = "numeric(18,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(5,2)")] public decimal DiscountRate { get; set; } // satır iskontosu (%)
    [Column(TypeName = "numeric(18,2)")] public decimal LineTotal { get; set; } // satır iskontosu düşülmüş tutar
    public int? WindowDesignId { get; set; } // çizimden gelen kalem; maliyeti çizimden hesaplanır
    [Column(TypeName = "numeric(18,2)")] public decimal UnitCost { get; set; }
    // Taşerona verilen parça: dolu ise sistem hesabı yerine bu birim maliyet kullanılır.
    [Column(TypeName = "numeric(18,2)")] public decimal? ManualBaseCost { get; set; } // doğrama/panjur imalatı
    [Column(TypeName = "numeric(18,2)")] public decimal? ManualGlassCost { get; set; } // cam
}
