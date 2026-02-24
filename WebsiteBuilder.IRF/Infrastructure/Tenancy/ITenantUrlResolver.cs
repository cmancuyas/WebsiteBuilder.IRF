namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantUrlResolver
    {
        /// <summary>
        /// Returns canonical (scheme, host) for the current tenant.
        /// - Prefers primary active DomainMapping when available
        /// - Falls back to current request host
        /// </summary>
        Task<(string Scheme, string Host)> GetCanonicalAsync(CancellationToken ct = default);
    }
}
