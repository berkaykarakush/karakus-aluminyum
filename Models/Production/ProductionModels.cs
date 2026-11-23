using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;

namespace KarakusAluminyum.Web.Models.Production;

// Ercom'daki sipariş durumları: Plan/Tasarım → Onay bekliyor → Onaylandı → Üretimde → Depo/Sevkiyat → Sevk edildi → Teslim edildi.
public enum OrderStatus { Plan = 0, AwaitingApproval = 1, Approved = 2, InProduction = 3, Warehouse = 4, Shipped = 5, Delivered = 6, Cancelled = 7 }

public static class OrderStatuses
{
    public static readonly OrderStatus[] Flow = { OrderStatus.Plan, OrderStatus.AwaitingApproval, OrderStatus.Approved, OrderStatus.InProduction, OrderStatus.Warehouse, OrderStatus.Shipped, OrderStatus.Delivered };

    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Plan => "Plan / Tasarım",
        OrderStatus.AwaitingApproval => "Onay bekliyor",
        OrderStatus.Approved => "Onaylandı",
        OrderStatus.InProduction => "Üretimde",
        OrderStatus.Warehouse => "Depo / Sevkiyat",
        OrderStatus.Shipped => "Sevk edildi",
        OrderStatus.Delivered => "Teslim edildi",
        _ => "İptal edildi"
    };

    public static bool IsOpen(OrderStatus status) => status is not (OrderStatus.Delivered or OrderStatus.Cancelled);
}

// Üretim siparişi: kabul edilen teklif veya sözleşmeden ya da elle açılır; pozları bağlı çizim listesindedir.
public sealed class ProductionOrder
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string OrderNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }
    public int? DrawingListId { get; set; }
    public DrawingList? DrawingList { get; set; }
    [Required(ErrorMessage = "Sipariş başlığı yazın."), StringLength(180)] public string Title { get; set; } = "";
    public OrderStatus Status { get; set; } = OrderStatus.Plan;
    public DateOnly OrderDate { get; set; }
    public DateOnly? PlannedShipDate { get; set; } // sevk (teslim) tarihi: üretim planı buna göre yapılır
    [StringLength(500)] public string? InstallAddress { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }

    // Ercom'daki sipariş işaretleri
    public bool SentToCutting { get; set; }
    public bool StockDeducted { get; set; }
    public bool GlassExcluded { get; set; }
    public bool InstallationExcluded { get; set; }
    public bool Reserved { get; set; }

    // Cam: camcıya (tedarikçi cari) verilen sipariş
    public int? GlassSupplierId { get; set; }
    public Customer? GlassSupplier { get; set; }
    public DateOnly? GlassOrderedOn { get; set; }
    public DateOnly? GlassExpectedOn { get; set; }
    public DateOnly? GlassReceivedOn { get; set; }

    // Montaj
    public int? InstallerId { get; set; }
    public Installer? Installer { get; set; }
    public DateOnly? InstallationDate { get; set; }

    public DateOnly? ShippedOn { get; set; }
    public DateOnly? DeliveredOn { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<OrderEvent> Events { get; set; } = new();
    public List<DeliveryNote> DeliveryNotes { get; set; } = new();
}

// Barkodlu üretim takibi: poz etiketinin (veya parça etiketinin) bir istasyonda okutulması.
// Bir pozun her adedi ayrı etikettir; aynı adet aynı istasyonda bir kez sayılır.
public sealed class ProductionScan
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public int WindowDesignId { get; set; }
    public int Unit { get; set; } // pozun kaçıncı adedi (1'den başlar)
    [Required, StringLength(20)] public string Station { get; set; } = ProductionStations.Cutting;
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    [StringLength(160)] public string? User { get; set; }
}

