using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Models;
using Microsoft.Extensions.Options;

namespace KarakusAluminyum.Web.Services;

public sealed class ContactNotificationService : IContactNotificationService
{
    private readonly NotificationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ContactNotificationService> _logger;

    public ContactNotificationService(IOptions<NotificationOptions> options, IHttpClientFactory httpClientFactory, ILogger<ContactNotificationService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task NotifyAsync(ContactRequest request, CancellationToken cancellationToken = default)
    {
        // Form önce veritabanına kaydedilir; bildirim hatası müşterinin talebini kaybettirmez.
        if (IsSmtpConfigured())
        {
            try { await SendEmailAsync(request, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Teklif e-posta bildirimi gönderilemedi."); }
        }
        else _logger.LogWarning("E-posta bildirimi atlandı: SMTP ayarları eksik.");

        if (IsWhatsAppConfigured())
        {
            try { await SendWhatsAppAsync(request, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "WhatsApp bildirimi gönderilemedi."); }
        }
        else _logger.LogWarning("WhatsApp bildirimi atlandı: WhatsApp Cloud API ayarları eksik.");
    }

    private bool IsSmtpConfigured() =>
        !string.IsNullOrWhiteSpace(_options.Smtp.Host) &&
        !string.IsNullOrWhiteSpace(_options.Smtp.Username) &&
        !string.IsNullOrWhiteSpace(_options.Smtp.Password) &&
        !string.IsNullOrWhiteSpace(_options.Smtp.From) &&
        !string.IsNullOrWhiteSpace(_options.Smtp.To);

    private bool IsWhatsAppConfigured() =>
        !string.IsNullOrWhiteSpace(_options.WhatsApp.AccessToken) &&
        !string.IsNullOrWhiteSpace(_options.WhatsApp.PhoneNumberId) &&
        !string.IsNullOrWhiteSpace(_options.WhatsApp.To);

    private async Task SendEmailAsync(ContactRequest request, CancellationToken cancellationToken)
    {
        var smtp = _options.Smtp;
        var body = new StringBuilder()
            .AppendLine("Karakuş Alüminyum web sitesinden yeni bir teklif talebi geldi.")
            .AppendLine().AppendLine($"Ad Soyad: {Safe(request.Name)}")
            .AppendLine($"Telefon / E-posta: {Safe(request.ContactInfo)}")
            .AppendLine($"Hizmet: {Safe(request.Service)}")
            .AppendLine($"Proje Konumu: {Safe(request.ProjectLocation)}")
            .AppendLine().AppendLine("Mesaj:").AppendLine(Safe(request.Message)).ToString();
        using var mail = new MailMessage(smtp.From!, smtp.To!, $"Yeni teklif talebi - {Safe(request.Name)}", body)
        {
            IsBodyHtml = false, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8
        };
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = true, DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtp.Username, smtp.Password), Timeout = 15000
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(mail);
        _logger.LogInformation("Yeni teklif bildirimi e-posta ile gönderildi.");
    }

    private async Task SendWhatsAppAsync(ContactRequest request, CancellationToken cancellationToken)
    {
        var wa = _options.WhatsApp;
        var version = wa.GraphApiVersion.Trim('/');
        var url = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(wa.PhoneNumberId!)}/messages";
        var body = string.Join("\n", new[]
        {
            "Karakuş Alüminyum - Yeni teklif talebi", $"Ad Soyad: {Safe(request.Name)}",
            $"İletişim: {Safe(request.ContactInfo)}", $"Hizmet: {Safe(request.Service)}",
            $"Konum: {Safe(request.ProjectLocation)}", $"Mesaj: {Safe(request.Message)}"
        });
        var payload = new { messaging_product = "whatsapp", recipient_type = "individual", to = new string(wa.To!.Where(char.IsDigit).ToArray()), type = "text", text = new { preview_url = false, body } };
        using var client = _httpClientFactory.CreateClient(nameof(ContactNotificationService));
        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wa.AccessToken);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("WhatsApp API bildirimi başarısız. HTTP {StatusCode}; yanıt: {ResponseBody}", (int)response.StatusCode, responseBody);
            return;
        }
        _logger.LogInformation("Yeni teklif bildirimi WhatsApp API'ye gönderildi.");
    }
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
