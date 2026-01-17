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
        private readonly ISectionValidationService _sectionValidation;

        public EditModel(
            DataContext db,
            ITenantContext tenant,
            IPagePublishingService pagePublishingService,
            ITenantNavigationService nav,
            PagePublishValidator pagePublishValidator,
            ISectionValidationService sectionValidation)
        {
            _db = db;
            _tenant = tenant;
            _pagePublishingService = pagePublishingService;
            _nav = nav;
            _pagePublishValidator = pagePublishValidator;
            _sectionValidation = sectionValidation;
        }

        [BindProperty]
        public PageEditVm Input { get; set; } = new();

        public SelectList PageStatusSelect { get; private set; } = default!;
        public bool SaveSuccess { get; private set; }
        public string TenantHost => _tenant.Host ?? string.Empty;

        // Sections (for drag-drop reorder UI)
        public List<SectionRowVm> Sections { get; set; } = new();

        // Trash (soft-deleted draft sections)
        public List<SectionRowVm> TrashedSections { get; set; } = new();

        // ===== Publish/Archive UI state =====
        public int SectionCount { get; private set; }
        public int TrashCount { get; private set; }
        public bool HasSections => SectionCount > 0;

        public bool HasSlug => !string.IsNullOrWhiteSpace(Input?.Slug);

        public bool IsPublished => Input.PageStatusId == PageStatusIds.Published;
        public bool IsDraft => Input.PageStatusId == PageStatusIds.Draft;
        public bool IsArchived => Input.PageStatusId == PageStatusIds.Archived;

        public bool CanPublish => !IsPublished && !IsArchived && HasSlug && HasSections;
        public bool CanUnpublish => IsPublished;
        public bool CanArchive => !IsArchived;
        public bool CanRestore => IsArchived;

        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        public sealed record SectionTypeLookupVm(int Id, string Name, string Key);

        public IReadOnlyList<SectionTypeLookupVm> SectionTypeLookup { get; private set; }
            = Array.Empty<SectionTypeLookupVm>();

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
                "hero" => "~/Pages/Shared/Sections/Editors/_HeroEditor.cshtml",
                "text" => "~/Pages/Shared/Sections/Editors/_TextEditor.cshtml",
                "gallery" => "~/Pages/Shared/Sections/Editors/_GalleryEditor.cshtml",
                _ => "~/Pages/Shared/Sections/Editors/_UnknownEditor.cshtml"
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

        // =======================
        // PAGE LOAD (GET)
        // =======================
        public async Task<IActionResult> OnGetAsync(int id, bool saveSuccess = false, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // Ensure we have a draft revision (required for revision sections CRUD)
            await EnsureDraftRevisionExistsAsync(page.Id, ct);

            // Map to VM (keep this minimal and safe)
            Input = new PageEditVm
            {
                Id = page.Id,
                Title = page.Title,
                Slug = page.Slug,
                PageStatusId = page.PageStatusId,
                ShowInNavigation = page.ShowInNavigation,
                NavigationOrder = page.NavigationOrder
            };

            SaveSuccess = saveSuccess;

            await BuildSelectListsAsync(Input.PageStatusId, ct);
            await LoadSectionTypeLookupAsync(ct);
            await LoadSectionTypeOptionsAsync(ct);
            await LoadSectionCountAsync(page.Id, ct);
            await LoadTrashCountAsync(page.Id, ct);
            await LoadSectionsAsync(page.Id, ct);
            await LoadTrashedSectionsAsync(page.Id, ct);

            return Page();
        }

        // =======================
        // PAGE SAVE (POST)
        // =======================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            if (!ModelState.IsValid)
            {
                await BuildSelectListsAsync(Input.PageStatusId, ct);
                await LoadSectionTypeLookupAsync(ct);
                await LoadSectionTypeOptionsAsync(ct);
                await LoadSectionCountAsync(Input.Id, ct);
                await LoadTrashCountAsync(Input.Id, ct);
                await LoadSectionsAsync(Input.Id, ct);
                await LoadTrashedSectionsAsync(Input.Id, ct);
                return Page();
            }

            var page = await _db.Pages
                .FirstOrDefaultAsync(p => p.Id == Input.Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // IMPORTANT: keep Slug sanitized and stable
            page.Title = (Input.Title ?? "").Trim();
            page.Slug = SanitizeSlug(Input.Slug, page.Title);

            // Do not allow manual status changes here (Publish/Unpublish/Archive handlers own status)
            // If you still have a status dropdown in _Form.cshtml, this enforces the server-side rule.

            page.ShowInNavigation = Input.ShowInNavigation;
            page.NavigationOrder = Input.NavigationOrder;

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedAt = DateTime.UtcNow;
            page.UpdatedBy = userGuid;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            return RedirectToPage(new { id = page.Id, saveSuccess = true });
        }

        // =======================
        // PUBLISH / UNPUBLISH / ARCHIVE
        // =======================
        public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // Guard: must have a draft revision with sections
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == id && !p.IsDeleted)
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
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive, ct);

            if (!hasAnySections)
            {
                TempData["Error"] = "Publish blocked. Add at least one section before publishing.";
                return RedirectToPage(new { id });
            }

            // Validate ALL sections before attempting publish
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

            var userGuid = GetUserIdOrEmpty();

            // Delegate publish transaction to the service
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

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedBy = userGuid;
            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            TempData["Success"] = "Page moved back to Draft.";
            return RedirectToPage(new { id });
        }

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
            page.ShowInNavigation = false;

            var userGuid = GetUserIdOrEmpty();
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

            page.PageStatusId = PageStatusIds.Draft;

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedBy = userGuid;
            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            TempData["Success"] = "Page restored to Draft.";
            return RedirectToPage(new { id });
        }

        // =======================
        // SECTIONS CRUD (DRAFT REVISION)
        // =======================
        public async Task<IActionResult> OnPostAddSectionAsync(int id, int sectionTypeId, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // Ensure page exists & belongs to tenant (and ensure DraftRevisionId exists)
            var page = await _db.Pages
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            var draftRevisionId = await EnsureDraftRevisionExistsAsync(page.Id, ct);

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

            var nextSort = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync(ct) ?? 0;

            var userGuid = GetUserIdOrEmpty();

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
                PageRevisionId = draftRevisionId,
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

            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == request.PageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .FirstOrDefaultAsync(ct);

            if (draftRevisionId == null)
                return new JsonResult(new { ok = false, message = "Draft revision not found." });

            var sections = await _db.PageRevisionSections
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive &&
                    request.OrderedSectionIds.Contains(s.Id))
                .ToListAsync(ct);

            if (sections.Count != request.OrderedSectionIds.Count)
                return new JsonResult(new { ok = false, message = "Invalid section list." });

            var byId = sections.ToDictionary(s => s.Id);

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            var strategy = _db.Database.CreateExecutionStrategy();

            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    // TEMP RANGE (positive) to avoid collisions + any "SortOrder > 0" constraints
                    var currentMax = await _db.PageRevisionSections
                        .AsNoTracking()
                        .Where(s =>
                            s.TenantId == _tenant.TenantId &&
                            s.PageRevisionId == draftRevisionId &&
                            !s.IsDeleted &&
                            s.IsActive)
                        .Select(s => (int?)s.SortOrder)
                        .MaxAsync(ct) ?? 0;

                    var tempStart = currentMax + 1000;

                    // Phase 1: move to unique temporary values
                    var idx = 0;
                    foreach (var id in request.OrderedSectionIds)
                    {
                        var s = byId[id];
                        s.SortOrder = tempStart + idx;
                        s.UpdatedAt = now;
                        s.UpdatedBy = userGuid;
                        idx++;
                    }

                    await _db.SaveChangesAsync(ct);

                    // Phase 2: apply final 1..N
                    var order = 1;
                    foreach (var id in request.OrderedSectionIds)
                    {
                        var s = byId[id];
                        s.SortOrder = order;
                        s.UpdatedAt = now;
                        s.UpdatedBy = userGuid;
                        order++;
                    }

                    await _db.SaveChangesAsync(ct);

                    await tx.CommitAsync(ct);
                });

                return new JsonResult(new { ok = true });
            }
            catch
            {
                return new JsonResult(new { ok = false, message = "Reorder failed. Please retry." });
            }
        }



        public async Task<IActionResult> OnPostSaveSectionAsync(
            [FromBody] SaveSectionRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // Ensure this section belongs to a current draft revision of a page in this tenant
            var isCurrentDraft = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.DraftRevisionId == section.PageRevisionId &&
                    !p.IsDeleted, ct);

            if (!isCurrentDraft)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });

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

            var json = string.IsNullOrWhiteSpace(request.SettingsJson)
                ? "{}"
                : request.SettingsJson.Trim();

            var validation = await _sectionValidation.ValidateAsync(typeKey, json);

            if (!validation.IsValid)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Validation failed.",
                    errors = validation.Errors
                });
            }

            section.SettingsJson = json;

            var userGuid = GetUserIdOrEmpty();
            section.UpdatedBy = userGuid;
            section.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        // =======================
        // LOAD HELPERS
        // =======================
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
                Sections = new List<SectionRowVm>();
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

        private async Task LoadSectionTypeLookupAsync(CancellationToken ct)
        {
            SectionTypeLookup = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.IsActive && !st.IsDeleted)
                .OrderBy(st => st.SortOrder)
                .ThenBy(st => st.Name)
                .Select(st => new SectionTypeLookupVm(st.Id, st.Name, st.Key))
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
            ViewData["PageStatuses"] = PageStatusSelect;
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

        private Guid GetUserIdOrEmpty()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userId, out var g) ? g : Guid.Empty;
        }

        private async Task<int> EnsureDraftRevisionExistsAsync(int pageId, CancellationToken ct)
        {
            var page = await _db.Pages
                .FirstOrDefaultAsync(p => p.Id == pageId && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                throw new InvalidOperationException("Page not found.");

            if (page.DraftRevisionId.HasValue && page.DraftRevisionId.Value > 0)
                return page.DraftRevisionId.Value;

            // Create a minimal draft revision row
            var rev = new PageRevision
            {
                TenantId = _tenant.TenantId,
                PageId = pageId
            };

            _db.PageRevisions.Add(rev);
            await _db.SaveChangesAsync(ct);

            page.DraftRevisionId = rev.Id;
            page.UpdatedAt = DateTime.UtcNow;
            page.UpdatedBy = GetUserIdOrEmpty();

            await _db.SaveChangesAsync(ct);

            return rev.Id;
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

        public sealed class SectionIdRequest
        {
            public int SectionId { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteRevisionSectionAsync(
            [FromBody] SectionIdRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // Draft-only guard
            var isCurrentDraft = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.DraftRevisionId == section.PageRevisionId, ct);

            if (!isCurrentDraft)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });

            if (section.IsDeleted)
                return new JsonResult(new { ok = true }); // idempotent

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            section.IsDeleted = true;
            section.DeletedAt = now;
            section.DeletedBy = userGuid;
            section.UpdatedAt = now;
            section.UpdatedBy = userGuid;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        public async Task<IActionResult> OnPostRestoreRevisionSectionAsync(
            [FromBody] SectionIdRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId, ct);


            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // Draft-only guard
            var isCurrentDraft = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.DraftRevisionId == section.PageRevisionId, ct);

            if (!isCurrentDraft)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });

            if (!section.IsDeleted)
                return new JsonResult(new { ok = true }); // idempotent

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            section.IsDeleted = false;
            section.DeletedAt = null;
            section.DeletedBy = null;
            section.UpdatedAt = now;
            section.UpdatedBy = userGuid;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        private async Task LoadTrashCountAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TrashCount = 0;
                return;
            }

            TrashCount = await _db.PageRevisionSections
                .AsNoTracking()
                .CountAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    s.IsDeleted, ct);
        }

        private async Task LoadTrashedSectionsAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TrashedSections = new List<SectionRowVm>();
                return;
            }

            TrashedSections = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    s.IsDeleted)
                .OrderByDescending(s => s.DeletedAt)
                .ThenByDescending(s => s.Id)
                .Select(s => new SectionRowVm
                {
                    Id = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    SortOrder = s.SortOrder,
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync(ct);
        }

    }
}
