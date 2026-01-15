using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public IndexModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public int ActiveCount { get; private set; }
        public int DeletedCount { get; private set; }

        public async Task OnGetAsync()
        {
            if (!_tenant.IsResolved)
            {
                ActiveCount = 0;
                DeletedCount = 0;
                return;
            }

            // If your TenantId type differs (int vs Guid), tell me and I will adjust.
            ActiveCount = await _db.MediaAssets
                .AsNoTracking()
                .CountAsync(x => x.TenantId == _tenant.TenantId && !x.IsDeleted);

            DeletedCount = await _db.MediaAssets
                .AsNoTracking()
                .CountAsync(x => x.TenantId == _tenant.TenantId && x.IsDeleted);
        }
    }
}
