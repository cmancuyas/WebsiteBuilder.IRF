using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Navigation
{
    [ValidateAntiForgeryToken]
    public class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ITenantNavigationService _nav;

        public IndexModel(DataContext db, ITenantContext tenant, ITenantNavigationService nav)
        {
            _db = db;
            _tenant = tenant;
            _nav = nav;
        }

        public sealed class NavPageRowVm
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string Slug { get; set; } = "";
            public int PageStatusId { get; set; }
            public bool ShowInNavigation { get; set; }
            public int NavigationOrder { get; set; }
        }

        public IReadOnlyList<NavPageRowVm> Pages { get; private set; } = Array.Empty<NavPageRowVm>();

        public async Task<IActionResult> OnGetAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            Pages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.IsActive)
                .OrderByDescending(p => p.ShowInNavigation)     // shown first
                .ThenBy(p => p.NavigationOrder == 0 ? int.MaxValue : p.NavigationOrder)
                .ThenBy(p => p.Title)
                .Select(p => new NavPageRowVm
                {
                    Id = p.Id,
                    Title = p.Title ?? "",
                    Slug = p.Slug ?? "",
                    PageStatusId = p.PageStatusId,
                    ShowInNavigation = p.ShowInNavigation,
                    NavigationOrder = p.NavigationOrder
                })
                .ToListAsync(ct);

            return Page();
        }

        public sealed class UpdateNavigationRequest
        {
            public List<int> OrderedVisiblePageIds { get; set; } = new();
            public Dictionary<int, bool> Visibility { get; set; } = new();
        }

        
        public async Task<IActionResult> OnPostUpdateNavigationAsync(
            [FromBody] UpdateNavigationRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request is null)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            // Deduplicate while preserving order (defensive)
            var orderedVisible = request.OrderedVisiblePageIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            // All page ids we will touch = keys in Visibility (source of truth)
            var allIds = request.Visibility.Keys
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (allIds.Count == 0)
                return new JsonResult(new { ok = false, message = "No pages provided." });

            var pages = await _db.Pages
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    allIds.Contains(p.Id) &&
                    !p.IsDeleted)
                .ToListAsync(ct);

            if (pages.Count != allIds.Count)
                return new JsonResult(new { ok = false, message = "One or more pages are invalid for this tenant." });

            // Enforce rule: only Published pages can be shown in navigation
            var errors = new List<string>();
            foreach (var p in pages)
            {
                if (request.Visibility.TryGetValue(p.Id, out var show) && show)
                {
                    if (p.PageStatusId != PageStatusIds.Published)
                    {
                        errors.Add($"Page '{p.Title}' must be Published before it can be shown in navigation.");
                    }
                }
            }

            if (errors.Count > 0)
                return new JsonResult(new { ok = false, message = string.Join(" ", errors) });

            // Snapshot old values to decide whether to invalidate nav cache
            var before = pages.ToDictionary(
                x => x.Id,
                x => (x.ShowInNavigation, x.NavigationOrder));

            // Apply visibility
            foreach (var p in pages)
            {
                if (request.Visibility.TryGetValue(p.Id, out var show))
                    p.ShowInNavigation = show;
                else
                    p.ShowInNavigation = false; // if missing, default off
            }

            // Apply order only for visible pages (1..N)
            // Hidden pages get NavigationOrder = 0
            var setOrder = new HashSet<int>(orderedVisible);

            var order = 1;
            foreach (var pageId in orderedVisible)
            {
                var page = pages.FirstOrDefault(x => x.Id == pageId);
                if (page == null) continue;

                // Only set order if it's actually visible
                if (page.ShowInNavigation)
                {
                    page.NavigationOrder = order;
                    order++;
                }
            }

            // Any remaining visible pages not in orderedVisible: append to end
            foreach (var page in pages
                .Where(p => p.ShowInNavigation && !setOrder.Contains(p.Id))
                .OrderBy(p => p.Title))
            {
                page.NavigationOrder = order;
                order++;
            }

            // Hidden pages: NavigationOrder = 0
            foreach (var page in pages.Where(p => !p.ShowInNavigation))
                page.NavigationOrder = 0;

            // Audit
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userId, out var userGuid);
            var now = DateTime.UtcNow;

            foreach (var p in pages)
            {
                p.UpdatedAt = now;
                if (userGuid != Guid.Empty)
                    p.UpdatedBy = userGuid;
            }

            await _db.SaveChangesAsync(ct);

            // Invalidate navigation cache if anything changed
            var changed = pages.Any(p =>
            {
                var b = before[p.Id];
                return b.ShowInNavigation != p.ShowInNavigation || b.NavigationOrder != p.NavigationOrder;
            });

            if (changed)
                _nav.Invalidate();

            return new JsonResult(new { ok = true });
        }
    }
}
