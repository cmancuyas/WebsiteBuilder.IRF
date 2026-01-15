using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class CompositeMediaAlertNotifier : IMediaAlertNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MediaAlertsOptions _opts;

    public CompositeMediaAlertNotifier(IHttpClientFactory httpClientFactory, IOptions<MediaAlertsOptions> opts)
    {
        _httpClientFactory = httpClientFactory;
        _opts = opts.Value;
    }

    public async Task NotifyAsync(string subject, string message, CancellationToken ct)
    {
        if (!_opts.Enabled)
            return;

        // Slack (best effort)
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
                // swallow: alerts must not break cleanup
            }
        }

        // Email can be added next (SMTP). For now, keep it best-effort and optional.
    }
}
