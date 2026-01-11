using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public EditModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        [BindProperty]
        public PageEditVm Input { get; set; } = new();

        public SelectList PageStatusSelect { get; private set; } = default!;
        public bool SaveSuccess { get; private set; }
        public string TenantHost => _tenant.Host ?? string.Empty;

        // ===== Publish/Archive UI state =====
        public int SectionCount { get; private set; }
        public bool HasSections => SectionCount > 0;
        public bool HasSlug => !string.IsNullOrWhiteSpace(Input?.Slug);

        public bool IsPublished => Input.PageStatusId == PageStatusIds.Published;
        public bool IsDraft => Input.PageStatusId == PageStatusIds.Draft;
        public bool IsArchived => Input.PageStatusId == PageStatusIds.Archived;

        // CanPublish: enabled only when Draft (or not Published/Archived), has slug, has sections
        public bool CanPublish => !IsPublished && !IsArchived && HasSlug && HasSections;

        // CanUnpublish: allowed only when Published
        public bool CanUnpublish => IsPublished;

        // CanArchive: allowed when not archived (Draft or Published)
        public bool CanArchive => !IsArchived;

        // CanRestore: allowed only when archived
        public bool CanRestore => IsArchived;

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

            // Update section count for UI (in case we return Page() on validation error)
            await LoadSectionCountAsync(page.Id, ct);

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

            await _db.SaveChangesAsync(ct);

            return RedirectToPage(new { id = page.Id, saveSuccess = true });
        }

        public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken ct = default)
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

            // Guardrails
            if (string.IsNullOrWhiteSpace(page.Slug))
            {
                TempData["Error"] = "Cannot publish: Slug is required.";
                return RedirectToPage(new { id });
            }

            var hasSections = await _db.PageSections
                .AsNoTracking()
                .AnyAsync(s => s.PageId == page.Id && !s.IsDeleted, ct);

            if (!hasSections)
            {
                TempData["Error"] = "Cannot publish: This page has no sections. Add at least one section before publishing.";
                return RedirectToPage(new { id });
            }

            if (page.PageStatusId == PageStatusIds.Archived)
            {
                TempData["Error"] = "Cannot publish: This page is archived. Restore it first.";
                return RedirectToPage(new { id });
            }

            page.PageStatusId = PageStatusIds.Published;

            if (page.PublishedAt == null)
                page.PublishedAt = DateTime.UtcNow;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

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

            // Recommended UX: archived pages should not appear in navigation
            page.ShowInNavigation = false;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userId, out var userGuid))
                page.UpdatedBy = userGuid;

            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

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

            TempData["Success"] = "Page restored to Draft.";
            return RedirectToPage(new { id });
        }

        private async Task LoadSectionCountAsync(int pageId, CancellationToken ct)
        {
            SectionCount = await _db.PageSections
                .AsNoTracking()
                .CountAsync(s => s.PageId == pageId && !s.IsDeleted, ct);
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

            // _Form.cshtml reads ViewData["PageStatuses"]
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
    }
}
