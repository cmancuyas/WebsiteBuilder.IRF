using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
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

        public WebsiteBuilder.Models.Page? PageEntity { get; private set; }
        public bool IsPreview { get; private set; }

        // Catch-all route param: /{**slug}
        public async Task<IActionResult> OnGetAsync(string? slug)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            // Preview requested?
            var previewRequested = IsPreviewRequested();

            // If preview is requested but user is NOT allowed:
            // - do NOT 404 (causes exactly what you are seeing)
            // - just ignore preview and render published
            IsPreview = previewRequested && UserCanPreview();

            var normalizedSlug = NormalizeSlug(slug);
            if (string.IsNullOrWhiteSpace(normalizedSlug))
            {
                normalizedSlug = "home";
            }

            var query = _db.Pages
                .AsNoTracking()
                .Include(p => p.Sections)
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.Slug == normalizedSlug);

            if (!IsPreview)
            {
                // Public visitors only see Published pages
                query = query.Where(p => p.PageStatusId == PageStatusIds.Published);
            }
            else
            {
                // Preview can see Draft + Published (but not Archived)
                query = query.Where(p => p.PageStatusId != PageStatusIds.Archived);
                ApplyNoCacheHeaders();
            }

            PageEntity = await query.FirstOrDefaultAsync(HttpContext.RequestAborted);

            if (PageEntity is null)
                return NotFound();

            PageEntity.Sections = (PageEntity.Sections ?? new List<PageSection>())
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.IsActive &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToList();

            return Page();
        }

        private bool IsPreviewRequested()
        {
            var val = Request.Query["preview"].ToString();
            if (string.IsNullOrWhiteSpace(val)) return false;

            return val.Equals("1", StringComparison.OrdinalIgnoreCase)
                || val.Equals("true", StringComparison.OrdinalIgnoreCase)
                || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private bool UserCanPreview()
        {
            // Minimum: must be authenticated
            if (User.Identity?.IsAuthenticated != true)
                return false;

            // Role-based
            if (User.IsInRole("Admin"))
                return true;

            // Claims-based (adjust as needed)
            if (User.HasClaim("Permission", "Pages.Preview"))
                return true;

            return false;
        }

        private void ApplyNoCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }

        private static string NormalizeSlug(string? slug)
        {
            slug ??= string.Empty;
            slug = slug.Trim();
            slug = slug.Trim('/');

            slug = slug.ToLowerInvariant();
            slug = Regex.Replace(slug, @"\s+", "-");
            slug = Regex.Replace(slug, @"-+", "-");
            slug = slug.Trim('-');

            return slug;
        }
    }
}
