using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
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

        // -----------------------------
        // TAB A: Pages-based navigation
        // -----------------------------
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

        // -----------------------------------------
        // TAB B: NavigationMenuItems (Step 1 + Step 2)
        // -----------------------------------------
        public sealed class NavNode
        {
            public required NavigationMenuItem Item { get; init; }
            public List<NavNode> Children { get; } = new();
        }
        public IReadOnlyList<NavigationMenuItem> MenuItemsFlat { get; private set; } = Array.Empty<NavigationMenuItem>();
        public IReadOnlyDictionary<int, IReadOnlyList<NavNode>> MenuTreesByMenuId { get; private set; }
            = new Dictionary<int, IReadOnlyList<NavNode>>();

        // Published pages for "Internal page" dropdown
        public sealed class PublishedPageOption
        {
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public string Slug { get; set; } = "";
        }

        public IReadOnlyList<PublishedPageOption> PublishedPages { get; private set; } = Array.Empty<PublishedPageOption>();

        public static readonly IReadOnlyDictionary<int, string> MenuNames = new Dictionary<int, string>
        {
            [1] = "Header",
            [2] = "Footer"
        };
        public IReadOnlyDictionary<int, string> MenuNamesMap => MenuNames;
        public async Task<IActionResult> OnGetAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // A) Pages tab (unchanged)
            Pages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.IsActive)
                .OrderByDescending(p => p.ShowInNavigation)
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

            // Published pages for internal linking (Published only)
            PublishedPages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive && !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new PublishedPageOption
                {
                    Id = p.Id,
                    Title = p.Title ?? "",
                    Slug = p.Slug ?? ""
                })
                .ToListAsync(ct);

            // Menu items (draft working set)
            MenuItemsFlat = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted)
                .OrderBy(x => x.MenuId)
                .ThenBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            MenuTreesByMenuId = BuildTreesByMenu(MenuItemsFlat);

            return Page();
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<NavNode>> BuildTreesByMenu(IReadOnlyList<NavigationMenuItem> flat)
        {
            var result = new Dictionary<int, IReadOnlyList<NavNode>>();

            foreach (var grp in flat.GroupBy(x => x.MenuId).OrderBy(g => g.Key))
            {
                var items = grp.ToList();

                var nodesById = items.ToDictionary(
                    x => x.Id,
                    x => new NavNode { Item = x });

                var roots = new List<NavNode>();

                foreach (var item in items)
                {
                    var node = nodesById[item.Id];

                    if (item.ParentId.HasValue && nodesById.TryGetValue(item.ParentId.Value, out var parent))
                        parent.Children.Add(node);
                    else
                        roots.Add(node);
                }

                result[grp.Key] = roots;
            }

            return result;
        }

        // -----------------------------
        // Existing Save handler (unchanged)
        // -----------------------------
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

            var orderedVisible = request.OrderedVisiblePageIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

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

            var errors = new List<string>();
            foreach (var p in pages)
            {
                if (request.Visibility.TryGetValue(p.Id, out var show) && show)
                {
                    if (p.PageStatusId != PageStatusIds.Published)
                        errors.Add($"Page '{p.Title}' must be Published before it can be shown in navigation.");
                }
            }

            if (errors.Count > 0)
                return new JsonResult(new { ok = false, message = string.Join(" ", errors) });

            var before = pages.ToDictionary(x => x.Id, x => (x.ShowInNavigation, x.NavigationOrder));

            foreach (var p in pages)
                p.ShowInNavigation = request.Visibility.TryGetValue(p.Id, out var show) && show;

            var setOrder = new HashSet<int>(orderedVisible);

            var order = 1;
            foreach (var pageId in orderedVisible)
            {
                var page = pages.FirstOrDefault(x => x.Id == pageId);
                if (page == null) continue;

                if (page.ShowInNavigation)
                {
                    page.NavigationOrder = order;
                    order++;
                }
            }

            foreach (var page in pages
                .Where(p => p.ShowInNavigation && !setOrder.Contains(p.Id))
                .OrderBy(p => p.Title))
            {
                page.NavigationOrder = order;
                order++;
            }

            foreach (var page in pages.Where(p => !p.ShowInNavigation))
                page.NavigationOrder = 0;

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

            var changed = pages.Any(p =>
            {
                var b = before[p.Id];
                return b.ShowInNavigation != p.ShowInNavigation || b.NavigationOrder != p.NavigationOrder;
            });

            if (changed)
                _nav.Invalidate();

            return new JsonResult(new { ok = true });
        }

        // -----------------------------
        // Step 2: Menu Item CRUD
        // -----------------------------

        public sealed class UpsertMenuItemRequest
        {
            public int? Id { get; set; }                // null => create
            public int MenuId { get; set; }             // 1=Header, 2=Footer
            public int? ParentId { get; set; }          // optional
            public string Label { get; set; } = "";
            public string LinkType { get; set; } = "";  // "internal" or "external"
            public int? PageId { get; set; }            // internal
            public string? Url { get; set; }            // external
            public bool OpenInNewTab { get; set; }
        }

        public async Task<IActionResult> OnPostUpsertMenuItemAsync(
            [FromBody] UpsertMenuItemRequest req,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (req is null)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            // Minimal validation
            req.Label = (req.Label ?? "").Trim();
            req.LinkType = (req.LinkType ?? "").Trim().ToLowerInvariant();
            req.Url = (req.Url ?? "").Trim();

            if (!MenuNames.ContainsKey(req.MenuId))
                return new JsonResult(new { ok = false, message = "Invalid menu selected." });

            if (string.IsNullOrWhiteSpace(req.Label))
                return new JsonResult(new { ok = false, message = "Label is required." });

            if (req.Label.Length > 200)
                return new JsonResult(new { ok = false, message = "Label is too long (max 200)." });

            if (req.LinkType != "internal" && req.LinkType != "external")
                return new JsonResult(new { ok = false, message = "Invalid link type." });

            if (req.LinkType == "internal")
            {
                if (req.PageId is null || req.PageId.Value <= 0)
                    return new JsonResult(new { ok = false, message = "Please select a published page." });

                var pageOk = await _db.Pages
                    .AsNoTracking()
                    .AnyAsync(p =>
                        p.Id == req.PageId.Value &&
                        p.TenantId == _tenant.TenantId &&
                        p.IsActive && !p.IsDeleted &&
                        p.PageStatusId == PageStatusIds.Published, ct);

                if (!pageOk)
                    return new JsonResult(new { ok = false, message = "Selected page is not a published page for this tenant." });

                // Optional: keep Url empty for internal (we resolve by PageId later)
                req.Url = "";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.Url))
                    return new JsonResult(new { ok = false, message = "External URL is required." });

                if (req.Url!.Length > 500)
                    return new JsonResult(new { ok = false, message = "URL is too long (max 500)." });

                req.PageId = null;
            }

            // Parent validation: must belong to same tenant + same menu + not deleted + not self
            if (req.ParentId.HasValue)
            {
                if (req.Id.HasValue && req.ParentId.Value == req.Id.Value)
                    return new JsonResult(new { ok = false, message = "Parent cannot be the item itself." });

                var parentOk = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.Id == req.ParentId.Value &&
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId, ct);

                if (!parentOk)
                    return new JsonResult(new { ok = false, message = "Invalid parent item for the selected menu." });
            }
            if (req.LinkType == "internal" && req.PageId.HasValue)
            {
                var duplicate = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId &&
                        x.PageId == req.PageId &&
                        (!req.Id.HasValue || x.Id != req.Id.Value),
                        ct);

                if (duplicate)
                    return new JsonResult(new
                    {
                        ok = false,
                        message = "This page already exists in the selected menu."
                    });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userId, out var userGuid);
            var now = DateTime.UtcNow;

            NavigationMenuItem entity;

            if (req.Id.HasValue && req.Id.Value > 0)
            {
                entity = await _db.NavigationMenuItems
                    .FirstOrDefaultAsync(x =>
                        x.Id == req.Id.Value &&
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted, ct);

                if (entity is null)
                    return new JsonResult(new { ok = false, message = "Menu item not found." });

                entity.MenuId = req.MenuId;
                entity.ParentId = req.ParentId;
                entity.Label = req.Label;
                entity.PageId = req.PageId;
                entity.Url = req.Url ?? "";
                entity.OpenInNewTab = req.OpenInNewTab;

                entity.UpdatedAt = now;
                if (userGuid != Guid.Empty) entity.UpdatedBy = userGuid;
            }
            else
            {
                // Default SortOrder: append to end within (MenuId, ParentId)
                var maxSort = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .Where(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId &&
                        x.ParentId == req.ParentId)
                    .MaxAsync(x => (int?)x.SortOrder, ct);

                var nextSort = (maxSort ?? 0) + 1;

                entity = new NavigationMenuItem
                {
                    TenantId = _tenant.TenantId,
                    MenuId = req.MenuId,
                    ParentId = req.ParentId,
                    SortOrder = nextSort,
                    Label = req.Label,
                    PageId = req.PageId,
                    Url = req.Url ?? "",
                    OpenInNewTab = req.OpenInNewTab,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    CreatedBy = userGuid == Guid.Empty ? Guid.Empty : userGuid
                };

                await _db.NavigationMenuItems.AddAsync(entity, ct);
            }

            await _db.SaveChangesAsync(ct);

            // invalidate navigation cache since draft set changed
            _nav.Invalidate();

            return new JsonResult(new { ok = true, id = entity.Id });
        }

        public sealed class DeleteMenuItemRequest
        {
            public int Id { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteMenuItemAsync(
            [FromBody] DeleteMenuItemRequest req,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (req is null || req.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            var item = await _db.NavigationMenuItems
                .FirstOrDefaultAsync(x =>
                    x.Id == req.Id &&
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted, ct);

            if (item is null)
                return new JsonResult(new { ok = false, message = "Menu item not found." });

            // Soft delete (Step 2 rule)
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userId, out var userGuid);
            var now = DateTime.UtcNow;

            item.IsDeleted = true;
            item.DeletedAt = now;
            if (userGuid != Guid.Empty) item.DeletedBy = userGuid;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            return new JsonResult(new { ok = true });
        }
    }
}
