using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    [Authorize]
    public class ListModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public ListModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public bool IsTenantResolved => _tenant.IsResolved;

        public List<MediaAsset> Media { get; private set; } = new();
        public int DeletedCount { get; private set; }

        public async Task OnGetAsync()
        {
            if (!_tenant.IsResolved)
                return;

            Media = await _db.MediaAssets
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            DeletedCount = await _db.MediaAssets
                .AsNoTracking()
                .CountAsync(x => x.TenantId == _tenant.TenantId && x.IsDeleted);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { success = false, error = "Tenant not resolved." }) { StatusCode = 400 };

            if (id <= 0)
                return new JsonResult(new { success = false, error = "Invalid media id." }) { StatusCode = 400 };

            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.TenantId == _tenant.TenantId);

            if (asset == null)
                return new JsonResult(new { success = false, error = "Media not found." }) { StatusCode = 404 };

            if (asset.IsDeleted)
                return new JsonResult(new { success = true }); // idempotent

            // Soft delete
            asset.IsDeleted = true;
            asset.DeletedAt = DateTime.UtcNow;
            asset.DeletedBy = GetUserIdOrNull();

            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedBy = GetUserIdOrNull();

            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }

        private Guid? GetUserIdOrNull()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(raw, out var userId))
                return userId;

            return null;
        }
    }
}
