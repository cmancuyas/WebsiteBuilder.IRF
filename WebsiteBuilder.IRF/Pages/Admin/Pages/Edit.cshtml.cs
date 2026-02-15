using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class EditModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;

    public EditModel(DataContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public string PageTitle { get; private set; } = "";
    public string PageSlug { get; private set; } = "";

    public string? Banner { get; private set; }

    public bool HasDraft { get; private set; }
    public bool CanPublish => HasDraft; // publish requires draft
    public bool CanRestoreDraft => PublishedInfo is not null;

    public int SectionCount { get; private set; }

    public RevisionInfo? PublishedInfo { get; private set; }

    public sealed record RevisionInfo(int Id, int VersionNumber, string Slug, DateTime? PublishedAt);

    public sealed class InputModel
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = "";

        [MaxLength(200)]
        public string? Slug { get; set; }

        [MaxLength(100)]
        public string? LayoutKey { get; set; }

        [MaxLength(200)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        public bool ShowInNavigation { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public sealed record SectionRowRenderVm(
    PageRevisionSection Section,
    string Title,
    string EditorPartialPath)
    {
        // ✅ This is the identifier you must use for delete/reorder/edit
        public int RevisionSectionId => Section.Id;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        return await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        if (!ModelState.IsValid)
            return await LoadAsync(ct);

        var page = await _db.Pages
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .Include(p => p.PublishedRevision)
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();

        if (page.DraftRevisionId is null || page.DraftRevision is null)
        {
            ModelState.AddModelError("", "No draft revision exists. Restore a draft from a published snapshot first.");
            return await LoadAsync(ct);
        }

        var newSlug = SlugUtil.Normalize(Input.Slug);

        var slugExists = await _db.Pages.AsNoTracking()
            .AnyAsync(p => p.TenantId == _tenant.TenantId && !p.IsDeleted && p.Id != page.Id && p.Slug == newSlug, ct);

        if (slugExists)
        {
            ModelState.AddModelError(nameof(Input.Slug), "Slug already exists for this tenant.");
            return await LoadAsync(ct);
        }

        // Update page canonical fields
        page.Title = Input.Title.Trim();
        page.Slug = newSlug;
        page.LayoutKey = Input.LayoutKey?.Trim();
        page.MetaTitle = Input.MetaTitle?.Trim();
        page.MetaDescription = Input.MetaDescription?.Trim();
        page.ShowInNavigation = Input.ShowInNavigation;
        page.PageStatusId = PageStatusIds.Draft;

        // Update draft revision snapshot fields too
        page.DraftRevision.Title = page.Title;
        page.DraftRevision.Slug = page.Slug;
        page.DraftRevision.LayoutKey = page.LayoutKey ?? "";
        page.DraftRevision.MetaTitle = page.MetaTitle ?? "";
        page.DraftRevision.MetaDescription = page.MetaDescription ?? "";
        page.DraftRevision.OgImageAssetId = page.OgImageAssetId;

        await _db.SaveChangesAsync(ct);

        Banner = "Draft saved.";
        return await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null || page.DraftRevision is null)
        {
            TempData["Error"] = "No draft revision exists to publish.";
            return RedirectToPage(new { id = Id });
        }

        // next version number = max + 1 (across all revisions)
        var maxVersion = await _db.PageRevisions
            .AsNoTracking()
            .Where(r => r.TenantId == _tenant.TenantId && r.PageId == page.Id && !r.IsDeleted)
            .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

        var now = DateTime.UtcNow;

        // create immutable published snapshot cloned from draft
        var published = new PageRevision
        {
            TenantId = _tenant.TenantId,
            PageId = page.Id,
            VersionNumber = maxVersion + 1,
            IsPublishedSnapshot = true,
            Title = page.DraftRevision.Title,
            Slug = page.DraftRevision.Slug,
            LayoutKey = page.DraftRevision.LayoutKey,
            MetaTitle = page.DraftRevision.MetaTitle,
            MetaDescription = page.DraftRevision.MetaDescription,
            OgImageAssetId = page.DraftRevision.OgImageAssetId,
            PublishedAt = now
        };

        // clone sections
        foreach (var s in page.DraftRevision.Sections.OrderBy(x => x.SortOrder))
        {
            published.Sections.Add(new PageRevisionSection
            {
                TenantId = _tenant.TenantId,
                SectionTypeId = s.SectionTypeId,
                SortOrder = s.SortOrder,
                SettingsJson = s.SettingsJson
            });
        }

        _db.PageRevisions.Add(published);
        await _db.SaveChangesAsync(ct);

        // point page to published snapshot
        page.PublishedRevisionId = published.Id;
        page.PublishedAt = now;
        page.PageStatusId = PageStatusIds.Published;

        // IMPORTANT for your architecture: published pages immutable; editing requires Draft
        page.DraftRevisionId = null;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Page published.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRestoreDraftAsync(int revisionId, CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages.FirstOrDefaultAsync(p =>
            p.Id == Id &&
            p.TenantId == _tenant.TenantId &&
            !p.IsDeleted, ct);

        if (page is null) return NotFound();

        // Validate published snapshot
        var source = await _db.PageRevisions
            .AsNoTracking()
            .Include(r => r.Sections)
            .FirstOrDefaultAsync(r =>
                r.Id == revisionId &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == page.Id &&
                r.IsPublishedSnapshot &&
                !r.IsDeleted, ct);

        if (source is null)
        {
            TempData["Error"] = "Invalid published revision selected.";
            return RedirectToPage(new { id = Id });
        }

        var maxVersion = await _db.PageRevisions
            .AsNoTracking()
            .Where(r => r.TenantId == _tenant.TenantId && r.PageId == page.Id && !r.IsDeleted)
            .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

        var draft = new PageRevision
        {
            TenantId = _tenant.TenantId,
            PageId = page.Id,
            VersionNumber = maxVersion + 1,
            IsPublishedSnapshot = false,
            Title = source.Title,
            Slug = source.Slug,
            LayoutKey = source.LayoutKey,
            MetaTitle = source.MetaTitle,
            MetaDescription = source.MetaDescription,
            OgImageAssetId = source.OgImageAssetId
        };

        foreach (var s in source.Sections.OrderBy(x => x.SortOrder))
        {
            draft.Sections.Add(new PageRevisionSection
            {
                TenantId = _tenant.TenantId,
                SectionTypeId = s.SectionTypeId,
                SortOrder = s.SortOrder,
                SettingsJson = s.SettingsJson
            });
        }

        _db.PageRevisions.Add(draft);
        await _db.SaveChangesAsync(ct);

        page.DraftRevisionId = draft.Id;
        page.PageStatusId = PageStatusIds.Draft;

        // Keep page canonical fields aligned with draft snapshot
        page.Title = draft.Title;
        page.Slug = draft.Slug;
        page.LayoutKey = draft.LayoutKey;
        page.MetaTitle = draft.MetaTitle;
        page.MetaDescription = draft.MetaDescription;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Draft restored from published snapshot.";
        return RedirectToPage(new { id = Id });
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .AsNoTracking()
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .Include(p => p.PublishedRevision)
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();

        PageTitle = page.Title;
        PageSlug = page.Slug;

        HasDraft = page.DraftRevisionId != null;

        SectionCount = page.DraftRevision?.Sections.Count ?? 0;

        if (page.PublishedRevisionId is not null && page.PublishedRevision is not null)
        {
            PublishedInfo = new RevisionInfo(
                page.PublishedRevision.Id,
                page.PublishedRevision.VersionNumber,
                page.PublishedRevision.Slug,
                page.PublishedRevision.PublishedAt
            );
        }

        // bind form from draft if available; else from page
        var src = page.DraftRevision ?? new PageRevision
        {
            Title = page.Title,
            Slug = page.Slug,
            LayoutKey = page.LayoutKey ?? "",
            MetaTitle = page.MetaTitle ?? "",
            MetaDescription = page.MetaDescription ?? ""
        };

        Input = new InputModel
        {
            Title = src.Title,
            Slug = src.Slug,
            LayoutKey = string.IsNullOrWhiteSpace(src.LayoutKey) ? null : src.LayoutKey,
            MetaTitle = string.IsNullOrWhiteSpace(src.MetaTitle) ? null : src.MetaTitle,
            MetaDescription = string.IsNullOrWhiteSpace(src.MetaDescription) ? null : src.MetaDescription,
            ShowInNavigation = page.ShowInNavigation
        };

        // show tempdata banner
        if (TempData.TryGetValue("Success", out var ok)) Banner = ok?.ToString();
        if (TempData.TryGetValue("Error", out var err)) Banner = err?.ToString();

        return Page();
    }
}
