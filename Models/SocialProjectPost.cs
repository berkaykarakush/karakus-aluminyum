using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

// Sosyal medyadaki "Referans Projelerimiz" gönderisi. Görsel tarayıcıda fotoğraf + proje adından üretilir;
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
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public static string DefaultCaption(string name) =>
        $"Referans projelerimizden: {name}.\n\nBu projede uygulanan sistemleri Karakuş Alüminyum olarak hayata geçirdik.\n\nSizin projeniz için de ölçü ve teklif almak isterseniz bize yazın.\n\n#KarakuşAlüminyum #İzmir #Bornova #ReferansProje #AlüminyumDoğrama";
}
