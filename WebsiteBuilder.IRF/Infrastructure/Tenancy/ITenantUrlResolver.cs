namespace WebsiteBuilder.IRF.Infrastructure.Rendering
{
    public interface ITenantUrlResolver
    {
        Task<(string Scheme, string Host)> GetCanonicalAsync(CancellationToken ct = default);
    }
}