using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Helpers;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;
using Page = WebsiteBuilder.Models.Page;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class CreateModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public CreateModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        [BindProperty]
        public PageEditVm Input { get; set; } = new()
        {
            PageStatusId = PageStatusIds.Draft,
            IsActive = true
        };

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_tenant.IsResolved)
                return NotFound();

            await LoadPageStatusesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!_tenant.IsResolved)
                return NotFound();

            await LoadPageStatusesAsync();

            // Normalize slug before validation
            Input.Slug = SlugUtil.Normalize(Input.Slug);

            if (!ModelState.IsValid)
                return Page();

            // Enforce slug uniqueness per tenant (excluding soft-deleted pages)
            var slugExists = await _db.Pages.AsNoTracking().AnyAsync(p =>
                p.TenantId == _tenant.TenantId &&
                p.Slug == Input.Slug &&
                !p.IsDeleted);

            if (slugExists)
            {
                ModelState.AddModelError(nameof(Input.Slug), "Slug already exists for this tenant.");
                return Page();
            }

            var userId = GetUserIdOrEmpty();

            var entity = new Page
            {
                TenantId = _tenant.TenantId,
                OwnerUserId = userId,                 // ✅ CRITICAL
                Title = Input.Title.Trim(),
                Slug = Input.Slug,
                PageStatusId = PageStatusIds.Draft,   // ✅ FORCE DRAFT
                LayoutKey = Input.LayoutKey?.Trim() ?? "Default",
                MetaTitle = string.IsNullOrWhiteSpace(Input.MetaTitle) ? null : Input.MetaTitle.Trim(),
                MetaDescription = string.IsNullOrWhiteSpace(Input.MetaDescription) ? null : Input.MetaDescription.Trim(),
                OgImageAssetId = Input.OgImageAssetId,

                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };

            _db.Pages.Add(entity);
            await _db.SaveChangesAsync();

            return RedirectToPage("./Edit", new { id = entity.Id, created = true });
        }

        private async Task LoadPageStatusesAsync()
        {
            var statuses = await _db.PageStatuses
                .AsNoTracking()
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name
                })
                .ToListAsync();

            ViewData["PageStatuses"] = statuses;
        }

        private Guid GetUserIdOrEmpty()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
