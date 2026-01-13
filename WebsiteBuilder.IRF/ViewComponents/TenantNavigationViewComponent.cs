using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.ViewComponents
{
    public sealed class TenantNavigationViewComponent : ViewComponent
    {
        private readonly ITenantNavigationService _nav;

        public TenantNavigationViewComponent(ITenantNavigationService nav)
        {
            _nav = nav;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var links = await _nav.GetNavAsync(HttpContext.RequestAborted);
            return View(links);
        }
    }
}
