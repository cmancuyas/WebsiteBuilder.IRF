namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantNavigationService
    {
        Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default);
    }
}
