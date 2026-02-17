using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.ViewComponents;

public sealed class PageSectionViewComponent : ViewComponent
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan MapTtl = TimeSpan.FromMinutes(10);

    public PageSectionViewComponent(DataContext db, ITenantContext tenant, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _cache = cache;
    }

    public async Task<IViewComponentResult> InvokeAsync(int sectionTypeId, string? settingsJson)
    {
        // Resolve key from cached map
        var key = await GetSectionKeyAsync(sectionTypeId);

        // Convert key → partial file
        var viewPath = ResolveViewPath(key);

        return View(viewPath, settingsJson);
    }

    private async Task<string?> GetSectionKeyAsync(int sectionTypeId)
    {
        if (!_tenant.IsResolved)
            return null;

        var cacheKey = $"sectiontypes:map:{_tenant.TenantId}";

        if (!_cache.TryGetValue(cacheKey, out Dictionary<int, string>? map) || map is null)
        {
            // If SectionTypes are global (not tenant-specific), you can drop TenantId filtering here
            var rows = await _db.SectionTypes.AsNoTracking()
                .Where(x => x.IsActive && !x.IsDeleted)
                .Select(x => new { x.Id, x.Key })
                .ToListAsync();

            map = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .ToDictionary(r => r.Id, r => r.Key!.Trim());

            _cache.Set(cacheKey, map, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = MapTtl
            });
        }

        return map.TryGetValue(sectionTypeId, out var key) ? key : null;
    }

    private static string ResolveViewPath(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "~/Pages/Shared/Sections/_Unknown.cshtml";

        var normalized = key.Trim();

        // Convert key like "hero" or "hero-banner" to PascalCase file name
        // "hero" -> "Hero"
        // "hero-banner" -> "HeroBanner"
        var parts = normalized
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        var pascal = string.Concat(parts.Select(p =>
            p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()
        ));

        if (string.IsNullOrWhiteSpace(pascal))
            return "~/Pages/Shared/Sections/_Unknown.cshtml";

        return $"~/Pages/Shared/Sections/_{pascal}.cshtml";
    }

}
