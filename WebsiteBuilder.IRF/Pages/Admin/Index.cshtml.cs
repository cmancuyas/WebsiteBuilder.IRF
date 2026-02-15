using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin;

public sealed class IndexModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly IOptions<MediaQuotaOptions> _quotaOptions;

    public IndexModel(
        DataContext db,
        ITenantContext tenant,
        IOptions<MediaQuotaOptions> quotaOptions)
    {
        _db = db;
        _tenant = tenant;
        _quotaOptions = quotaOptions;
    }

    public string? Banner { get; private set; }

    // Page stats
    public int PageTotal { get; private set; }
    public int PageDraft { get; private set; }
    public int PagePublished { get; private set; }
    public int PageArchived { get; private set; }

    // Media stats
    public int MediaImageCount { get; private set; }
    public int MediaDeletedCount { get; private set; }
    public long MediaTotalBytes { get; private set; }
    public long MediaQuotaBytes { get; private set; }

    // Recent lists
    public sealed class RecentPageVm
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public DateTime? PublishedAt { get; set; }
    }

    public sealed class RecentMediaVm
    {
        public int Id { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public List<RecentPageVm> RecentPublishedPages { get; private set; } = new();
    public List<RecentMediaVm> RecentMedia { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
        {
            ZeroAll();
            Banner = "Tenant not resolved. Dashboard metrics are unavailable.";
            return;
        }

        var tenantId = _tenant.TenantId;

        // ------------------------
        // Page counts (tenant-scoped)
        // ------------------------
        var pages = _db.Pages
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted);

        PageTotal = await pages.CountAsync(ct);
        PageDraft = await pages.CountAsync(p => p.PageStatusId == PageStatusIds.Draft, ct);
        PagePublished = await pages.CountAsync(p => p.PageStatusId == PageStatusIds.Published, ct);
        PageArchived = await pages.CountAsync(p => p.PageStatusId == PageStatusIds.Archived, ct);

        // ------------------------
        // Recent published pages
        // ------------------------
        RecentPublishedPages = await pages
            .Where(p => p.PageStatusId == PageStatusIds.Published && p.PublishedRevisionId != null)
            .OrderByDescending(p => p.PublishedAt ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .Take(6)
            .Select(p => new RecentPageVm
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                PublishedAt = p.PublishedAt
            })
            .ToListAsync(ct);

        // ------------------------
        // Media stats (tenant-scoped)
        // ------------------------
        var media = _db.MediaAssets
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IsActive);

        MediaImageCount = await media.CountAsync(m => m.ContentType.StartsWith("image/") && !m.IsDeleted, ct);
        MediaDeletedCount = await media.CountAsync(m => m.ContentType.StartsWith("image/") && m.IsDeleted, ct);

        MediaTotalBytes = await media
            .Where(m => m.ContentType.StartsWith("image/") && !m.IsDeleted)
            .Select(m => (long?)m.SizeBytes)
            .SumAsync(ct) ?? 0;

        // ------------------------
        // Quota (optional) - FIXED
        // ------------------------
        MediaQuotaBytes = 0;
        var quotaMap = _quotaOptions.Value?.TenantQuotaBytes;
        if (quotaMap is not null && quotaMap.TryGetValue(tenantId, out var tenantQuotaBytes))
        {
            MediaQuotaBytes = tenantQuotaBytes;
        }

        // ------------------------
        // Recent uploads
        // ------------------------
        RecentMedia = await media
            .Where(m => m.ContentType.StartsWith("image/") && !m.IsDeleted)
            .OrderByDescending(m => m.Id)
            .Take(6)
            .Select(m => new RecentMediaVm
            {
                Id = m.Id,
                FileName = m.FileName,
                ContentType = m.ContentType,
                SizeBytes = m.SizeBytes,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync(ct);

        if (TempData.TryGetValue("Success", out var ok)) Banner = ok?.ToString();
        if (TempData.TryGetValue("Error", out var err)) Banner = err?.ToString();
    }

    private void ZeroAll()
    {
        PageTotal = 0;
        PageDraft = 0;
        PagePublished = 0;
        PageArchived = 0;

        MediaImageCount = 0;
        MediaDeletedCount = 0;
        MediaTotalBytes = 0;
        MediaQuotaBytes = 0;

        RecentPublishedPages = new();
        RecentMedia = new();
    }
}
