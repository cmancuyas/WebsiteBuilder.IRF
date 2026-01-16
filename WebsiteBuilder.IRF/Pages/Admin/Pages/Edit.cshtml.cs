using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IPagePublishingService _pagePublishingService;
        private readonly ITenantNavigationService _nav;
        private readonly PagePublishValidator _pagePublishValidator;

        public EditModel(
            DataContext db,
            ITenantContext tenant,
            IPagePublishingService pagePublishingService,
            ITenantNavigationService nav,
            PagePublishValidator pagePublishValidator)
        {
            _db = db;
            _tenant = tenant;
            _pagePublishingService = pagePublishingService;
            _nav = nav;
            _pagePublishValidator = pagePublishValidator;
        }

        [BindProperty]
        public PageEditVm Input { get; set; } = new();

        public SelectList PageStatusSelect { get; private set; } = default!;
        public bool SaveSuccess { get; private set; }
        public string TenantHost => _tenant.Host ?? string.Empty;

        // Sections (for drag-drop reorder UI)
        public IReadOnlyList<SectionRowVm> Sections { get; set; } = Array.Empty<SectionRowVm>();

        // ===== Publish/Archive UI state =====
        public int SectionCount { get; private set; }
        public bool HasSections => SectionCount > 0;
        public bool HasSlug => !string.IsNullOrWhiteSpace(Input?.Slug);

        public bool IsPublished => Input.PageStatusId == PageStatusIds.Published;
        public bool IsDraft => Input.PageStatusId == PageStatusIds.Draft;
        public bool IsArchived => Input.PageStatusId == PageStatusIds.Archived;

        // CanPublish: enabled only when not Published/Archived, has slug, has sections
        public bool CanPublish => !IsPublished && !IsArchived && HasSlug && HasSections;

        // CanUnpublish: allowed only when Published
        public bool CanUnpublish => IsPublished;

        // CanArchive: allowed when not archived (Draft or Published)
        public bool CanArchive => !IsArchived;

        // CanRestore: allowed only when archived
        public bool CanRestore => IsArchived;
        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();
        public sealed record SectionTypeLookupVm(int Id, string Name, string Key);

        public IReadOnlyList<SectionTypeLookupVm> SectionTypeLookup { get; private set; }
            = Array.Empty<SectionTypeLookupVm>();

        // Simple label for the reorder list
        public string GetSectionTitle(SectionRowVm s)
        {
            var st = SectionTypeLookup.FirstOrDefault(x => x.Id == s.SectionTypeId);
            return st?.Name ?? $"SectionType #{s.SectionTypeId}";
        }

        public string GetEditorPartialPath(SectionRowVm s)
        {
            var st = SectionTypeLookup.FirstOrDefault(x => x.Id == s.SectionTypeId);
            var key = (st?.Key ?? "").Trim().ToLowerInvariant();

            return key switch
            {
                "hero" => "Shared/Sections/Editors/_HeroEditor",
                "text" => "Shared/Sections/Editors/_TextEditor",
                "gallery" => "Shared/Sections/Editors/_GalleryEditor",
                _ => "Shared/Sections/Editors/_UnknownEditor"
            };
        }

        public string PreviewUrl
        {
            get
            {
                var slug = (Input?.Slug ?? string.Empty).Trim().Trim('/');
                if (string.IsNullOrWhiteSpace(slug))
                    slug = "home";

                return $"/{slug}?preview=true";
            }
        }

        public async Task<IActionResult> OnGetAsync(int id, bool saveSuccess = false, CancellationToken ct = default)
        {
            SaveSuccess = saveSuccess;

            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // ✅ Ensure DraftRevision exists (create once, on first edit)
            if (page.DraftRevisionId == null)
            {
                // Load a tracked entity for update
                var trackedPage = await _db.Pages
                    .FirstOrDefaultAsync(p =>
                        p.Id == id &&
                        p.TenantId == _tenant.TenantId &&
                        !p.IsDeleted, ct);

                if (trackedPage == null)
                    return NotFound();

                // Double-check in case another request created it between reads
                if (trackedPage.DraftRevisionId == null)
                {
                    var nextVersion = await _db.PageRevisions
                        .Where(r => r.TenantId == trackedPage.TenantId && r.PageId == trackedPage.Id)
                        .Select(r => r.VersionNumber)
                        .DefaultIfEmpty(0)
                        .MaxAsync(ct) + 1;

                    var draft = new PageRevision
                    {
                        TenantId = trackedPage.TenantId,
                        PageId = trackedPage.Id,
                        VersionNumber = nextVersion,
                        IsPublishedSnapshot = false,
                        Title = trackedPage.Title,
                        Slug = trackedPage.Slug,
                        LayoutKey = trackedPage.LayoutKey!,
                        MetaTitle = trackedPage.MetaTitle!,
                        MetaDescription = trackedPage.MetaDescription!,
                        OgImageAssetId = trackedPage.OgImageAssetId
                    };

                    _db.PageRevisions.Add(draft);
                    await _db.SaveChangesAsync(ct);

                    trackedPage.DraftRevisionId = draft.Id;
                    await _db.SaveChangesAsync(ct);
                }

                // refresh the no-tracking snapshot so later logic sees DraftRevisionId
                page = await _db.Pages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.Id == id &&
                        p.TenantId == _tenant.TenantId &&
                        !p.IsDeleted, ct);
            }

            Input = new PageEditVm
            {
                Id = page!.Id,
                Title = page.Title,
                Slug = page.Slug,
                LayoutKey = page.LayoutKey,
                MetaTitle = page.MetaTitle,
                MetaDescription = page.MetaDescription,
                OgImageAssetId = page.OgImageAssetId,
                PageStatusId = page.PageStatusId,
                PublishedAt = page.PublishedAt,
                IsActive = page.IsActive,
                IsDeleted = page.IsDeleted,
                ShowInNavigation = page.ShowInNavigation,
                NavigationOrder = page.NavigationOrder,
            };

            await BuildSelectListsAsync(Input.PageStatusId, ct);
            await LoadSectionCountAsync(page.Id, ct);

            // ⬇️ After this point, your section loading can safely use DraftRevisionId
            await LoadSectionsAsync(page.Id, ct);
            await LoadSectionTypeOptionsAsync(ct);

            return Page();
        }


        public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            await BuildSelectListsAsync(Input.PageStatusId, ct);

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // Normalize slug (if empty, derive from Title)
            Input.Slug = SanitizeSlug(Input.Slug, Input.Title);

            // Refresh section state for UI (if we return Page() on validation error)
            await LoadSectionCountAsync(page.Id, ct);
            await LoadSectionsAsync(page.Id, ct);
            await LoadSectionTypeOptionsAsync(ct);

            // Slug uniqueness per-tenant (exclude current page)
            if (!string.IsNullOrWhiteSpace(Input.Slug))
            {
                var normalizedSlug = Input.Slug.Trim().Trim('/');

                var slugExists = await _db.Pages
                    .AsNoTracking()
                    .AnyAsync(p =>
                        p.TenantId == _tenant.TenantId &&
                        p.Id != page.Id &&
                        !p.IsDeleted &&
                        p.Slug != null &&
                        p.Slug.ToLower() == normalizedSlug.ToLower(), ct);

                if (slugExists)
                    ModelState.AddModelError("Input.Slug", "Slug is already in use for this tenant.");
            }

            if (!ModelState.IsValid)
                return Page();

            // Snapshot old nav-affecting values BEFORE mapping
            var oldTitle = page.Title;
            var oldSlug = page.Slug;
            var oldShow = page.ShowInNavigation;
            var oldOrder = page.NavigationOrder;

            // Map fields
            page.Title = Input.Title?.Trim() ?? string.Empty;
            page.Slug = (Input.Slug ?? string.Empty).Trim().Trim('/');
            page.LayoutKey = string.IsNullOrWhiteSpace(Input.LayoutKey) ? null : Input.LayoutKey.Trim();
            page.MetaTitle = string.IsNullOrWhiteSpace(Input.MetaTitle) ? null : Input.MetaTitle.Trim();
            page.MetaDescription = string.IsNullOrWhiteSpace(Input.MetaDescription) ? null : Input.MetaDescription.Trim();
            page.OgImageAssetId = Input.OgImageAssetId;

            page.ShowInNavigation = Input.ShowInNavigation;
            page.NavigationOrder = Input.NavigationOrder;

            // IMPORTANT: Save does NOT change status (publish/unpublish/archive are explicit actions)
            // page.PageStatusId = Input.PageStatusId;

            page.IsActive = Input.IsActive;
            page.IsDeleted = Input.IsDeleted;

            // PublishedAt behavior: set when first published, keep existing otherwise
            if (page.PageStatusId == PageStatusIds.Published && page.PublishedAt == null)
                page.PublishedAt = DateTime.UtcNow;

            // Audit
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            // Determine if navigation needs refresh AFTER mapping
            var navChanged =
                !string.Equals(oldTitle, page.Title, StringComparison.Ordinal) ||
                !string.Equals(oldSlug, page.Slug, StringComparison.Ordinal) ||
                oldShow != page.ShowInNavigation ||
                oldOrder != page.NavigationOrder;

            await _db.SaveChangesAsync(ct);

            if (navChanged)
                _nav.Invalidate();

            return RedirectToPage(new { id = page.Id, saveSuccess = true });
        }

        public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // 0) Guard: must have a draft revision with sections
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == id)
                .Select(p => p.DraftRevisionId)
                .FirstOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TempData["Error"] = "Publish blocked. No draft revision found for this page.";
                return RedirectToPage(new { id });
            }

            var hasAnySections = await _db.PageRevisionSections
                .AsNoTracking()
                .AnyAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId, ct);

            if (!hasAnySections)
            {
                TempData["Error"] = "Publish blocked. Add at least one section before publishing.";
                return RedirectToPage(new { id });
            }

            // 1) Validate ALL sections before attempting publish
            var validation = await _pagePublishValidator.ValidateDraftSectionsAsync(id, ct);

            if (!validation.IsValid)
            {
                var message = string.Join(" ",
                    validation.Errors.SelectMany(e =>
                        e.Messages.Select(m => $"[Section #{e.SectionId} - {e.TypeKey}] {m}")
                    )
                );

                TempData["Error"] = "Publish blocked. Fix invalid sections first. " + message;
                return RedirectToPage(new { id });
            }

            // 2) Actor
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userId, out var userGuid);

            // 3) Delegate publish transaction to the service
            var result = await _pagePublishingService.PublishAsync(id, userGuid, ct);

            if (!result.Success)
            {
                TempData["Error"] = string.Join(" ", result.Errors);
                return RedirectToPage(new { id });
            }

            _nav.Invalidate();
            TempData["Success"] = "Page published.";
            return RedirectToPage(new { id });
        }


        public async Task<IActionResult> OnPostUnpublishAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId != PageStatusIds.Published)
            {
                TempData["Error"] = "Unpublish is only available for published pages.";
                return RedirectToPage(new { id });
            }

            page.PageStatusId = PageStatusIds.Draft;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();
            TempData["Success"] = "Page moved back to Draft.";
            return RedirectToPage(new { id });
        }

        // ===== Archive UX =====
        public async Task<IActionResult> OnPostArchiveAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId == PageStatusIds.Archived)
            {
                TempData["Success"] = "Page is already archived.";
                return RedirectToPage(new { id });
            }

            page.PageStatusId = PageStatusIds.Archived;
            page.ShowInNavigation = false; // archived pages should not appear in navigation

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();
            TempData["Success"] = "Page archived.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId != PageStatusIds.Archived)
            {
                TempData["Error"] = "Restore is only available for archived pages.";
                return RedirectToPage(new { id });
            }

            // Restore goes back to Draft (safe default)
            page.PageStatusId = PageStatusIds.Draft;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();
            TempData["Success"] = "Page restored to Draft.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostReorderSectionsAsync(
                [FromBody] ReorderSectionsRequest request,
                CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.PageId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid page id." });

            if (request.OrderedSectionIds == null || request.OrderedSectionIds.Count == 0)
                return new JsonResult(new { ok = false, message = "No sections provided." });

            // Resolve DraftRevisionId for this page (tenant-safe)
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == request.PageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .FirstOrDefaultAsync(ct);

            if (draftRevisionId == null)
                return new JsonResult(new { ok = false, message = "Draft revision not found." });

            // Load only draft sections that match the ordered IDs
            var sections = await _db.PageRevisionSections
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    request.OrderedSectionIds.Contains(s.Id))
                .ToListAsync(ct);

            if (sections.Count != request.OrderedSectionIds.Count)
                return new JsonResult(new { ok = false, message = "Invalid section list." });

            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userGuid);

            // Update sort order in the exact order provided
            for (int i = 0; i < request.OrderedSectionIds.Count; i++)
            {
                var secId = request.OrderedSectionIds[i];
                var section = sections.First(s => s.Id == secId);

                section.SortOrder = i + 1; // 1,2,3...
                section.UpdatedAt = DateTime.UtcNow;
                if (userGuid != Guid.Empty)
                    section.UpdatedBy = userGuid;
            }

            await _db.SaveChangesAsync(ct);
            return new JsonResult(new { ok = true });
        }


        private async Task LoadSectionCountAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                SectionCount = 0;
                return;
            }

            SectionCount = await _db.PageRevisionSections
                .AsNoTracking()
                .CountAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive, ct);
        }


        private async Task LoadSectionsAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                Sections = Array.Empty<SectionRowVm>();
                return;
            }

            Sections = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .Select(s => new SectionRowVm
                {
                    Id = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    SortOrder = s.SortOrder,
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync(ct);
        }


        private async Task BuildSelectListsAsync(int selectedPageStatusId, CancellationToken ct)
        {
            var statuses = await _db.PageStatuses
                .AsNoTracking()
                .Where(s => s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .ToListAsync(ct);

            PageStatusSelect = new SelectList(statuses, "Id", "Name", selectedPageStatusId);
            ViewData["PageStatuses"] = PageStatusSelect; // _Form.cshtml reads this
        }

        private static string SanitizeSlug(string? slug, string? title)
        {
            var value = (slug ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = (title ?? string.Empty).Trim();

            value = value.Trim().Trim('/');
            value = value.Replace(' ', '-');

            while (value.Contains("--"))
                value = value.Replace("--", "-");

            return value.ToLowerInvariant();
        }



        private async Task LoadSectionTypeOptionsAsync(CancellationToken ct)
        {
            SectionTypeOptions = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.IsActive && !st.IsDeleted)
                .OrderBy(st => st.SortOrder)
                .ThenBy(st => st.Name)
                .Select(st => new SelectListItem
                {
                    Value = st.Id.ToString(),
                    Text = st.Name
                })
                .ToListAsync(ct);
        }
        public async Task<IActionResult> OnPostAddSectionAsync(int id, int sectionTypeId, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // Ensure page exists & belongs to tenant (and fetch DraftRevisionId)
            var pageInfo = await _db.Pages
                .AsNoTracking()
                .Where(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
                .Select(p => new { p.Id, p.DraftRevisionId })
                .FirstOrDefaultAsync(ct);

            if (pageInfo == null)
                return NotFound();

            if (pageInfo.DraftRevisionId == null)
            {
                TempData["Error"] = "Draft revision not found. Please reload the page and try again.";
                return RedirectToPage(new { id });
            }

            // Ensure section type exists (and is active) + get Key for defaults
            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.Id == sectionTypeId && st.IsActive && !st.IsDeleted)
                .Select(st => new { st.Id, st.Key })
                .FirstOrDefaultAsync(ct);

            if (sectionType == null)
            {
                TempData["Error"] = "Invalid section type.";
                return RedirectToPage(new { id });
            }

            // Next sort order (draft)
            var nextSort = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == pageInfo.DraftRevisionId.Value &&
                    !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync(ct) ?? 0;

            var userGuid = GetUserIdOrEmpty();

            // Default settings JSON based on SectionType.Key (stable across environments)
            var typeKey = (sectionType.Key ?? "").Trim().ToLowerInvariant();

            var settingsJson = typeKey switch
            {
                "hero" => """{"headline":"Headline","subheadline":"Subheadline","ctaText":"Learn more","ctaUrl":"/"}""",
                "text" => """{"text":"Your text here..."}""",
                "gallery" => """{"items":[]}""",
                _ => "{}"
            };

            var section = new PageRevisionSection
            {
                TenantId = _tenant.TenantId,
                PageRevisionId = pageInfo.DraftRevisionId.Value,
                SectionTypeId = sectionTypeId,
                SortOrder = nextSort + 1,
                IsActive = true,
                IsDeleted = false,

                SettingsJson = settingsJson,

                CreatedAt = DateTime.UtcNow,
                CreatedBy = userGuid,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = userGuid
            };

            _db.PageRevisionSections.Add(section);
            await _db.SaveChangesAsync(ct);

            return RedirectToPage(new { id });
        }


        private Guid GetUserIdOrEmpty()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userId, out var g) ? g : Guid.Empty;
        }
        public async Task<IActionResult> OnPostSaveSectionAsync(
             [FromBody] SaveSectionRequest request,
             CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            // Load the draft section (revision-based)
            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId &&
                    !s.IsDeleted, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            var currentDraftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.DraftRevisionId == section.PageRevisionId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .FirstOrDefaultAsync(ct);

            if (currentDraftRevisionId == null)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });


            // Resolve SectionType.Key without relying on navigation properties
            var typeKey = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.Id == section.SectionTypeId && st.IsActive && !st.IsDeleted)
                .Select(st => st.Key)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(typeKey))
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Section type key not found. Ensure SectionTypes.Key is populated and the type is active.",
                    sectionId = section.Id,
                    sectionTypeId = section.SectionTypeId
                });
            }

            typeKey = typeKey.Trim().ToLowerInvariant();

            // Normalize JSON
            var json = string.IsNullOrWhiteSpace(request.SettingsJson)
                ? "{}"
                : request.SettingsJson.Trim();

            // Validate JSON shape for this section type
            var validation = await HttpContext.RequestServices
                .GetRequiredService<ISectionValidationService>()
                .ValidateAsync(typeKey, json);

            if (!validation.IsValid)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Validation failed.",
                    errors = validation.Errors
                });
            }

            // Apply
            section.SettingsJson = json;

            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userGuid);
            if (userGuid != Guid.Empty)
                section.UpdatedBy = userGuid;

            section.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }


        private Task<bool> PageHasAnySectionsAsync(int pageId, CancellationToken ct)
        {
            return _db.PageSections
                .AsNoTracking()
                .AnyAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageId == pageId &&
                    s.IsActive &&
                    !s.IsDeleted, ct);
        }

        public sealed class ReorderSectionsRequest
        {
            public int PageId { get; set; }
            public List<int> OrderedSectionIds { get; set; } = new();
        }
        public sealed class SaveSectionRequest
        {
            public int SectionId { get; set; }
            public string? SettingsJson { get; set; }
        }
    }
}
