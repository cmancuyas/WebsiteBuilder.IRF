using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public sealed class PageRenderPipeline : IPageRenderPipeline
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITenantUrlResolver _url;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan PublishedCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DraftCacheTtl = TimeSpan.FromSeconds(5);

    public PageRenderPipeline(DataContext db, ITenantContext tenant, ITenantUrlResolver url, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _url = url;
        _cache = cache;
    }

    public async Task<PageRenderContext?> BuildForSlugAsync(string? slug, bool previewRequested, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return null;

        var normalizedSlug = NormalizeSlug(slug);

        // Resolve page by slug OR tenant HomePageId (for "/")
        var page = await ResolvePageAsync(normalizedSlug, ct);
        if (page is null) return null;

        // ==========================
        // PREVIEW (DRAFT) MODE
        // ==========================
        if (previewRequested)
        {
            if (page.DraftRevisionId is null) return null;

            var draftRevId = page.DraftRevisionId.Value;

            // Optional: tiny cache for draft (very short TTL)
            var draftCacheKey = $"render:draft:{_tenant.TenantId}:{draftRevId}";
            if (!_cache.TryGetValue(draftCacheKey, out IReadOnlyList<PageRenderContext.RenderSectionDto>? draftSections) || draftSections is null)
            {
                draftSections = await LoadSectionsAsync(draftRevId, ct);
                _cache.Set(draftCacheKey, draftSections, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DraftCacheTtl
                });
            }

            return new PageRenderContext
            {
                PageEntity = page,
                IsPreview = true,
                RobotsNoIndex = true,
                CanonicalUrl = null,
                RenderSections = draftSections
            };
        }

        // ==========================
        // PUBLIC (PUBLISHED) MODE
        // ==========================
        if (page.PublishedRevisionId is null) return null;

        return await BuildPublishedAsync(page, page.PublishedRevisionId.Value, ct);
    }

    public async Task<PageRenderContext?> BuildDraftForPageIdAsync(int pageId, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved) return null;

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Id == pageId &&
                p.TenantId == _tenant.TenantId &&
                p.IsActive &&
                !p.IsDeleted, ct);

        if (page is null) return null;
        if (page.DraftRevisionId is null) return null;

        var draftRevId = page.DraftRevisionId.Value;

        var draftCacheKey = $"render:draft:{_tenant.TenantId}:{draftRevId}";
        if (!_cache.TryGetValue(draftCacheKey, out IReadOnlyList<PageRenderContext.RenderSectionDto>? sections) || sections is null)
        {
            sections = await LoadSectionsAsync(draftRevId, ct);
            _cache.Set(draftCacheKey, sections, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DraftCacheTtl
            });
        }

        return new PageRenderContext
        {
            PageEntity = page,
            IsPreview = true,
            RobotsNoIndex = true,
            CanonicalUrl = null,
            RenderSections = sections
        };
    }

    // ==========================
    // Published builder (cached)
    // ==========================
    private async Task<PageRenderContext?> BuildPublishedAsync(Page page, int publishedRevisionId, CancellationToken ct)
    {
        var cacheKey = $"render:published:{_tenant.TenantId}:{publishedRevisionId}";

        if (!_cache.TryGetValue(cacheKey, out CachedPublishedRenderData? cached) || cached is null)
        {
            var revision = await _db.PageRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == publishedRevisionId &&
                    r.TenantId == _tenant.TenantId &&
                    r.IsPublishedSnapshot &&
                    !r.IsDeleted, ct);

            if (revision is null) return null;

            var sections = await LoadSectionsAsync(publishedRevisionId, ct);

            cached = new CachedPublishedRenderData
            {
                PageId = page.Id,
                PublishedRevisionId = publishedRevisionId,
                RevisionTitle = revision.Title,
                RevisionSlug = revision.Slug,
                Sections = sections
            };

            _cache.Set(cacheKey, cached, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = PublishedCacheTtl
            });
        }

        // Canonical computed per-request (NOT cached)
        var (scheme, host) = await _url.GetCanonicalAsync(ct);
        var publishedSlug = NormalizeSlug(cached.RevisionSlug);
        var canonicalPath = string.IsNullOrWhiteSpace(publishedSlug) ? "/" : "/" + publishedSlug;

        return new PageRenderContext
        {
            PageEntity = page,
            IsPreview = false,
            RobotsNoIndex = false,
            CanonicalUrl = $"{scheme}://{host}{canonicalPath}",
            RenderSections = cached.Sections
        };
    }

    // ==========================
    // Resolve page by slug or tenant home
    // ==========================
    private async Task<Page?> ResolvePageAsync(string normalizedSlug, CancellationToken ct)
    {
        // "/" requested → use Tenant.HomePageId
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct);

            if (tenant?.HomePageId is null) return null;

            return await _db.Pages.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == tenant.HomePageId &&
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted, ct);
        }

        // slug requested
        return await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.TenantId == _tenant.TenantId &&
                p.IsActive &&
                !p.IsDeleted &&
                p.Slug == normalizedSlug, ct);
    }

    private async Task<IReadOnlyList<PageRenderContext.RenderSectionDto>> LoadSectionsAsync(int pageRevisionId, CancellationToken ct)
    {
        var sections = await _db.PageRevisionSections.AsNoTracking()
            .Include(s => s.SectionType)
            .Where(s =>
                s.TenantId == _tenant.TenantId &&
                s.PageRevisionId == pageRevisionId &&
                s.IsActive &&
                !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        return sections.Select(s => new PageRenderContext.RenderSectionDto
        {
            SectionTypeId = s.SectionTypeId,
            SectionTypeName = s.SectionType?.Name,
            SortOrder = s.SortOrder,
            SettingsJson = s.SettingsJson,
            SectionTypeKey = s.SectionType?.Key,
        }).ToList();
    }

    private static string NormalizeSlug(string? slug)
    {
        slug ??= string.Empty;

        var s = slug.Trim().Trim('/').ToLowerInvariant();
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\-]+", string.Empty);
        s = Regex.Replace(s, @"-+", "-");

        return s.Trim('-');
    }
}
