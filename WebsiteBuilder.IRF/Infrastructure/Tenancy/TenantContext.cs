namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;

        public bool IsResolved => TenantId != Guid.Empty;
    }
}
