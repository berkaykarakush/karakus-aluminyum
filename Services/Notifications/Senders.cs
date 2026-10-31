using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KarakusAluminyum.Web.Services.Notifications;

// Yeniden denemenin işe yaramayacağı hata (ör. geçersiz numara). Kuyruk bunu hemen başarısız sayar.
public sealed class PermanentNotificationException(string message) : Exception(message);
public sealed class NotificationNotConfiguredException(string message) : Exception(message);

public sealed record OutgoingEmail(string To, string Subject, string TextBody, string HtmlBody, string? ReplyTo);
public sealed record OutgoingWhatsApp(string To, string? TemplateName, string TemplateLanguage, IReadOnlyList<string> Parameters, string FallbackText);

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task<string?> SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

public interface IWhatsAppSender
{
    bool IsConfigured { get; }
    Task<string?> SendAsync(OutgoingWhatsApp message, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    public SmtpEmailSender(IOptions<NotificationOptions> options) => _options = options.Value.Smtp;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host) && !string.IsNullOrWhiteSpace(_options.From)
        && (_options.Security == "none" || (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password)));

    public async Task<string?> SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new NotificationNotConfiguredException("SMTP ayarları eksik.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.From));
        message.To.Add(MailboxAddress.Parse(email.To));
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20000 };
        var security = _options.Security switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
        };
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.Username)) await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (SmtpCommandException error) when (error.StatusCode is MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable or MailKit.Net.Smtp.SmtpStatusCode.MailboxNameNotAllowed)
        {
            throw new PermanentNotificationException("Alıcı adresi kabul edilmedi.");
        }
        return message.MessageId;
    }
}

public sealed class WhatsAppCloudSender : IWhatsAppSender
{
    public const string ClientName = "WhatsAppCloud";
    private readonly WhatsAppOptions _options;
    private readonly IHttpClientFactory _clients;
    public WhatsAppCloudSender(IOptions<NotificationOptions> options, IHttpClientFactory clients) { _options = options.Value.WhatsApp; _clients = clients; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.AccessToken) && !string.IsNullOrWhiteSpace(_options.PhoneNumberId);

    public async Task<string?> SendAsync(OutgoingWhatsApp message, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new NotificationNotConfiguredException("WhatsApp Cloud API ayarları eksik.");
        var url = $"https://graph.facebook.com/{_options.GraphApiVersion.Trim('/')}/{Uri.EscapeDataString(_options.PhoneNumberId!)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(WhatsAppPayload.Build(message)), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var response = await _clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return WhatsAppPayload.MessageId(body);

        var detail = $"WhatsApp API {(int)response.StatusCode}: {WhatsAppPayload.ErrorText(body)}";
        // Yetki ve hız sınırı düzelebilir; diğer 4xx hataları aynı istekle düzelmez.
        if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout))
            throw new PermanentNotificationException(detail);
        throw new HttpRequestException(detail);
    }
}

public static partial class WhatsAppPayload
{
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();

    // Meta, şablon parametresinde satır sonu, sekme ve art arda boşluk kabul etmez.
    public static string Parameter(string? value, int limit = 200)
    {
        var text = Whitespace().Replace(value ?? "", " ").Trim();
        if (text.Length == 0) return "-";
        return text.Length > limit ? text[..limit] : text;
    }

    public static string Digits(string number) => new(number.Where(char.IsDigit).ToArray());

    public static object Build(OutgoingWhatsApp message)
    {
        var to = Digits(message.To);
        if (string.IsNullOrWhiteSpace(message.TemplateName))
            return new { messaging_product = "whatsapp", recipient_type = "individual", to, type = "text", text = new { preview_url = false, body = message.FallbackText } };
        return new
        {
            messaging_product = "whatsapp", recipient_type = "individual", to, type = "template",
            template = new
            {
                name = message.TemplateName, language = new { code = message.TemplateLanguage },
                components = new[] { new { type = "body", parameters = message.Parameters.Select(x => new { type = "text", text = Parameter(x) }).ToArray() } }
            }
        };
    }

    public static string? MessageId(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("messages", out var list) && list.GetArrayLength() > 0 ? list[0].GetProperty("id").GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public static string ErrorText(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)) return message.GetString() ?? "bilinmeyen hata";
        }
        catch (JsonException) { }
        return "bilinmeyen hata";
    }
}
