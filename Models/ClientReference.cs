using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

// Sitedeki "Referanslarımız" bölümünde logosu gösterilen müşteri.
public sealed class ClientReference
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Firma adı yazın."), StringLength(160)] public string Name { get; set; } = "";
    [StringLength(500)] public string? LogoPath { get; set; }
    [StringLength(500)] public string? WebsiteUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // "ornek.com" yazılabilir. Yalnızca http/https kabul edilir: javascript: gibi adresler tıklanınca kod çalıştırır.
    public static bool TryNormalizeUrl(string? input, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal) && !text.Contains(':')) text = "https://" + text;
        if (text.Length > 500 || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || !uri.Host.Contains('.') || uri.Host.EndsWith('.')) return false;
        url = uri.AbsoluteUri;
        return true;
    }
}
