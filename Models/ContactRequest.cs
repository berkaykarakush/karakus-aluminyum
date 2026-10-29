using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class ContactRequest
{
    [Required(ErrorMessage = "Adınızı ve soyadınızı yazın.")]
    [StringLength(100)]
    [Display(Name = "Ad Soyad")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Telefon veya e-posta bilgisi girin.")]
    [StringLength(120)]
    [Display(Name = "Telefon / E-posta")]
    public string ContactInfo { get; set; } = "";

    [StringLength(120)]
    [Display(Name = "Proje konumu")]
    public string? ProjectLocation { get; set; }

    [StringLength(80)]
    [Display(Name = "İlgilendiğiniz hizmet")]
    public string? Service { get; set; }

    [Required(ErrorMessage = "Projeniz hakkında kısa bir açıklama yazın.")]
    [StringLength(3000, MinimumLength = 10, ErrorMessage = "Mesajınız 10-3000 karakter arasında olmalıdır.")]
    [Display(Name = "Proje detayları")]
    public string Message { get; set; } = "";

    // Honeypot field: should remain empty for normal visitors.
    public string? Website { get; set; }

    [Required(ErrorMessage = "Aydınlatma metnini okuduğunuzu onaylayın.")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "Aydınlatma metnini onaylamanız gerekiyor.")]
    [Display(Name = "KVKK aydınlatma metnini okudum.")]
    public bool PrivacyAccepted { get; set; }
}
