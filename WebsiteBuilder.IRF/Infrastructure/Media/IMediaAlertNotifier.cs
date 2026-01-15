namespace WebsiteBuilder.IRF.Infrastructure.Media;

public interface IMediaAlertNotifier
{
    Task NotifyAsync(string subject, string message, CancellationToken ct);
}
