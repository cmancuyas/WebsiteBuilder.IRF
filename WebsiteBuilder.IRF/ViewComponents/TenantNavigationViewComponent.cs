using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.ViewComponents
{
    [ViewComponent(Name = "TenantNavigation")]
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

            // Optional defensive deep clone (safe even if someone changes NavItem later)
            var safeItems = Clone(items);

            var currentPath = HttpContext?.Request?.Path.Value ?? "/";
            var resolvedVariant = variant ?? (menuId == 2 ? "footer" : "header");

            var vm = TenantNavigationVm.From(safeItems, currentPath, menuId, resolvedVariant);

            return View(vm);
        }

        private static IReadOnlyList<NavItem> Clone(IReadOnlyList<NavItem>? src)
        {
            if (src == null || src.Count == 0)
                return Array.Empty<NavItem>();

            var list = new List<NavItem>(src.Count);
            for (var i = 0; i < src.Count; i++)
                list.Add(CloneItem(src[i]));

            return list;
        }

        private static NavItem CloneItem(NavItem n)
        {
            IReadOnlyList<NavItem> kids = (n.Children != null && n.Children.Count > 0)
                ? Clone(n.Children)
                : Array.Empty<NavItem>();

            return new NavItem(
                n.Title,
                n.Url,
                n.Order,
                n.OpenInNewTab,
                kids
            );
        }
    }
}
