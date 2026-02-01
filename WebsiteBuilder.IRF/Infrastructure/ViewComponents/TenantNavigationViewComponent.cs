using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.ViewComponents
{
    public sealed class TenantNavigationViewComponent : ViewComponent
    {
        private readonly ITenantNavigationService _nav;

        public TenantNavigationViewComponent(ITenantNavigationService nav)
        {
            _nav = nav;
        }

        // menuId: 1 = header, 2 = footer (matches your service constants)
        public async Task<IViewComponentResult> InvokeAsync(int menuId = 1, CancellationToken ct = default)
        {
            var items = await _nav.GetMenuAsync(menuId, ct);
            return View(items); // Pages/Shared/Components/TenantNavigation/Default.cshtml
        }
    }
}
