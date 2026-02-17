using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Razor;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Sections.Settings;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class EditModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISectionRegistry _sections;
    private readonly IPageRevisionSectionService _pageRevisionSectionService;
    private readonly IRazorPartialRenderer _partialRenderer;
    private readonly PagePublishValidator _pagePublishValidator;

    public EditModel(
        DataContext db,
        ITenantContext tenant,
        ISectionRegistry sections,
        IPageRevisionSectionService pageRevisionSectionService,
        IRazorPartialRenderer partialRenderer,
        PagePublishValidator pagePublishValidator)
    {
        _db = db;
        _tenant = tenant;
        _sections = sections;
        _pageRevisionSectionService = pageRevisionSectionService;
        _partialRenderer = partialRenderer;
        _pagePublishValidator = pagePublishValidator;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public string PageTitle { get; private set; } = "";
    public string PageSlug { get; private set; } = "";
    public string? Banner { get; private set; }

    public bool HasDraft { get; private set; }
    public bool CanPublish => HasDraft;
    public bool CanRestoreDraft => PublishedInfo is not null;

    public int SectionCount { get; private set; }

    public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

    public RevisionInfo? PublishedInfo { get; private set; }

    public sealed record RevisionInfo(int Id, int VersionNumber, string Slug, DateTime? PublishedAt);

    public List<SectionRowRenderVm> SectionRows { get; private set; } = new();

    // ✅ base64 rowversion for current draft revision
    public int? DraftRevisionId { get; private set; }
    public string DraftRevisionRowVersionBase64 { get; private set; } = "";

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

    public sealed class AddSectionRequest
    {
        public int SectionTypeId { get; init; }
        public int? InsertAfterRevisionSectionId { get; init; }
        public bool InsertAtTop { get; init; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<PagePublishValidator.PageSectionPublishError> PublishValidationErrors { get; private set; } = new();
    public HashSet<int> PublishValidationSectionIds { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    // =========================
    // ✅ AJAX: Validate Draft UX
    // =========================
    public record ValidateDraftResult(bool isValid, int[] invalidSectionIds, string[] errors);

    public async Task<IActionResult> OnGetValidateDraftAsync(int id, CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        // ✅ Same tenant-safe lookup pattern used by Save/Publish
        var page = await _db.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Id == id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted, ct);

        if (page is null)
            return NotFound();

        // No draft => treat as valid
        if (page.DraftRevisionId is null)
            return new JsonResult(new ValidateDraftResult(
                isValid: true,
                invalidSectionIds: Array.Empty<int>(),
                errors: Array.Empty<string>()));

        // ✅ Re-use your existing validator (matches your provided implementation)
        var validation = await _pagePublishValidator.ValidateDraftSectionsAsync(page.Id, ct);

        // Errors: List<PageSectionPublishError> where SectionId is int and Messages is List<string>
        var invalidIds = (validation.Errors ?? new List<PagePublishValidator.PageSectionPublishError>())
            .Where(e => e.SectionId > 0)
            .Select(e => e.SectionId)
            .Distinct()
            .ToArray();

        var errors = (validation.Errors ?? new List<PagePublishValidator.PageSectionPublishError>())
            .SelectMany(e => e.Messages ?? new List<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToArray();

        return new JsonResult(new ValidateDraftResult(
            isValid: validation.IsValid,
            invalidSectionIds: invalidIds,
            errors: errors));
    }



    // =========================
    // Save Draft
    // =========================
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

        page.Title = Input.Title.Trim();
        page.Slug = newSlug;
        page.LayoutKey = Input.LayoutKey?.Trim();
        page.MetaTitle = Input.MetaTitle?.Trim();
        page.MetaDescription = Input.MetaDescription?.Trim();
        page.ShowInNavigation = Input.ShowInNavigation;
        page.PageStatusId = PageStatusIds.Draft;

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

    // =========================
    // Publish
    // =========================
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

        var publishValidation = await _pagePublishValidator.ValidateDraftSectionsAsync(page.Id, ct);
        if (!publishValidation.IsValid)
        {
            TempData["Error"] = "Cannot publish. One or more sections have invalid settings.";
            TempData["PublishValidationErrors"] = JsonSerializer.Serialize(publishValidation.Errors);
            return RedirectToPage(new { id = Id });
        }

        var now = DateTime.UtcNow;
        var strategy = _db.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                var maxVersion = await _db.PageRevisions
                    .AsNoTracking()
                    .Where(r => r.TenantId == _tenant.TenantId && r.PageId == page.Id && !r.IsDeleted)
                    .MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0;

                var published = new PageRevision
                {
                    TenantId = _tenant.TenantId,
                    PageId = page.Id,
                    VersionNumber = maxVersion + 1,
                    IsPublishedSnapshot = true,
                    Title = page.DraftRevision!.Title,
                    Slug = page.DraftRevision!.Slug,
                    LayoutKey = page.DraftRevision!.LayoutKey,
                    MetaTitle = page.DraftRevision!.MetaTitle,
                    MetaDescription = page.DraftRevision!.MetaDescription,
                    OgImageAssetId = page.DraftRevision!.OgImageAssetId,
                    PublishedAt = now
                };

                foreach (var s in page.DraftRevision!.Sections
                             .Where(x => !x.IsDeleted && x.IsActive)
                             .OrderBy(x => x.SortOrder)
                             .ThenBy(x => x.Id))
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

                page.PublishedRevisionId = published.Id;
                page.PublishedAt = now;
                page.PageStatusId = PageStatusIds.Published;

                page.DraftRevisionId = null;
                page.DraftRevision = null;

                await _db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex)
        {
            TempData["Error"] = "Publish failed. " + ex.GetBaseException().Message;
            return RedirectToPage(new { id = Id });
        }

        TempData["Success"] = "Page published.";
        return RedirectToPage(new { id = Id });
    }

    // =========================
    // Restore Draft
    // =========================
    public async Task<IActionResult> OnPostRestoreDraftAsync(int revisionId, CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages.FirstOrDefaultAsync(p =>
            p.Id == Id &&
            p.TenantId == _tenant.TenantId &&
            !p.IsDeleted, ct);

        if (page is null) return NotFound();

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

        page.Title = draft.Title;
        page.Slug = draft.Slug;
        page.LayoutKey = draft.LayoutKey;
        page.MetaTitle = draft.MetaTitle;
        page.MetaDescription = draft.MetaDescription;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Draft restored from published snapshot.";
        return RedirectToPage(new { id = Id });
    }

    // ✅ AJAX: Add section (kept as-is)
    public async Task<IActionResult> OnPostAddRevisionSectionAsync([FromBody] AddSectionRequest req, CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return new JsonResult(new { ok = false, error = "Tenant not resolved." }) { StatusCode = 404 };

        if (req is null || req.SectionTypeId <= 0)
            return BadRequest(new { ok = false, error = "Invalid request." });

        var page = await _db.Pages
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .FirstOrDefaultAsync(p =>
                p.Id == Id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted, ct);

        if (page is null)
            return NotFound(new { ok = false, error = "Page not found." });

        if (page.DraftRevisionId is null || page.DraftRevision is null)
            return BadRequest(new { ok = false, error = "No draft exists. Restore a draft first." });

        var st = await _db.Set<SectionType>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == req.SectionTypeId, ct);

        if (st is null)
            return BadRequest(new { ok = false, error = "Invalid SectionTypeId." });

        var draftRevisionId = page.DraftRevisionId.Value;

        var sections = page.DraftRevision.Sections
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .ToList();

        int insertIndex;
        if (req.InsertAtTop)
        {
            insertIndex = 0;
        }
        else if (req.InsertAfterRevisionSectionId.HasValue)
        {
            var idx = sections.FindIndex(x => x.Id == req.InsertAfterRevisionSectionId.Value);
            if (idx < 0)
                return BadRequest(new { ok = false, error = "InsertAfter section not found." });

            insertIndex = idx + 1;
        }
        else
        {
            insertIndex = sections.Count;
        }

        var newSection = new PageRevisionSection
        {
            TenantId = _tenant.TenantId,
            PageRevisionId = draftRevisionId,
            SectionTypeId = st.Id,
            SortOrder = insertIndex,
            SettingsJson = GetDefaultJsonByKey(st.Key)
        };

        _db.PageRevisionSections.Add(newSection);
        await _db.SaveChangesAsync(ct);

        await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

        var created = await _db.PageRevisionSections
            .AsNoTracking()
            .FirstAsync(s =>
                s.Id == newSection.Id &&
                s.TenantId == _tenant.TenantId &&
                s.PageRevisionId == draftRevisionId &&
                !s.IsDeleted, ct);

        var createdKey = (await _db.Set<SectionType>()
                .AsNoTracking()
                .Where(x => x.Id == created.SectionTypeId)
                .Select(x => x.Key)
                .FirstOrDefaultAsync(ct))?.Trim();

        var title = !string.IsNullOrWhiteSpace(createdKey) ? createdKey : "Section";
        var editorPartialPath = "Shared/Sections/_Text";

        if (!string.IsNullOrWhiteSpace(createdKey) && _sections.TryGet(createdKey, out var def))
        {
            title = def.DisplayName;
            editorPartialPath = def.PartialViewPath;
        }

        var rowVm = new SectionRowRenderVm
        {
            RevisionSectionId = created.Id,
            SectionTypeId = created.SectionTypeId,
            Title = title,
            CollapseId = $"sec-editor-{created.Id}",
            EditorPartialPath = editorPartialPath,
            IsEditable = false,
            Section = created
        };

        var html = await _partialRenderer.RenderPartialAsync("Partials/_PageRevisionSectionRow", rowVm, HttpContext);

        return new JsonResult(new
        {
            ok = true,
            revisionSectionId = created.Id,
            title = rowVm.Title,
            html
        });
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .AsNoTracking()
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .Include(p => p.PublishedRevision)
            .FirstOrDefaultAsync(p =>
                p.Id == Id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted, ct);

        if (page is null)
            return NotFound();

        PageTitle = page.Title;
        PageSlug = page.Slug;

        HasDraft = page.DraftRevisionId != null;

        DraftRevisionId = page.DraftRevisionId;
        DraftRevisionRowVersionBase64 =
            (page.DraftRevision?.RowVersion is { Length: > 0 } rv)
                ? Convert.ToBase64String(rv)
                : "";

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

        SectionTypeOptions = await _db.Set<SectionType>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync(ct);

        SectionRows = new List<SectionRowRenderVm>();

        var draftSections = page.DraftRevision?.Sections?
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToList();

        if (draftSections is not null && draftSections.Count > 0)
        {
            var typeIds = draftSections.Select(s => s.SectionTypeId).Distinct().ToList();

            var keyById = await _db.Set<SectionType>()
                .AsNoTracking()
                .Where(st => typeIds.Contains(st.Id))
                .Select(st => new { st.Id, st.Key })
                .ToDictionaryAsync(x => x.Id, x => x.Key, ct);

            foreach (var s in draftSections)
            {
                keyById.TryGetValue(s.SectionTypeId, out var typeKey);
                typeKey = typeKey?.Trim();

                var title = !string.IsNullOrWhiteSpace(typeKey)
                    ? typeKey
                    : $"Section {s.SortOrder + 1}";

                var editorPartialPath = "Shared/Sections/_Text";

                if (!string.IsNullOrWhiteSpace(typeKey) && _sections.TryGet(typeKey, out var def))
                {
                    title = def.DisplayName;
                    editorPartialPath = def.PartialViewPath;
                }

                SectionRows.Add(new SectionRowRenderVm
                {
                    RevisionSectionId = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    Title = title,
                    CollapseId = $"sec-editor-{s.Id}",
                    EditorPartialPath = editorPartialPath,
                    IsEditable = false,
                    Section = s
                });
            }
        }

        if (TempData.TryGetValue("Success", out var ok))
            Banner = ok?.ToString();

        if (TempData.TryGetValue("Error", out var err))
            Banner = err?.ToString();

        PublishValidationErrors = new();
        PublishValidationSectionIds = new();

        if (TempData.TryGetValue("PublishValidationErrors", out var raw) &&
            raw is string json &&
            !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var errs =
                    JsonSerializer.Deserialize<List<PagePublishValidator.PageSectionPublishError>>(json)
                    ?? new List<PagePublishValidator.PageSectionPublishError>();

                PublishValidationErrors = errs;

                PublishValidationSectionIds = errs
                    .Where(e => e.SectionId > 0)
                    .Select(e => e.SectionId)
                    .ToHashSet();
            }
            catch
            {
                PublishValidationErrors = new();
                PublishValidationSectionIds = new();
            }
        }

        return Page();
    }

    private static string GetDefaultJsonByKey(string key)
    {
        if (string.Equals(key, "Hero", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new HeroSettings
            {
                Heading = "Welcome",
                Subheading = "Your tagline here"
            });

        if (string.Equals(key, "Text", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new TextSettings
            {
                Title = "Title",
                Body = "Your content here."
            });

        if (string.Equals(key, "Gallery", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new GallerySettings
            {
                Layout = "grid",
                ImageAssetIds = new List<int>()
            });

        return "{}";
    }
}
