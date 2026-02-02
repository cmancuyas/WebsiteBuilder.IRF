using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.ViewComponents
{
    public sealed class TenantNavigationViewComponent : ViewComponent
    {
        private readonly ITenantNavigationService _nav;

        public TenantNavigationViewComponent(ITenantNavigationService nav)
            => _nav = nav;

        public async Task<IViewComponentResult> InvokeAsync(
            int menuId = 1,
            string? variant = null,
            CancellationToken ct = default)
        {
            var items = await _nav.GetMenuAsync(menuId, ct);

            // Pass raw Request.Path; VM handles normalization (slashes, /home, casing, etc.)
            var currentPath = HttpContext?.Request?.Path.Value ?? "/";

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
