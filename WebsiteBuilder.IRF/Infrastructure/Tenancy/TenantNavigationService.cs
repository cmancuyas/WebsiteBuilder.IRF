using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed record NavItem(string Title, string Url, int Order);

    public sealed class TenantNavigationService : ITenantNavigationService
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;

        private string CacheKey => $"tenant-nav:{_tenant.TenantId}";

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

            // Only show published pages that are intended to appear in navigation
            var pages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published &&
                    p.ShowInNavigation)
                .OrderBy(p => p.NavigationOrder)
                .ThenBy(p => p.Title)
                .Select(p => new
                {
                    Title = p.Title,
                    Slug = p.Slug,
                    Order = p.NavigationOrder
                })
                .ToListAsync(ct);

            var nav = pages
                .Select(p =>
                {
                    var title = string.IsNullOrWhiteSpace(p.Title) ? "Untitled" : p.Title.Trim();
                    var url = ToUrl(p.Slug);

                    return new NavItem(title, url, p.Order);
                })
                .ToList()
                .AsReadOnly();

            _cache.Set(CacheKey, nav, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return nav;
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
