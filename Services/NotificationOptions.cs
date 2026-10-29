namespace KarakusAluminyum.Web.Services;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";
    public SmtpOptions Smtp { get; set; } = new();
    public WhatsAppOptions WhatsApp { get; set; } = new();
}
public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}
public sealed class WhatsAppOptions
{
    public string? AccessToken { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? To { get; set; }
    public string GraphApiVersion { get; set; } = "v22.0";
}
