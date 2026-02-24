using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class RevisionsModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;

    public RevisionsModel(DataContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public string PageTitle { get; private set; } = "";
    public string PageSlug { get; private set; } = "";
    public string? Banner { get; private set; }

    public int? CurrentPublishedRevisionId { get; private set; }

    public sealed record Row(int Id, int VersionNumber, bool IsPublishedSnapshot, string Slug, DateTime? PublishedAt);
    public List<Row> Items { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        return await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostRollbackAsync(int revisionId, CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages.FirstOrDefaultAsync(p =>
            p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();

        var target = await _db.PageRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.Id == revisionId &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == page.Id &&
                r.IsPublishedSnapshot &&
                !r.IsDeleted, ct);

        if (target is null)
        {
            TempData["Error"] = "Invalid published revision selected.";
            return RedirectToPage(new { id = Id });
        }

        // swap published pointer
        page.PublishedRevisionId = target.Id;
        page.PublishedAt = target.PublishedAt ?? DateTime.UtcNow;
        page.PageStatusId = PageStatusIds.Published;

        // keep slug/title aligned to published canonical
        page.Title = target.Title;
        page.Slug = target.Slug;
        page.LayoutKey = target.LayoutKey;
        page.MetaTitle = target.MetaTitle;
        page.MetaDescription = target.MetaDescription;
        page.OgImageAssetId = target.OgImageAssetId;

        // ensure no draft exists after rollback (your rule: editing requires draft)
        page.DraftRevisionId = null;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Rollback completed.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRestoreDraftAsync(int revisionId, CancellationToken ct)
    {
        // delegate to Edit handler behavior by redirecting to Edit RestoreDraft
        return RedirectToPage("/Admin/Pages/Edit", new { id = Id, handler = "RestoreDraft", revisionId });
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();

        PageTitle = page.Title;
        PageSlug = page.Slug;
        CurrentPublishedRevisionId = page.PublishedRevisionId;

        Items = await _db.PageRevisions
            .AsNoTracking()
            .Where(r => r.TenantId == _tenant.TenantId && r.PageId == page.Id && !r.IsDeleted)
            .OrderByDescending(r => r.VersionNumber)
            .Select(r => new Row(r.Id, r.VersionNumber, r.IsPublishedSnapshot, r.Slug, r.PublishedAt))
            .ToListAsync(ct);

        if (TempData.TryGetValue("Success", out var ok)) Banner = ok?.ToString();
        if (TempData.TryGetValue("Error", out var err)) Banner = err?.ToString();

        return Page();
    }
}
