namespace WebsiteBuilder.IRF.Infrastructure.Sitemap
{
    public interface ITenantSitemapIndexService
    {
        Task<string> GetSitemapIndexXmlAsync(CancellationToken ct = default);
        Task<string> GetSitemapPartXmlAsync(int part, CancellationToken ct = default);
        void Invalidate();
    }
}
