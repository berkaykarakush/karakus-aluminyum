using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Models;

namespace KarakusAluminyum.Web.Services.Notifications;

public static partial class NotificationComposer
{
    [GeneratedRegex(@"^[^@\s,;<>]+@[^@\s,;<>]+\.[^@\s,;<>]+$")] private static partial Regex EmailPattern();
    [GeneratedRegex(@"[\r\n\t]+")] private static partial Regex LineBreaks();

    public static bool IsEmail(string? value) => value is not null && EmailPattern().IsMatch(value.Trim());

    // Türkiye numaraları için sade biçim; telefon gibi görünmeyen metinde null döner.
    public static string? PhoneDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsEmail(value) || Regex.IsMatch(value, "[^0-9+() .-]")) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '0') digits = "9" + digits;
        else if (digits.Length == 10 && digits[0] == '5') digits = "90" + digits;
        return digits.Length is >= 10 and <= 15 ? digits : null;
    }

    public static IEnumerable<string> Split(string? list) => (list ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Line(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : LineBreaks().Replace(value, " ").Trim();
    private static string H(string? value) => WebUtility.HtmlEncode(Line(value));

    public static OutgoingEmail AdminEmail(ContactMessage m, string to, SiteSettings site)
    {
        var panel = $"{site.BaseUrl.TrimEnd('/')}/yonetim/mesajlar/{m.Id}";
        var subject = Line($"Yeni teklif talebi: {Line(m.Name)}{(string.IsNullOrWhiteSpace(m.Service) ? "" : " - " + Line(m.Service))}");
        if (subject.Length > 150) subject = subject[..150];
        var text = new StringBuilder()
            .AppendLine($"{site.SiteName} web sitesinden yeni bir teklif talebi geldi.").AppendLine()
            .AppendLine($"Ad Soyad: {Line(m.Name)}").AppendLine($"İletişim: {Line(m.ContactInfo)}").AppendLine($"Hizmet: {Line(m.Service)}").AppendLine($"Proje konumu: {Line(m.ProjectLocation)}").AppendLine()
            .AppendLine("Mesaj:").AppendLine(m.Message.Trim()).AppendLine().AppendLine($"Panelde görüntüle: {panel}").ToString();
        string Row(string label, string value) => $"<tr><td style=\"padding:8px 12px;color:#5b6872;width:130px\">{label}</td><td style=\"padding:8px 12px;color:#111\">{value}</td></tr>";
        var contact = H(m.ContactInfo);
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:620px;margin:auto;border:1px solid #e3e6e8;border-radius:10px;overflow:hidden">
              <div style="background:#0d1115;color:#fff;padding:16px 20px;font-size:16px;font-weight:700">Yeni teklif talebi</div>
              <table style="width:100%;border-collapse:collapse;font-size:14px">
                {Row("Ad Soyad", H(m.Name))}{Row("İletişim", contact)}{Row("Hizmet", H(m.Service))}{Row("Proje konumu", H(m.ProjectLocation))}
              </table>
              <div style="padding:12px 20px;font-size:14px;color:#111;white-space:pre-wrap;border-top:1px solid #e3e6e8">{WebUtility.HtmlEncode(m.Message.Trim())}</div>
              <div style="padding:16px 20px"><a href="{WebUtility.HtmlEncode(panel)}" style="background:#2c5f7c;color:#fff;padding:10px 16px;border-radius:7px;text-decoration:none;font-size:14px">Panelde görüntüle</a></div>
            </div>
            """;
        return new OutgoingEmail(to, subject, text, html, IsEmail(m.ContactInfo) ? m.ContactInfo.Trim() : null);
    }

    // Adres doğrulanmadığı için formdaki serbest metin bu e-postaya yazılmaz: form, başkasına içerik göndermek için kullanılamaz.
    public static OutgoingEmail CustomerEmail(string to, SiteSettings site)
    {
        var subject = $"Talebinizi aldık | {site.SiteName}";
        var text = $"Merhaba,\n\nTeklif talebiniz bize ulaştı. Ekibimiz talebinizi inceleyip en kısa sürede sizinle iletişime geçecek.\n\nBu arada bize {site.PhoneDisplay} numarasından veya {site.Email} adresinden ulaşabilirsiniz.\n\nBu talebi siz oluşturmadıysanız bu e-postayı dikkate almayın.\n\n{site.SiteName}\n{site.BaseUrl}\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#111;font-size:15px;line-height:1.6">
              <p>Merhaba,</p>
              <p>Teklif talebiniz bize ulaştı. Ekibimiz talebinizi inceleyip en kısa sürede sizinle iletişime geçecek.</p>
              <p>Bu arada bize <strong>{H(site.PhoneDisplay)}</strong> numarasından veya <strong>{H(site.Email)}</strong> adresinden ulaşabilirsiniz.</p>
              <p style="color:#5b6872;font-size:13px">Bu talebi siz oluşturmadıysanız bu e-postayı dikkate almayın.</p>
              <p><strong>{H(site.SiteName)}</strong><br /><a href="{WebUtility.HtmlEncode(site.BaseUrl)}">{H(site.BaseUrl)}</a></p>
            </div>
            """;
        return new OutgoingEmail(to, subject, text, html, IsEmail(site.Email) ? site.Email : null);
    }

    public static OutgoingWhatsApp AdminWhatsApp(ContactMessage m, string to, SiteSettings site)
    {
        var parameters = new[] { m.Name, m.ContactInfo, string.IsNullOrWhiteSpace(m.Service) ? "-" : m.Service, string.IsNullOrWhiteSpace(m.ProjectLocation) ? "-" : m.ProjectLocation };
        var fallback = $"{site.SiteName} - Yeni teklif talebi\nAd Soyad: {Line(m.Name)}\nİletişim: {Line(m.ContactInfo)}\nHizmet: {Line(m.Service)}\nKonum: {Line(m.ProjectLocation)}";
        return new OutgoingWhatsApp(to, site.WhatsAppTemplateName, string.IsNullOrWhiteSpace(site.WhatsAppTemplateLanguage) ? "tr" : site.WhatsAppTemplateLanguage, parameters, fallback);
    }
}
