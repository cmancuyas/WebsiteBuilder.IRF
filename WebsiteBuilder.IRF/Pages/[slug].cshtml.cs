using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

        public Models.Page? Page { get; private set; }

        public async Task<IActionResult> OnGetAsync(string slug)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            Page = await _db.Pages
                .AsNoTracking()
                .Include(p => p.Sections)
                .FirstOrDefaultAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.Slug == slug &&
                    p.PageStatusId == PageStatusIds.Published &&
                    p.IsActive &&
                    !p.IsDeleted);

            if (Page == null)
                return NotFound();

            Page.Sections = Page.Sections
                .Where(s => s.TenantId == _tenant.TenantId && s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ToList();

            return Page();
        }
    }
}
