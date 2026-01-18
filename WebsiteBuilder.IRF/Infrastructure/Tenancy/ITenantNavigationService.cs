namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantNavigationService
    {
        // Backward-compatible: returns Header menu (MenuId = 1)
        Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default);

        // Explicit header/footer fetch
        Task<IReadOnlyList<NavItem>> GetHeaderAsync(CancellationToken ct = default);
        Task<IReadOnlyList<NavItem>> GetFooterAsync(CancellationToken ct = default);

        // New: generic menu fetch (supports future menus)
        Task<IReadOnlyList<NavItem>> GetMenuAsync(int menuId, CancellationToken ct = default);

        // Clears cached nav for the current tenant (header + footer)
        void Invalidate();

        // New: clear cached nav for a specific menu id
        void InvalidateMenu(int menuId);
    }
}
