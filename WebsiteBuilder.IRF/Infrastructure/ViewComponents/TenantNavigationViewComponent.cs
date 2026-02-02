using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.ViewComponents
{
    public sealed class TenantNavigationViewComponent : ViewComponent
    {
        private readonly ITenantNavigationService _nav;

        public TenantNavigationViewComponent(ITenantNavigationService nav)
            => _nav = nav;

        // menuId: 1 = header, 2 = footer (matches your service constants)
        public async Task<IViewComponentResult> InvokeAsync(
            int menuId = 1,
            string? variant = null,
            CancellationToken ct = default)
        {
            var items = await _nav.GetMenuAsync(menuId, ct);

            // Active matching should be based on Request.Path only (query is ignored)
            var currentPath = HttpContext?.Request?.Path.Value?.ToLowerInvariant() ?? "/";

            var resolvedVariant = variant ?? (menuId == 2 ? "footer" : "header");

            var vm = TenantNavigationVm.From(
                items,
                currentPath,
                menuId,
                resolvedVariant);

            return View(vm); // Pages/Shared/Components/TenantNavigation/Default.cshtml
        }
    }
}
