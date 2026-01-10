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

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!_tenant.IsResolved) return NotFound();

            await LoadPageStatusesAsync();

            var entity = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted);

            if (entity is null)
                return NotFound();

            Input = new PageEditVm
            {
                Id = entity.Id,
                Title = entity.Title,
                Slug = entity.Slug,
                PageStatusId = entity.PageStatusId,
                LayoutKey = entity.LayoutKey,
                MetaTitle = entity.MetaTitle,
                MetaDescription = entity.MetaDescription,
                OgImageAssetId = entity.OgImageAssetId,
                IsActive = entity.IsActive
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!_tenant.IsResolved) return NotFound();

            await LoadPageStatusesAsync();

            if (id != Input.Id)
                return BadRequest();

            Input.Slug = NormalizeSlug(Input.Slug);

            if (!ModelState.IsValid)
                return Page();

            var entity = await _db.Pages.FirstOrDefaultAsync(p =>
                p.Id == id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted);

            if (entity is null)
                return NotFound();

            // Enforce uniqueness excluding current record
            var slugExists = await _db.Pages.AsNoTracking().AnyAsync(p =>
                p.TenantId == _tenant.TenantId &&
                p.Slug == Input.Slug &&
                p.Id != id &&
                !p.IsDeleted);

            if (slugExists)
            {
                ModelState.AddModelError(nameof(Input.Slug), "Slug already exists for this tenant.");
                return Page();
            }

            var previousStatus = entity.PageStatusId;

            entity.Title = Input.Title.Trim();
            entity.Slug = Input.Slug;
            entity.PageStatusId = Input.PageStatusId;
            entity.LayoutKey = string.IsNullOrWhiteSpace(Input.LayoutKey) ? null : Input.LayoutKey.Trim();
            entity.MetaTitle = string.IsNullOrWhiteSpace(Input.MetaTitle) ? null : Input.MetaTitle.Trim();
            entity.MetaDescription = string.IsNullOrWhiteSpace(Input.MetaDescription) ? null : Input.MetaDescription.Trim();
            entity.OgImageAssetId = Input.OgImageAssetId;
            entity.IsActive = Input.IsActive;

            // Publish timestamp rule:
            // - If transitioning into Published and PublishedAt is null, set it once.
            if (previousStatus != PageStatusIds.Published && entity.PageStatusId == PageStatusIds.Published)
            {
                entity.PublishedAt ??= DateTime.UtcNow;
            }

            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return RedirectToPage("./Edit", new { id, saved = true });
        }

        private async Task LoadPageStatusesAsync()
        {
            var statuses = await _db.PageStatuses
                .AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name
                })
                .ToListAsync();

            ViewData["PageStatuses"] = statuses;
        }

        private static string NormalizeSlug(string? slug)
        {
            slug ??= string.Empty;
            slug = slug.Trim();

            while (slug.StartsWith("/"))
                slug = slug.Substring(1);

            slug = slug.ToLowerInvariant();
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
            slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
            slug = slug.Trim('-');

            return slug;
        }

        private Guid GetUserIdOrEmpty()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
