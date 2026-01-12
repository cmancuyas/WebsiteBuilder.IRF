namespace WebsiteBuilder.IRF.Infrastructure.Pages
{
    public interface IPagePublishingService
    {
        Task<PublishResult> PublishAsync(
            int pageId,
            Guid actorUserId,
            CancellationToken ct = default);
    }
}
