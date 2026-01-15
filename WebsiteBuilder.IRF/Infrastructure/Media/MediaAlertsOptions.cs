namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaAlertsOptions
{
    public bool Enabled { get; set; } = false;
    public long EligibleBytesThreshold { get; set; } = 5L * 1024 * 1024 * 1024; // 5GB
    public string SlackWebhookUrl { get; set; } = string.Empty;

    // Optional email settings (can leave blank to disable email)
    public string EmailTo { get; set; } = string.Empty;
    public string EmailFrom { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
}
