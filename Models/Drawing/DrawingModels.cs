using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Models.Drawing;

public sealed class GlassType
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Cam adı yazın."), StringLength(120)] public string Name { get; set; } = "";
    [Column(TypeName = "numeric(18,2)"), Range(0, 1000000)] public decimal PricePerM2 { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WindowDesign
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [StringLength(20)] public string Category { get; set; } = ProductCategories.Aluminum;
    [StringLength(7)] public string ColorHex { get; set; } = "#f4f5f5";
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    [StringLength(10)] public string Shape { get; set; } = WindowShapes.Rect;
    public int ShapeParamMm { get; set; }
    public int Quantity { get; set; } = 1;
    public string LayoutJson { get; set; } = "{}";
    public string ParamsJson { get; set; } = "{}"; // panjur, korkuluk ve cam balkon seçimleri: ProductParams
    [StringLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class ProductCategories
{
    public const string Aluminum = "aluminum";
    public const string Pvc = "pvc";
    public const string Shutter = "shutter";
    public const string Railing = "railing";
    public const string GlassBalcony = "balcony";
    public static readonly string[] All = { Aluminum, Pvc, Shutter, Railing, GlassBalcony };

    public static bool IsWindow(string category) => category is Aluminum or Pvc;

    public static string Label(string? category) => category switch
    {
        Pvc => "Plastik Doğrama",
        Shutter => "Panjur",
        Railing => "Korkuluk",
        GlassBalcony => "Cam Balkon",
        _ => "Alüminyum Doğrama"
    };
}

// Tek bir panjur, korkuluk veya cam balkon çiziminin seçimleri.
public sealed class ProductParams
{
    public string Drive { get; set; } = "motor";    // panjur: motor | manual
    public string Infill { get; set; } = "glass";   // korkuluk: glass | bars
    public decimal PostSpacingMm { get; set; }      // korkuluk: 0 ise 120 cm çizilir
    public int PanelCount { get; set; } = 4;        // cam balkon
    public string OpenType { get; set; } = "sliding"; // cam balkon: sliding | folding
}

public static class WindowShapes
{
    public const string Rect = "rect";
    public const string Arch = "arch";       // ShapeParamMm: kemer yüksekliği
    public const string SlopeLeft = "slopeL"; // ShapeParamMm: alçak olan sol kenarın yüksekliği
    public const string SlopeRight = "slopeR"; // ShapeParamMm: alçak olan sağ kenarın yüksekliği
    public static readonly string[] All = { Rect, Arch, SlopeLeft, SlopeRight };
}

public static class WindowOpenings
{
    public const string Fixed = "fixed";
    public const string Left = "left";           // solda menteşeli
    public const string Right = "right";         // sağda menteşeli
    public const string Tilt = "tilt";           // vasistas
    public const string TiltLeft = "tiltLeft";   // açılır-devrilir, solda menteşeli
    public const string TiltRight = "tiltRight"; // açılır-devrilir, sağda menteşeli
    public const string SlideLeft = "slideLeft";
    public const string SlideRight = "slideRight";
    public const string DoorLeft = "doorLeft";
    public const string DoorRight = "doorRight";
    public static readonly string[] All = { Fixed, Left, Right, Tilt, TiltLeft, TiltRight, SlideLeft, SlideRight, DoorLeft, DoorRight };

    public static bool IsSlide(string opening) => opening is SlideLeft or SlideRight;
}

// Çizim ağacı: bir düğüm ya bölünür (Split) ya da cam/panel/kanat içeren yapraktır.
public sealed class LayoutNode
{
    public string? Id { get; set; }
    public string? Split { get; set; } // "v": dikey kayıtlar (yan yana), "h": yatay kayıtlar (üst üste), null: yaprak
    public bool Sliding { get; set; }  // sürme bölme: aralarında kayıt yoktur
    public List<decimal>? Sizes { get; set; } // çocukların ölçüleri (mm); toplamları üst bölümün ölçüsüne eşittir
    public List<LayoutNode>? Children { get; set; }
    public string Opening { get; set; } = WindowOpenings.Fixed;
    public string Fill { get; set; } = "glass"; // "glass" veya "panel"
}

public sealed class DesignInputVm
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Çizim adı yazın."), StringLength(160)] public string Name { get; set; } = "";
    public int? CustomerId { get; set; }
    public string Category { get; set; } = ProductCategories.Aluminum;
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string ColorHex { get; set; } = "#f4f5f5";
    public int WidthMm { get; set; } = 1500;
    public int HeightMm { get; set; } = 1400;
    public string Shape { get; set; } = WindowShapes.Rect;
    public int ShapeParamMm { get; set; }
    [Range(1, 10000)] public int Quantity { get; set; } = 1;
    public string LayoutJson { get; set; } = "{}";
    public string ParamsJson { get; set; } = "{}";
    [StringLength(1000)] public string? Notes { get; set; }
}

// Cam listesi: doğramalardan bağımsız cam siparişi. Her satır bir ölçüdür.
public sealed class GlassList
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Liste adı yazın."), StringLength(160)] public string Name { get; set; } = "";
    public int? CustomerId { get; set; }
    public KarakusAluminyum.Web.Models.Crm.Customer? Customer { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<GlassListItem> Items { get; set; } = new();
}

public sealed class GlassListItem
{
    public int Id { get; set; }
    public int GlassListId { get; set; }
    public GlassList? GlassList { get; set; }
    public int GlassTypeId { get; set; }
    public GlassType? GlassType { get; set; }
    [StringLength(40)] public string? Position { get; set; }
    [Column(TypeName = "numeric(10,1)")] public decimal WidthMm { get; set; }
    [Column(TypeName = "numeric(10,1)")] public decimal HeightMm { get; set; }
    public int Quantity { get; set; } = 1;
    public bool Shaped { get; set; }
    [StringLength(200)] public string? Note { get; set; }
    public int SortOrder { get; set; }
}

public sealed class GlassListInputVm
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Liste adı yazın."), StringLength(160)] public string Name { get; set; } = "";
    public int? CustomerId { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public string ItemsJson { get; set; } = "[]";
}

public sealed class GlassRowInput
{
    public int GlassTypeId { get; set; }
    public string? Position { get; set; }
    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public int Quantity { get; set; } = 1;
    public bool Shaped { get; set; }
    public string? Note { get; set; }
}
