using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    // Supports hierarchical menus (dropdown).
    public sealed record NavItem(
        string Title,
        string Url,
        int Order,
        bool OpenInNewTab,
        IReadOnlyList<NavItem> Children
    );

    public sealed class TenantNavigationService : ITenantNavigationService
    {
        private const int HeaderMenuId = 1;
        private const int FooterMenuId = 2;

        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _http;

        public TenantNavigationService(DataContext db, ITenantContext tenant, IMemoryCache cache, IHttpContextAccessor http)
        {
            _db = db;
            _tenant = tenant;
            _cache = cache;
            _http = http;
        }

        // Backward compatible: existing callers get Header
        public Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default)
            => GetHeaderAsync(ct);

        public Task<IReadOnlyList<NavItem>> GetHeaderAsync(CancellationToken ct = default)
            => GetMenuAsync(HeaderMenuId, ct);

        public Task<IReadOnlyList<NavItem>> GetFooterAsync(CancellationToken ct = default)
            => GetMenuAsync(FooterMenuId, ct);

        public Task<IReadOnlyList<NavItem>> GetMenuAsync(int menuId, CancellationToken ct = default)
            => GetMenuInternalAsync(menuId, ct);

        private string CacheKey(int menuId) => $"tenant-nav:{_tenant.TenantId}:menu:{menuId}";

        // Cache raw rows (user-agnostic). Then filter per request.
        private async Task<List<NavRow>> GetRawRowsAsync(int menuId, CancellationToken ct)
        {
            var cacheKey = CacheKey(menuId) + ":raw";

            if (_cache.TryGetValue(cacheKey, out List<NavRow>? cached) && cached is not null)
                return cached;

            var rows = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted &&
                    x.IsActive &&
                    x.IsPublished &&
                    x.MenuId == menuId)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new NavRow
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    SortOrder = x.SortOrder,
                    Label = x.Label,
                    PageId = x.PageId,
                    Url = x.Url,
                    OpenInNewTab = x.OpenInNewTab,
                    AllowedRolesCsv = x.AllowedRolesCsv
                })
                .ToListAsync(ct);

            _cache.Set(cacheKey, rows, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return rows;
        }

        private async Task<IReadOnlyList<NavItem>> GetMenuInternalAsync(int menuId, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return Array.Empty<NavItem>();

            // 1) Load raw menu rows (cached, user-agnostic)
            var rawItems = await GetRawRowsAsync(menuId, ct);
            if (rawItems.Count == 0)
                return Array.Empty<NavItem>();

            // 2) Role-based visibility filter (per request)
            var user = _http.HttpContext?.User;

            var items = rawItems
                .Where(x => IsVisibleToUser(x.AllowedRolesCsv, user))
                .ToList();

            if (items.Count == 0)
                return Array.Empty<NavItem>();

            // 3) Build PageId -> Published slug map (only for visible PageIds referenced by menu items)
            var pageIds = items
                .Where(x => x.PageId.HasValue)
                .Select(x => x.PageId!.Value)
                .Distinct()
                .ToList();

            var publishedSlugs = pageIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Pages
                    .AsNoTracking()
                    .Where(p =>
                        p.TenantId == _tenant.TenantId &&
                        p.IsActive &&
                        !p.IsDeleted &&
                        p.PageStatusId == PageStatusIds.Published &&
                        pageIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Slug })
                    .ToDictionaryAsync(x => x.Id, x => x.Slug, ct);

            // 4) ParentId -> children lookup (ParentId is nullable int)
            const int RootKey = 0;

            var byParent = items
                .GroupBy(x => x.ParentId ?? RootKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            string ResolveUrl(int? pageId, string? url)
            {
                // Prefer published PageId -> slug
                if (pageId.HasValue &&
                    publishedSlugs.TryGetValue(pageId.Value, out var slug) &&
                    !string.IsNullOrWhiteSpace(slug))
                {
                    return ToUrl(slug);
                }

                // Fallback to stored URL
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var trimmed = url.Trim();

                    // Normalize common home variant so Home doesn't end up as /home
                    if (trimmed.Equals("/home", StringComparison.OrdinalIgnoreCase))
                        return "/";

                    return trimmed;
                }

                // safe fallback (don’t emit empty href)
                return "#";
            }

            static string SafeTitle(string? label)
                => string.IsNullOrWhiteSpace(label) ? "Untitled" : label.Trim();

            // Build a dictionary for O(1) lookups (avoid items.First(...) repeatedly)
            var byId = items.ToDictionary(x => x.Id);

            // 5) Build tree (cycle-safe)
            NavItem MapItem(int id, HashSet<int> visiting, int depth)
            {
                // depth guard (prevents runaway in case of bad data)
                if (depth > 8)
                    return new NavItem("…", "#", 0, false, Array.Empty<NavItem>());

                if (!visiting.Add(id))
                {
                    // cycle detected; drop children
                    var cyc = byId[id];
                    return new NavItem(
                        SafeTitle(cyc.Label),
                        ResolveUrl(cyc.PageId, cyc.Url),
                        cyc.SortOrder,
                        cyc.OpenInNewTab,
                        Array.Empty<NavItem>()
                    );
                }

                var current = byId[id];

                var kids = byParent.TryGetValue(id, out var childRows)
                    ? childRows
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .Select(x => MapItem(x.Id, visiting, depth + 1))
                        .ToList()
                        .AsReadOnly()
                    : (IReadOnlyList<NavItem>)Array.Empty<NavItem>();

                visiting.Remove(id);

                return new NavItem(
                    SafeTitle(current.Label),
                    ResolveUrl(current.PageId, current.Url),
                    current.SortOrder,
                    current.OpenInNewTab,
                    kids
                );
            }

            // roots: ParentId == null
            var roots = byParent.TryGetValue(RootKey, out var rootRows)
                ? rootRows
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => MapItem(x.Id, new HashSet<int>(), 0))
                    .ToList()
                    .AsReadOnly()
                : (IReadOnlyList<NavItem>)Array.Empty<NavItem>();

            return roots;
        }

        public void Invalidate()
        {
            if (!_tenant.IsResolved)
                return;

            _cache.Remove(CacheKey(HeaderMenuId) + ":raw");
            _cache.Remove(CacheKey(FooterMenuId) + ":raw");
        }

        public void InvalidateMenu(int menuId)
        {
            if (!_tenant.IsResolved)
                return;

            _cache.Remove(CacheKey(menuId) + ":raw");
        }

        private static string ToUrl(string? slug)
        {
            var s = (slug ?? string.Empty).Trim().Trim('/');

            // Treat empty or "home" as root
            if (string.IsNullOrWhiteSpace(s) || s.Equals("home", StringComparison.OrdinalIgnoreCase))
                return "/";

            return "/" + s;
        }

        private static bool IsVisibleToUser(string? allowedRolesCsv, ClaimsPrincipal? user)
        {
            if (string.IsNullOrWhiteSpace(allowedRolesCsv))
                return true;

            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                return false;

            var roles = allowedRolesCsv
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => r.Length > 0);

            return roles.Any(user.IsInRole);
        }

        private sealed class NavRow
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public int SortOrder { get; set; }
            public string? Label { get; set; }
            public int? PageId { get; set; }
            public string? Url { get; set; }
            public bool OpenInNewTab { get; set; }
            public string? AllowedRolesCsv { get; set; }
        }
    }
}