// Kesim makinesine (CNC / çift kafa testere) aktarılan kesim listesinin biçimi. Her makinenin beklediği sütunlar burada ayarlanır.
public sealed class MachineExportProfile
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Makine adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    [StringLength(4)] public string Delimiter { get; set; } = ";"; // ";", ",", "tab"
    [StringLength(1)] public string DecimalSeparator { get; set; } = ",";
    [StringLength(20)] public string Encoding { get; set; } = MachineExportFields.Utf8;
    [StringLength(10)] public string Extension { get; set; } = "csv";
    public bool HeaderRow { get; set; } = true;
    public bool AsciiOnly { get; set; } // eski makineler için Türkçe karakterleri sadeleştirir (ç → c)
    public bool OneRowPerPiece { get; set; } = true; // kapalıysa aynı boydaki aynı parçalar tek satırda adetle yazılır
    [Range(0, 3)] public int LengthDecimals { get; set; } = 1;
    [Required, StringLength(400)] public string Columns { get; set; } = "bar,profileCode,profile,length,angleLeft,angleRight,quantity,pos,part,barcode";
    [StringLength(500)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class MachineExportFields
{
    public const string Utf8 = "utf-8";
    public const string Utf8Bom = "utf-8-bom";
    public const string Windows1254 = "windows-1254";
    public static readonly string[] Encodings = { Utf8, Utf8Bom, Windows1254 };

    public static readonly (string Key, string Label)[] All =
    {
        ("row", "Sıra no"), ("order", "Sipariş no"), ("customer", "Müşteri"), ("bar", "Boy no"), ("barLength", "Boy uzunluğu"),
        ("profileCode", "Profil kodu (stok kartı)"), ("profile", "Profil"), ("part", "Parça adı"), ("length", "Kesim ölçüsü (mm)"),
        ("angleLeft", "Sol açı"), ("angleRight", "Sağ açı"), ("quantity", "Adet"), ("pos", "Poz no"), ("posName", "Poz adı"),
        ("unit", "Poz adedi (n/toplam)"), ("color", "Renk"), ("barcode", "Barkod (parça etiketi)"), ("system", "Seri")
    };

    public static string Label(string key) => All.FirstOrDefault(x => x.Key == key).Label ?? key;
}

// Barkod (etiket) yazıcısı: Zebra ve ZPL öykünmeli yazıcılar ZPL, TSC / Xprinter gibi yazıcılar TSPL konuşur.
// Ağ adresi girilirse etiketler sunucudan doğrudan yazıcının 9100 portuna gönderilir (sunucu yazıcıya erişebilmelidir).
public sealed class LabelPrinter
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Yazıcı adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    [StringLength(10)] public string Language { get; set; } = LabelLanguages.Zpl;
    [Range(150, 600)] public int Dpi { get; set; } = 203;
    [Range(20, 200, ErrorMessage = "Etiket genişliği 20-200 mm olmalıdır.")] public int WidthMm { get; set; } = 100;
    [Range(15, 300, ErrorMessage = "Etiket yüksekliği 15-300 mm olmalıdır.")] public int HeightMm { get; set; } = 50;
    [Range(0, 20)] public decimal GapMm { get; set; } = 2; // TSPL: etiketler arası boşluk
    [Range(0, 30)] public int Darkness { get; set; } // 0: yazıcı ayarı kalır
    public bool AsciiOnly { get; set; } = true; // yazıcı fontunda Türkçe karakter yoksa ş → s
    [StringLength(64)] public string? Host { get; set; }
    [Range(1, 65535)] public int Port { get; set; } = 9100;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class LabelLanguages
{
    public const string Zpl = "zpl";
    public const string Tspl = "tspl";
    public static readonly string[] All = { Zpl, Tspl };
    public static string Label(string language) => language == Tspl ? "TSPL (TSC, Xprinter, Argox)" : "ZPL (Zebra ve ZPL uyumlu)";
}

public static class ProductionStations
{
    public const string Cutting = "cut";
    public const string Assembly = "assembly";
    public const string Glazing = "glazing";
    public const string Packed = "packed";
    public static readonly string[] All = { Cutting, Assembly, Glazing, Packed };

    public static string Label(string station) => station switch
    {
        Cutting => "Kesim",
        Assembly => "Birleştirme / donanım",
        Glazing => "Camlama",
        Packed => "Paketlendi (hazır)",
        _ => station
    };
}

// Sipariş geçmişi: durum değişiklikleri, işaretler, cam ve montaj kayıtları.
public sealed class OrderEvent
{
    public int Id { get; set; }
    public int ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public OrderStatus? Status { get; set; }
    [Required, StringLength(500)] public string Text { get; set; } = "";
    [StringLength(160)] public string? User { get; set; }
}

// Montaj ekibi (Ercom'daki montajcı bilgileri). Kendi ekibimiz veya taşeron olabilir.
public sealed class Installer
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Ekip adı yazın."), StringLength(160)] public string Name { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
    [Range(1, 50, ErrorMessage = "Ekip kişi sayısı 1-50 arasında olmalıdır.")] public int TeamSize { get; set; } = 2;
    public bool IsSubcontractor { get; set; }
    [Column(TypeName = "numeric(18,2)"), Range(0, 10_000_000)] public decimal DailyCost { get; set; } // ekibin günlük ücreti (taşeron); takvim işçiliği bununla hesaplanır
    [StringLength(500)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

// Sevk irsaliyesi: siparişin pozlarının tamamı veya bir kısmı.
public sealed class DeliveryNote
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Number { get; set; } = "";
    public int ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public DateOnly Date { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(40)] public string? Vehicle { get; set; }
    [StringLength(120)] public string? Driver { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<DeliveryNoteLine> Lines { get; set; } = new();
}

public sealed class DeliveryNoteLine
{
    public int Id { get; set; }
    public int DeliveryNoteId { get; set; }
    public DeliveryNote? DeliveryNote { get; set; }
    public int SortOrder { get; set; }
    [Required, StringLength(200)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(18,3)")] public decimal Quantity { get; set; }
    [StringLength(20)] public string Unit { get; set; } = "Adet";
    public int? WindowDesignId { get; set; } // pozdan gelen satır: kalan adet bu pozdan hesaplanır
}

public sealed class OrderInputVm
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    [Required(ErrorMessage = "Sipariş başlığı yazın."), StringLength(180)] public string Title { get; set; } = "";
    public int? DrawingListId { get; set; }
    public DateOnly OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? PlannedShipDate { get; set; }
    [StringLength(500)] public string? InstallAddress { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    public bool GlassExcluded { get; set; }
    public bool InstallationExcluded { get; set; }
    public bool Reserved { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}

public sealed class DeliveryNoteInputVm
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(40)] public string? Vehicle { get; set; }
    [StringLength(120)] public string? Driver { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public List<DeliveryLineInput> Lines { get; set; } = new();
}

public sealed class DeliveryLineInput
{
    public int? WindowDesignId { get; set; }
    [StringLength(200)] public string? Description { get; set; }
    public decimal Quantity { get; set; }
    [StringLength(20)] public string? Unit { get; set; }
}
