namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantContext
    {
        Guid TenantId { get; set; }
        string Slug { get; set; }
        string Host { get; set; }
        bool IsResolved { get; }
    }

}
