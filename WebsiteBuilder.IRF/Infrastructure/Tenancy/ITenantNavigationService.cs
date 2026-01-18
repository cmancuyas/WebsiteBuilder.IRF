namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantNavigationService
    {
        // Backward-compatible: returns Header menu (MenuId = 1)
        Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default);

        // New: explicit header/footer fetch
        Task<IReadOnlyList<NavItem>> GetHeaderAsync(CancellationToken ct = default);
        Task<IReadOnlyList<NavItem>> GetFooterAsync(CancellationToken ct = default);

        // Clears cached nav for the current tenant (header + footer)
        void Invalidate();
    }
}
