namespace WebsiteBuilder.IRF.Infrastructure.Media
{
    public interface IMediaCleanupRunner
    {
        Task<MediaCleanupResult> RunOnceAsync(CancellationToken ct);
    }
}
