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
    public class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public IndexModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public sealed record PublishedPageVm(int Id, string Title, string? Slug);

        public sealed record MenuItemVm(
            int Id,
            int MenuId,
            int? ParentId,
            int SortOrder,
            string Label,
            int? PageId,
            string? Url,
            bool OpenInNewTab
        );

        public sealed class NavNode
        {
            public required MenuItemVm Item { get; init; }
            public IReadOnlyList<NavNode> Children { get; init; } = Array.Empty<NavNode>();
        }

        public List<PublishedPageVm> PublishedPages { get; private set; } = new();
        public List<MenuItemVm> MenuItemsFlat { get; private set; } = new();

        public IReadOnlyDictionary<int, IReadOnlyList<NavNode>> TreesByMenu { get; private set; }
            = new Dictionary<int, IReadOnlyList<NavNode>>();

        private static readonly IReadOnlyDictionary<int, string> MenuNames = new Dictionary<int, string>
        {
            [1] = "Header",
            [2] = "Footer"
        };
        // ============================
        // System-route blocking helpers
        // ============================

        private static readonly HashSet<string> BlockedSlugs = new(StringComparer.OrdinalIgnoreCase)
{
    // Admin/system
    "admin",
    "navigation",
    "pages",
    "media",
    "account",
    "login",
    "logout",
    "register",
    "accessdenied",
    "error",
    "health",
    "swagger",

    // internal-only platform areas (adjust to your app)
    "platform"
};

        private static bool IsBlockedSlug(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return false;

            var s = slug.Trim().Trim('/');

            if (BlockedSlugs.Contains(s))
                return true;

            // prefixes
            if (s.StartsWith("admin", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("api", StringComparison.OrdinalIgnoreCase)) return true;

            // framework/static folders
            if (s.StartsWith("_framework", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("css", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("js", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("lib", StringComparison.OrdinalIgnoreCase)) return true;

            // suspicious
            if (s.StartsWith(".", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.StartsWith("_", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsBlockedRelativeUrl(string relativeUrl)
        {
            var p = (relativeUrl ?? "").Trim();

            // only block app-relative paths; absolute URLs allowed
            if (!p.StartsWith("/"))
                return false;

            p = p.TrimEnd('/');
            if (p.Length == 0)
                return false;

            var seg = p.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return IsBlockedSlug(seg);
        }

        private static bool IsSystemPage(string? layoutKey, string? slug)
        {
            if (!string.IsNullOrWhiteSpace(layoutKey))
            {
                var lk = layoutKey.Trim();
                if (lk.Equals("Navigation", StringComparison.OrdinalIgnoreCase) ||
                    lk.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    lk.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                    lk.Equals("Platform", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return IsBlockedSlug(slug);
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            // Published pages for internal link dropdown
            PublishedPages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.IsActive &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new PublishedPageVm(p.Id, p.Title, p.Slug))
                .ToListAsync(ct);

            // Menu items (flat, ordered)
            MenuItemsFlat = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted)
                .OrderBy(x => x.MenuId)
                .ThenBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new MenuItemVm(
                    x.Id,
                    x.MenuId,
                    x.ParentId,
                    x.SortOrder,
                    x.Label,
                    x.PageId,
                    x.Url,
                    x.OpenInNewTab
                ))
                .ToListAsync(ct);

            TreesByMenu = BuildTrees(MenuItemsFlat);

            return Page();
        }

        private static IReadOnlyDictionary<int, IReadOnlyList<NavNode>> BuildTrees(IReadOnlyList<MenuItemVm> items)
        {
            const int RootKey = 0;

            var byMenu = items
                .GroupBy(x => x.MenuId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<int, IReadOnlyList<NavNode>>();

            foreach (var kvp in byMenu)
            {
                var menuId = kvp.Key;
                var menuItems = kvp.Value;

                // Non-nullable dictionary key: ParentId ?? RootKey
                var byParent = menuItems
                    .GroupBy(x => x.ParentId ?? RootKey)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList()
                    );

                NavNode MapItem(MenuItemVm mi, HashSet<int> visiting, int depth)
                {
                    // depth guard (prevents runaway if bad data)
                    if (depth > 25)
                        return new NavNode { Item = mi, Children = Array.Empty<NavNode>() };

                    if (!visiting.Add(mi.Id))
                        return new NavNode { Item = mi, Children = Array.Empty<NavNode>() };

                    IReadOnlyList<NavNode> children = byParent.TryGetValue(mi.Id, out var kids)
                        ? (IReadOnlyList<NavNode>)kids.Select(k => MapItem(k, visiting, depth + 1)).ToList()
                        : Array.Empty<NavNode>();

                    visiting.Remove(mi.Id);

                    return new NavNode { Item = mi, Children = children };
                }

                IReadOnlyList<NavNode> roots = byParent.TryGetValue(RootKey, out var rootRows)
                    ? (IReadOnlyList<NavNode>)rootRows.Select(r => MapItem(r, new HashSet<int>(), 0)).ToList()
                    : Array.Empty<NavNode>();

                result[menuId] = roots;
            }

            // Ensure menu buckets exist even if empty
            if (!result.ContainsKey(1)) result[1] = Array.Empty<NavNode>();
            if (!result.ContainsKey(2)) result[2] = Array.Empty<NavNode>();

            return result;
        }

        // =========================
        // AJAX Handlers (Step 2/3)
        // =========================

        public sealed class UpsertMenuItemRequest
        {
            public int? Id { get; set; }
            public int MenuId { get; set; }
            public int? ParentId { get; set; }
            public string? Label { get; set; }
            public string? LinkType { get; set; } // "internal" | "external"
            public int? PageId { get; set; }
            public string? Url { get; set; }
            public bool OpenInNewTab { get; set; }
        }

        public async Task<IActionResult> OnPostUpsertMenuItemAsync([FromBody] UpsertMenuItemRequest req, CancellationToken ct)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (!MenuNames.ContainsKey(req.MenuId))
                return new JsonResult(new { ok = false, message = "Invalid menu selected." });

            var label = (req.Label ?? "").Trim();
            if (string.IsNullOrWhiteSpace(label))
                return new JsonResult(new { ok = false, message = "Label is required." });
            if (label.Length > 100)
                return new JsonResult(new { ok = false, message = "Label is too long (max 100)." });

            var linkType = (req.LinkType ?? "").Trim().ToLowerInvariant();
            if (linkType != "internal" && linkType != "external")
                return new JsonResult(new { ok = false, message = "Invalid link type." });

            int? pageId = null;
            string? url = null;

            // ======================================================
            // Server-side rule: Block system pages / system routes
            // ======================================================

           
           

           

            if (linkType == "internal")
            {
                if (!req.PageId.HasValue || req.PageId.Value <= 0)
                    return new JsonResult(new { ok = false, message = "Published page is required for internal links." });

                var page = await _db.Pages
                    .AsNoTracking()
                    .Where(p =>
                        p.TenantId == _tenant.TenantId &&
                        p.Id == req.PageId.Value &&
                        !p.IsDeleted &&
                        p.IsActive &&
                        p.PageStatusId == PageStatusIds.Published)
                    .Select(p => new { p.Id, p.LayoutKey, p.Slug })
                    .FirstOrDefaultAsync(ct);

                if (page == null)
                    return new JsonResult(new { ok = false, message = "Selected page is invalid or not published." });

                if (IsSystemPage(page.LayoutKey, page.Slug))
                    return new JsonResult(new { ok = false, message = "System pages cannot be added to navigation." });

                pageId = page.Id;
                url = null;
            }
            else
            {
                url = (req.Url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url))
                    return new JsonResult(new { ok = false, message = "URL is required for external links." });

                // Block relative URLs that point into system areas
                // (Absolute URLs like https://example.com are allowed)
                if (IsBlockedRelativeUrl(url))
                    return new JsonResult(new { ok = false, message = "System routes cannot be used as navigation URLs." });

                pageId = null;
            }

            // parent must be within same menu
            int? parentId = req.ParentId;
            if (parentId.HasValue)
            {
                var parentExists = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId &&
                        x.Id == parentId.Value, ct);

                if (!parentExists)
                    return new JsonResult(new { ok = false, message = "Invalid parent selected." });
            }

            // Duplicate guard for internal links: same menu + same pageId (excluding self on edit)
            if (pageId.HasValue)
            {
                var duplicateExists = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId &&
                        x.PageId == pageId.Value &&
                        (!req.Id.HasValue || x.Id != req.Id.Value), ct);

                if (duplicateExists)
                    return new JsonResult(new { ok = false, message = "This page already exists in the selected menu." });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userId, out var userGuid);
            var now = DateTime.UtcNow;

            NavigationMenuItem entity;

            if (req.Id.HasValue && req.Id.Value > 0)
            {
                entity = await _db.NavigationMenuItems
                    .Where(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.Id == req.Id.Value)
                    .FirstOrDefaultAsync(ct);

                if (entity == null)
                    return new JsonResult(new { ok = false, message = "Menu item not found." });
            }
            else
            {
                // next sort order under same menu + parent
                var nextSort = await _db.NavigationMenuItems
                    .AsNoTracking()
                    .Where(x =>
                        x.TenantId == _tenant.TenantId &&
                        !x.IsDeleted &&
                        x.MenuId == req.MenuId &&
                        x.ParentId == parentId)
                    .Select(x => (int?)x.SortOrder)
                    .MaxAsync(ct) ?? 0;

                entity = new NavigationMenuItem
                {
                    TenantId = _tenant.TenantId,
                    MenuId = req.MenuId,
                    ParentId = parentId,
                    SortOrder = nextSort + 1,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    CreatedBy = userGuid == Guid.Empty ? Guid.Empty : userGuid
                };

                await _db.NavigationMenuItems.AddAsync(entity, ct);
            }

            // prevent cycle: cannot set parent to itself or descendants
            if (parentId.HasValue)
            {
                if (req.Id.HasValue && parentId.Value == req.Id.Value)
                    return new JsonResult(new { ok = false, message = "Parent cannot be the item itself." });

                var cursor = parentId;
                while (cursor.HasValue)
                {
                    if (req.Id.HasValue && cursor.Value == req.Id.Value)
                        return new JsonResult(new { ok = false, message = "Invalid parent (cycle detected)." });

                    cursor = await _db.NavigationMenuItems
                        .AsNoTracking()
                        .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted && x.Id == cursor.Value)
                        .Select(x => x.ParentId)
                        .FirstOrDefaultAsync(ct);
                }
            }

            entity.MenuId = req.MenuId;
            entity.ParentId = parentId;
            entity.Label = label;
            entity.PageId = pageId;
            entity.Url = url;
            entity.OpenInNewTab = req.OpenInNewTab;

            entity.UpdatedAt = now;
            entity.UpdatedBy = userGuid == Guid.Empty ? Guid.Empty : userGuid;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        public sealed class DeleteMenuItemRequest
        {
            public int Id { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteMenuItemAsync([FromBody] DeleteMenuItemRequest req, CancellationToken ct)
        {
            var entity = await _db.NavigationMenuItems
                .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted && x.Id == req.Id)
                .FirstOrDefaultAsync(ct);

            if (entity == null)
                return new JsonResult(new { ok = false, message = "Menu item not found." });

            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return new JsonResult(new { ok = true });
        }

        public sealed class ReorderMenuItemsRequest
        {
            public List<ReorderRow> Items { get; set; } = new();
            public sealed class ReorderRow
            {
                public int Id { get; set; }
                public int MenuId { get; set; }
                public int? ParentId { get; set; }
                public int SortOrder { get; set; }
            }
        }

        public async Task<IActionResult> OnPostReorderMenuItemsAsync([FromBody] ReorderMenuItemsRequest req, CancellationToken ct)
        {
            // Minimal + safe: update sort orders exactly as sent (you already validated drag behavior)
            var ids = req.Items.Select(x => x.Id).Distinct().ToList();

            var entities = await _db.NavigationMenuItems
                .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted && ids.Contains(x.Id))
                .ToListAsync(ct);

            var map = req.Items.ToDictionary(x => x.Id, x => x);

            foreach (var e in entities)
            {
                if (!map.TryGetValue(e.Id, out var row))
                    continue;

                e.MenuId = row.MenuId;
                e.ParentId = row.ParentId;
                e.SortOrder = row.SortOrder;
                e.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            return new JsonResult(new { ok = true });
        }
    }
}
