using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

// Proje gönderisinin türü: tamamlanan iş (Referans Projelerimiz) veya şantiyesi süren iş (Devam Eden Projelerimiz).
// Panelde iki ayrı sekmedir; görsel düzeni ve düzenleyicisi aynıdır, yalnızca etiket, metin ve dosya adı değişir.
public sealed record SocialProjectKind(string Key, string Segment, string Label, string Tab, string Singular, string FilePrefix)
{
    public static readonly SocialProjectKind Reference = new("reference", "referans-projeler", "Referans Projelerimiz", "Referans projeler", "referans proje", "referans");
    public static readonly SocialProjectKind Ongoing = new("ongoing", "devam-eden-projeler", "Devam Eden Projelerimiz", "Devam eden projeler", "devam eden proje", "devam-eden");
    public static readonly SocialProjectKind[] All = { Reference, Ongoing };
    public static SocialProjectKind? FromSegment(string? segment) => All.FirstOrDefault(k => k.Segment == segment);
    public static SocialProjectKind FromKey(string? key) => All.FirstOrDefault(k => k.Key == key) ?? Reference;
}

// Sosyal medyadaki proje gönderisi (bkz. SocialProjectKind). Görsel tarayıcıda fotoğraf + proje adından üretilir;
// fotoğrafın ve yazının yeri saklanır, böylece gönderi sonradan yeniden düzenlenebilir.
public sealed class SocialProjectPost
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Proje adı yazın."), StringLength(120, ErrorMessage = "Proje adı en fazla 120 karakter olabilir.")] public string Name { get; set; } = "";
    [StringLength(2200, ErrorMessage = "Açıklama en fazla 2200 karakter olabilir.")] public string? Caption { get; set; } // Instagram sınırı
    [StringLength(500)] public string PhotoPath { get; set; } = "";
    [StringLength(500)] public string ImagePath { get; set; } = "";
    // 0-1 arası: yazı bloğunun kenar boşlukları içindeki yeri ve fotoğrafın kadraj odağı (CSS object-position gibi).
    [Range(0, 1)] public double TextX { get; set; }
    [Range(0, 1)] public double TextY { get; set; }
    [Range(0, 1)] public double FocusX { get; set; } = 0.5;
    [Range(0, 1)] public double FocusY { get; set; } = 0.5;
    [Range(0.6, 1.4)] public double TextScale { get; set; } = 1;
    [StringLength(20)] public string Kind { get; set; } = SocialProjectKind.Reference.Key;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public static string DefaultCaption(string name, string kind = "reference") => kind == SocialProjectKind.Ongoing.Key
        ? $"Devam eden projelerimizden: {name}.\n\nŞantiyede imalat ve montaj çalışmalarımız sürüyor; ilerleyişi buradan paylaşmaya devam edeceğiz.\n\nSizin projeniz için de ölçü ve teklif almak isterseniz bize yazın.\n\n#KarakuşAlüminyum #İzmir #Bornova #DevamEdenProje #AlüminyumDoğrama"
        : $"Referans projelerimizden: {name}.\n\nBu projede uygulanan sistemleri Karakuş Alüminyum olarak hayata geçirdik.\n\nSizin projeniz için de ölçü ve teklif almak isterseniz bize yazın.\n\n#KarakuşAlüminyum #İzmir #Bornova #ReferansProje #AlüminyumDoğrama";
}
