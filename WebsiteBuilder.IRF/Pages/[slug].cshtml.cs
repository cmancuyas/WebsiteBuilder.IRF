using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages
{
    public class _slug_Model : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public _slug_Model(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        // Domain entity (fully qualify to avoid Razor Pages "Page" ambiguity)
        public WebsiteBuilder.Models.Page? PageEntity { get; private set; }

        // Exposed to the view (banner, etc.)
        public bool IsPreviewMode { get; private set; }

        public async Task<IActionResult> OnGetAsync(string slug, bool preview = false)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            if (string.IsNullOrWhiteSpace(slug))
                return NotFound();

            slug = slug.Trim();

            // Preview only allowed when authenticated (production baseline).
            // If you want stricter control, replace this with a policy/claim check.
            IsPreviewMode = preview && User?.Identity?.IsAuthenticated == true;

            // Disable caching when previewing (critical)
            if (IsPreviewMode)
            {
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";
            }

            var query = _db.Pages
                .AsNoTracking()
                .Include(p => p.Sections)
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.Slug == slug &&
                    p.IsActive &&
                    !p.IsDeleted);

            // Public sees Published only. Preview can see any status.
            if (!IsPreviewMode)
            {
                query = query.Where(p => p.PageStatusId == PageStatusIds.Published);
            }

            PageEntity = await query.FirstOrDefaultAsync();

            if (PageEntity is null)
                return NotFound();

            // Filter sections: keep only non-deleted + active (since BaseModel has IsActive/IsDeleted)
            PageEntity.Sections = PageEntity.Sections
                .Where(s => s.TenantId == _tenant.TenantId && s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToList();

            return Page();
        }
    }
}
