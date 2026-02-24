namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantContext
    {
        Guid TenantId { get; set; }
        string Slug { get; set; }

        // incoming host
        string Host { get; set; }

        // canonical primary host for tenant
        string PrimaryHost { get; set; }

        bool IsResolved { get; }
    }

}
