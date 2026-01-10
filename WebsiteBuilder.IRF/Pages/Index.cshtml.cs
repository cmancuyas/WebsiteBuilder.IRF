using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages
{
    public class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public IndexModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public bool IsTenantResolved => _tenant.IsResolved;
        public Guid TenantId => _tenant.TenantId;
        public string TenantHost => _tenant.Host;

        public Tenant? TenantEntity { get; private set; }
        public WebsiteBuilder.Models.Page? HomePage { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // If platform (localhost/root marketing domain/etc.), just show the normal Index page
            if (!_tenant.IsResolved)
                return Page();

            TenantEntity = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.Id == _tenant.TenantId &&
                    t.IsActive &&
                    !t.IsDeleted);

            if (TenantEntity is null)
                return NotFound();

            if (TenantEntity.HomePageId is null)
                return Page(); // or NotFound(), depending on your preferred behavior

            // We only need the slug for redirect; do not Include Sections here
            HomePage = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == TenantEntity.HomePageId.Value &&
                    p.TenantId == TenantEntity.Id &&
                    p.IsActive && !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published); // Published

            if (HomePage is null)
                return Page(); // or NotFound()

            var slug = (HomePage.Slug ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(slug))
                slug = "home"; // fallback; adjust if you want home to be empty slug

            // Redirect to your dynamic tenant page route: @page "/{slug}"
            return Redirect("/" + slug);
        }
    }
}
