using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    // Updated: supports children and new-tab. Order kept for compatibility/debug.
    public sealed record NavItem(
        string Title,
        string Url,
        int Order,
        bool OpenInNewTab = false,
        IReadOnlyList<NavItem> Children = null!
    );

    public sealed class TenantNavigationService : ITenantNavigationService
    {
        private const int HeaderMenuId = 1;

        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;

        private string CacheKey => $"tenant-nav:{_tenant.TenantId}:header:v2";

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

            // 1) Load Header menu items (NavigationMenuItems is the new source of truth)
            var items = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.MenuId == HeaderMenuId)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            if (items.Count == 0)
            {
                var empty = Array.Empty<NavItem>();
                _cache.Set(CacheKey, empty, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
                return empty;
            }

            // 2) Resolve PageId -> published slug (fallback to Url)
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

                // Safe fallback - don't emit empty href
                return "#";
            }

            string SafeTitle(string? label)
                => string.IsNullOrWhiteSpace(label) ? "Untitled" : label.Trim();

            // 3) Build tree: ParentId -> children
            const int RootKey = 0;

            var byParent = items
                .GroupBy(x => x.ParentId ?? RootKey)
                .ToDictionary(g => g.Key, g => g.ToList());


            NavItem MapItem(Models.NavigationMenuItem mi)
            {
                IReadOnlyList<NavItem> children = byParent.TryGetValue(mi.Id, out var kids)
                    ? kids.Select(MapItem).ToList()
                    : Array.Empty<NavItem>();


                return new NavItem(
                    Title: SafeTitle(mi.Label),
                    Url: ResolveUrl(mi.PageId, mi.Url),
                    Order: mi.SortOrder,
                    OpenInNewTab: mi.OpenInNewTab,
                    Children: children
                );
            }
            IReadOnlyList<NavItem> roots = byParent.TryGetValue(RootKey, out var rootItems)
                ? rootItems.Select(MapItem).ToList()
                : Array.Empty<NavItem>();

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

        private static string ToUrl(string? slugOrPath)
        {
            var s = (slugOrPath ?? string.Empty).Trim();

            // Allow already-absolute paths
            if (s.StartsWith("/"))
                return s;

            s = s.Trim('/');

            // Treat empty or "home" as root
            if (string.IsNullOrWhiteSpace(s) || s.Equals("home", StringComparison.OrdinalIgnoreCase))
                return "/";

            return "/" + s;
        }
    }
}
