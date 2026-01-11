using System.Security.Claims;
using System.Text.RegularExpressions;
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
            if (!_tenant.IsResolved)
                return NotFound();

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
            if (!_tenant.IsResolved)
                return NotFound();

            await LoadPageStatusesAsync();

            if (id != Input.Id)
                return BadRequest();

            // Normalize early so validation + uniqueness check uses final value
            Input.Title = (Input.Title ?? string.Empty).Trim();
            Input.Slug = NormalizeSlug(Input.Slug);

            // Server-side validation: ensure posted status is valid (prevents tampering / bad posts)
            var isValidStatus = await _db.PageStatuses.AsNoTracking().AnyAsync(s => s.Id == Input.PageStatusId);
            if (!isValidStatus)
                ModelState.AddModelError(nameof(Input.PageStatusId), "Invalid status.");

            if (string.IsNullOrWhiteSpace(Input.Title))
                ModelState.AddModelError(nameof(Input.Title), "Title is required.");

            if (string.IsNullOrWhiteSpace(Input.Slug))
                ModelState.AddModelError(nameof(Input.Slug), "Slug is required.");

            if (!ModelState.IsValid)
            {
                await LoadPageStatusesAsync();
                return Page();
            }


            var entity = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted);

            if (entity is null)
                return NotFound();

            // Uniqueness: slug must be unique per tenant (case-insensitive)
            var slugExists = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.Id != id &&
                    p.Slug.ToLower() == Input.Slug.ToLower());

            if (slugExists)
            {
                ModelState.AddModelError(nameof(Input.Slug), "Slug already exists for this tenant.");
                return Page();
            }

            var previousStatus = entity.PageStatusId;

            entity.Title = Input.Title;
            entity.Slug = Input.Slug;
            entity.PageStatusId = Input.PageStatusId;

            entity.LayoutKey = string.IsNullOrWhiteSpace(Input.LayoutKey) ? null : Input.LayoutKey.Trim();
            entity.MetaTitle = string.IsNullOrWhiteSpace(Input.MetaTitle) ? null : Input.MetaTitle.Trim();
            entity.MetaDescription = string.IsNullOrWhiteSpace(Input.MetaDescription) ? null : Input.MetaDescription.Trim();
            entity.OgImageAssetId = Input.OgImageAssetId;

            entity.IsActive = Input.IsActive;

            // Publish timestamp:
            // - Set once when transitioning into Published
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
            ViewData["PageStatuses"] = await _db.PageStatuses
                .AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name
                })
                .ToListAsync();
        }


        private static string NormalizeSlug(string? slug)
        {
            slug ??= string.Empty;

            // trim whitespace and slashes
            slug = slug.Trim();
            slug = slug.Trim('/');

            // lowercase
            slug = slug.ToLowerInvariant();

            // spaces -> hyphen
            slug = Regex.Replace(slug, @"\s+", "-");

            // collapse multiple hyphens
            slug = Regex.Replace(slug, @"-+", "-");

            // allow only url-safe characters (optional but recommended)
            // keep letters, digits, hyphen
            slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

            // trim hyphens again after cleanup
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
