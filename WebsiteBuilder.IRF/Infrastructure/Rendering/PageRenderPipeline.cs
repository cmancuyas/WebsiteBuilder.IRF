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
                RedirectToUrl = null,
                MetaTitle = page.MetaTitle ?? page.Title,
                MetaDescription = page.MetaDescription,
                OgImageUrl = null, // keep null unless you want draft OG
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

        PageRevision? revision = null;

        if (!_cache.TryGetValue(cacheKey, out CachedPublishedRenderData? cached) || cached is null)
        {
            revision = await _db.PageRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == publishedRevisionId &&
                    r.TenantId == _tenant.TenantId &&
                    r.IsPublishedSnapshot &&
                    !r.IsDeleted &&
                    r.IsActive, ct);

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

        // Ensure we have the revision for SEO fields even on cache hit.
        // We keep this DB call lightweight (single row, no includes).
        revision ??= await _db.PageRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Id == publishedRevisionId &&
                r.TenantId == _tenant.TenantId &&
                r.IsPublishedSnapshot &&
                !r.IsDeleted &&
                r.IsActive, ct);

        if (revision is null) return null;

        // Canonical computed per-request (NOT cached)
        var (scheme, host) = await _url.GetCanonicalAsync(ct);
        var publishedSlug = NormalizeSlug(cached.RevisionSlug);
        var canonicalPath = string.IsNullOrWhiteSpace(publishedSlug) ? "/" : "/" + publishedSlug;
        var canonicalUrl = $"{scheme}://{host}{canonicalPath}";

        // Redirect if Page.Slug drifted from published snapshot slug
        string? redirectToUrl = null;
        var pageSlugNormalized = NormalizeSlug(page.Slug);
        if (!string.Equals(pageSlugNormalized, publishedSlug, StringComparison.OrdinalIgnoreCase))
        {
            redirectToUrl = canonicalUrl;
        }

        // ---- SEO fields FROM PUBLISHED SNAPSHOT (revision) ----
        // Revision fields are non-nullable in your model but may be empty strings.
        var metaTitle = !string.IsNullOrWhiteSpace(revision.MetaTitle)
            ? revision.MetaTitle
            : (!string.IsNullOrWhiteSpace(revision.Title) ? revision.Title : page.Title);

        var metaDesc = !string.IsNullOrWhiteSpace(revision.MetaDescription)
            ? revision.MetaDescription
            : page.MetaDescription;

        // OG image: simplest absolute URL strategy (adjust to your real media route)
        // If you already have a canonical media endpoint, update the path below.
        string? ogImageUrl = null;
        if (revision.OgImageAssetId.HasValue)
        {
            // Example route — change if your actual media endpoint differs:
            // ogImageUrl = $"{scheme}://{host}/media/{revision.OgImageAssetId.Value}";
            ogImageUrl = $"{scheme}://{host}/media/{revision.OgImageAssetId.Value}";
        }

        return new PageRenderContext
        {
            PageEntity = page,
            IsPreview = false,
            RobotsNoIndex = false,
            CanonicalUrl = canonicalUrl,
            RedirectToUrl = redirectToUrl,

            MetaTitle = metaTitle,
            MetaDescription = metaDesc,
            OgImageUrl = ogImageUrl,

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
