namespace WebsiteBuilder.IRF.Infrastructure.Sitemap
{
    public interface ITenantSitemapService
    {
        Task<string> GetSitemapXmlAsync(CancellationToken ct = default);

        // keep it simple: tenant-scoped cache invalidation
        void Invalidate();
    }
}
