using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class CompositeMediaAlertNotifier : IMediaAlertNotifier
{
    private readonly DbMediaAlertNotifier _dbNotifier;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MediaAlertsOptions _opts;

    public CompositeMediaAlertNotifier(
        DbMediaAlertNotifier dbNotifier,
        IHttpClientFactory httpClientFactory,
        IOptions<MediaAlertsOptions> opts)
    {
        _dbNotifier = dbNotifier;
        _httpClientFactory = httpClientFactory;
        _opts = opts.Value;
    }

    public async Task NotifyAsync(string subject, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(message))
            return;

        // 1) DB (best effort): keep durable history even if Slack is disabled
        try
        {
            await _dbNotifier.NotifyAsync(subject, message, ct);
        }
        catch
        {
            // swallow: alerts must not break cleanup/publish flows
        }

        // 2) Slack (best effort): controlled by options
        if (!_opts.Enabled)
            return;

        if (!string.IsNullOrWhiteSpace(_opts.SlackWebhookUrl))
        {
            try
            {
                var http = _httpClientFactory.CreateClient();
                var payload = new { text = $"*{subject}*\n{message}" };
                await http.PostAsJsonAsync(_opts.SlackWebhookUrl, payload, ct);
            }
            catch
            {
                // swallow
            }
        }

        // Email can be added later (SMTP / SendGrid / SES)
    }
}
