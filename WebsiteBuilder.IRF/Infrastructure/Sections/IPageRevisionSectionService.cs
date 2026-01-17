namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface IPageRevisionSectionService
    {
        Task CompactSortOrderAsync(Guid tenantId, int pageRevisionId, CancellationToken ct);
    }
}
