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
        public IReadOnlyList<PageSection> Sections { get; private set; } = Array.Empty<PageSection>();

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
        public static string GetEditorPartialPath(PageSection s) => s.SectionTypeId switch
        {
            1 => "/Pages/Admin/Pages/Sections/Partials/_HeroEditor.cshtml",
            2 => "/Pages/Admin/Pages/Sections/Partials/_TextEditor.cshtml",
            3 => "/Pages/Admin/Pages/Sections/Partials/_GalleryEditor.cshtml",
            _ => "/Pages/Admin/Pages/Sections/Partials/_TextEditor.cshtml"
        };

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

            Input = new PageEditVm
            {
                Id = page.Id,
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

            if (request.OrderedSectionIds == null || request.OrderedSectionIds.Count == 0)
                return new JsonResult(new { ok = false, message = "No sections provided." });

            var sections = await _db.PageSections
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageId == request.PageId &&
                    !s.IsDeleted &&
                    request.OrderedSectionIds.Contains(s.Id))
                .ToListAsync(ct);

            if (sections.Count != request.OrderedSectionIds.Count)
                return new JsonResult(new { ok = false, message = "Invalid section list." });

            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userGuid);

            for (int i = 0; i < request.OrderedSectionIds.Count; i++)
            {
                var id = request.OrderedSectionIds[i];
                var section = sections.First(s => s.Id == id);

                section.SortOrder = i + 1; // 1, 2, 3, ...
                section.UpdatedAt = DateTime.UtcNow;
                if (userGuid != Guid.Empty)
                    section.UpdatedBy = userGuid;
            }

            await _db.SaveChangesAsync(ct);
            return new JsonResult(new { ok = true });
        }

        private async Task LoadSectionCountAsync(int pageId, CancellationToken ct)
        {
            SectionCount = await _db.PageSections
                .AsNoTracking()
                .CountAsync(s => s.TenantId == _tenant.TenantId && s.PageId == pageId && !s.IsDeleted, ct);
        }

        private async Task LoadSectionsAsync(int pageId, CancellationToken ct)
        {
            Sections = await _db.PageSections
                .AsNoTracking()
                .Where(s => s.TenantId == _tenant.TenantId && s.PageId == pageId && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
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

        // Simple label for the reorder list
        public static string GetSectionTitle(PageSection s) => s.SectionTypeId switch
        {
            1 => "Hero",
            2 => "Text",
            3 => "Gallery",
            _ => $"SectionType {s.SectionTypeId}"
        };

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

            // Ensure page exists & belongs to tenant
            var pageExists = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (!pageExists)
                return NotFound();

            // Ensure section type exists (and is active)
            var sectionTypeExists = await _db.SectionTypes
                .AsNoTracking()
                .AnyAsync(st => st.Id == sectionTypeId && st.IsActive && !st.IsDeleted, ct);

            if (!sectionTypeExists)
            {
                TempData["Error"] = "Invalid section type.";
                return RedirectToPage(new { id });
            }

            // Next sort order
            var nextSort = await _db.PageSections
                .AsNoTracking()
                .Where(s => s.TenantId == _tenant.TenantId && s.PageId == id && !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync(ct) ?? 0;

            var userGuid = GetUserIdOrEmpty();

            // IMPORTANT: your PageSection uses SettingsJson (not ContentJson)
            var settingsJson = sectionTypeId switch
            {
                1 => """{"headline":"Headline","subheadline":"Subheadline","ctaText":"Learn more","ctaUrl":"/"}""",
                2 => """{"text":"Your text here..."}""",
                3 => """{"items":[]}""",
                _ => "{}"
            };

            var section = new PageSection
            {
                TenantId = _tenant.TenantId,
                PageId = id,
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

            _db.PageSections.Add(section);
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

            var section = await _db.PageSections
                .AsTracking()
                .Include(s => s.SectionType)
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId &&
                    !s.IsDeleted, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // IMPORTANT: this is what is failing for you right now
            if (section.SectionType == null)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Section type not resolved (navigation is null).",
                    sectionId = section.Id,
                    sectionTypeId = section.SectionTypeId
                });
            }

            if (string.IsNullOrWhiteSpace(section.SectionType.Key))
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Section type key is empty. Populate SectionType.Key in DB.",
                    sectionId = section.Id,
                    sectionTypeId = section.SectionTypeId,
                    sectionTypeName = section.SectionType.Name
                });
            }

            // Normalize JSON
            var json = string.IsNullOrWhiteSpace(request.SettingsJson)
                ? "{}"
                : request.SettingsJson.Trim();

            // Validate JSON shape for this section type
            var validation = await HttpContext.RequestServices
                .GetRequiredService<ISectionValidationService>()
                .ValidateAsync(section.SectionType.Key, json);

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

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                section.UpdatedBy = userGuid;

            section.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
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
