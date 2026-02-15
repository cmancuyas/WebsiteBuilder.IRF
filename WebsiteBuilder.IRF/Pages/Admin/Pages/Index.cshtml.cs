using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class IndexModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;

    public IndexModel(DataContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // ✅ Replace IsArchived boolean with PageStatusId + derived flag
    public sealed record Row(
        int Id,
        string Title,
        string Slug,
        int PageStatusId,
        int? DraftRevisionId,
        int? PublishedRevisionId)
    {
        public bool IsArchived => PageStatusId == PageStatusIds.Archived;
        public bool IsDraft => PageStatusId == PageStatusIds.Draft;
        public bool IsPublished => PageStatusId == PageStatusIds.Published;
    }

    public List<Row> Items { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return;

        var tenantId = _tenant.TenantId;

        Items = await _db.Pages
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .Select(p => new Row(
                p.Id,
                p.Title,
                p.Slug,
                p.PageStatusId,
                p.DraftRevisionId,
                p.PublishedRevisionId
            ))
            .ToListAsync(ct);
    }
}
