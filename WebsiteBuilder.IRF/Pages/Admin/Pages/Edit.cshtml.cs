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

        /// <summary>
        /// Computed preview URL for the current page.
        /// </summary>
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
        public async Task<IActionResult> OnPostPublishAsync(int id)
        {
            // Load page
            var entity = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (entity == null) return NotFound();

            // Optional: enforce basic requirements before publish
            // (slug not empty, etc.)
            if (string.IsNullOrWhiteSpace(entity.Slug))
            {
                TempData["Error"] = "Cannot publish: Slug is required.";
                return RedirectToPage(new { id });
            }

            entity.PageStatusId = PageStatusIds.Published;

            // Optional if you track these:
            // entity.PublishedAt = DateTime.UtcNow;
            // entity.UpdatedAt = DateTime.UtcNow;
            // entity.UpdatedBy = User.Identity?.Name;

            await _db.SaveChangesAsync();

            TempData["Success"] = "Page published.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostUnpublishAsync(int id)
        {
            var entity = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (entity == null) return NotFound();

            entity.PageStatusId = PageStatusIds.Draft;

            // Optional audit fields:
            // entity.UpdatedAt = DateTime.UtcNow;
            // entity.UpdatedBy = User.Identity?.Name;

            await _db.SaveChangesAsync();

            TempData["Success"] = "Page moved back to Draft.";
            return RedirectToPage(new { id });
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
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // Ensure dropdown lists exist again on validation errors
            await BuildSelectListsAsync(Input.PageStatusId, ct);

            // Must load the tracked entity to update it
            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // Normalize slug (if empty, derive from Title)
            Input.Slug = SanitizeSlug(Input.Slug, Input.Title);

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
                {
                    ModelState.AddModelError("Input.Slug", "Slug is already in use for this tenant.");
                }
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

            page.PageStatusId = Input.PageStatusId;
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

        private async Task BuildSelectListsAsync(int selectedPageStatusId, CancellationToken ct)
        {
            var statuses = await _db.PageStatuses
                .AsNoTracking()
                .Where(s => s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .ToListAsync(ct);

            PageStatusSelect = new SelectList(statuses, "Id", "Name", selectedPageStatusId);

            // If your Form partial expects ViewData["PageStatusSelect"], set it here
            ViewData["PageStatuses"] = PageStatusSelect;
        }

        private static string SanitizeSlug(string? slug, string? title)
        {
            var value = (slug ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = (title ?? string.Empty).Trim();

            // Basic slug normalization:
            // - trim
            // - replace spaces with hyphens
            // - remove leading/trailing slashes
            value = value.Trim().Trim('/');
            value = value.Replace(' ', '-');

            // Collapse repeated hyphens
            while (value.Contains("--"))
                value = value.Replace("--", "-");

            // Lowercase
            value = value.ToLowerInvariant();

            return value;
        }
    }
}
