namespace WebsiteBuilder.IRF.Infrastructure.Media
{
    public sealed class MediaQuotaOptions
    {
        // Default quota for tenants (bytes). Example: 2GB.
        public long DefaultQuotaBytes { get; set; } = 2L * 1024 * 1024 * 1024;

        // Optional per-tenant override (TenantId -> bytes)
        public Dictionary<Guid, long> TenantQuotaBytes { get; set; } = new();
    }
}
