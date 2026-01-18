using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    // Supports hierarchical menus (dropdown) but still "Option 1": only MenuId = 1 (Header).
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

        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;

        private string CacheKey => $"tenant-nav:{_tenant.TenantId}:menu:{HeaderMenuId}";

        public TenantNavigationService(DataContext db, ITenantContext tenant, IMemoryCache cache)
        {
            _db = db;
            _tenant = tenant;
            _cache = cache;
        }

        public async Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return Array.Empty<NavItem>();

            if (_cache.TryGetValue(CacheKey, out IReadOnlyList<NavItem>? cached) && cached is not null)
                return cached;

            // 1) Load menu items for header only (MenuId=1)
            var items = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted &&
                    x.MenuId == HeaderMenuId)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.ParentId,
                    x.SortOrder,
                    x.Label,
                    x.PageId,
                    x.Url,
                    x.OpenInNewTab
                })
                .ToListAsync(ct);

            if (items.Count == 0)
                return Array.Empty<NavItem>();

            // 2) Build PageId -> Published slug map (only for PageIds referenced by menu items)
            var pageIds = items
                .Where(x => x.PageId.HasValue)
                .Select(x => x.PageId!.Value)
                .Distinct()
                .ToList();

            var publishedSlugs = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published &&
                    pageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Slug })
                .ToDictionaryAsync(x => x.Id, x => x.Slug, ct);

            // 3) ParentId -> children lookup (ParentId is nullable int)
            const int RootKey = 0;

            var byParent = items
                .GroupBy(x => x.ParentId ?? RootKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            string ResolveUrl(int? pageId, string? url)
            {
                if (pageId.HasValue &&
                    publishedSlugs.TryGetValue(pageId.Value, out var slug) &&
                    !string.IsNullOrWhiteSpace(slug))
                {
                    return ToUrl(slug);
                }

                if (!string.IsNullOrWhiteSpace(url))
                    return url.Trim();

                // safe fallback (don’t emit empty href)
                return "#";
            }

            static string SafeTitle(string? label)
                => string.IsNullOrWhiteSpace(label) ? "Untitled" : label.Trim();

            // 4) Build tree (cycle-safe)
            NavItem MapItem(int id, HashSet<int> visiting, int depth)
            {
                // depth guard (prevents runaway in case of bad data)
                if (depth > 8)
                    return new NavItem("…", "#", 0, false, Array.Empty<NavItem>());

                if (!visiting.Add(id))
                {
                    // cycle detected; drop children
                    var cyc = items.First(x => x.Id == id);
                    return new NavItem(
                        SafeTitle(cyc.Label),
                        ResolveUrl(cyc.PageId, cyc.Url),
                        cyc.SortOrder,
                        cyc.OpenInNewTab,
                        Array.Empty<NavItem>()
                    );
                }

                var current = items.First(x => x.Id == id);

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


            _cache.Set(CacheKey, roots, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return roots;
        }

        public void Invalidate()
        {
            if (_tenant.IsResolved)
                _cache.Remove(CacheKey);
        }

        private static string ToUrl(string? slug)
        {
            var s = (slug ?? string.Empty).Trim().Trim('/');

            // Treat empty or "home" as root
            if (string.IsNullOrWhiteSpace(s) || s.Equals("home", StringComparison.OrdinalIgnoreCase))
                return "/";

            return "/" + s;
        }
    }
}
