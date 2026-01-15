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
    public class TrashModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public TrashModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public List<MediaAsset> DeletedMedia { get; private set; } = new();

        public async Task OnGetAsync()
        {
            // Safety: tenant must be resolved for admin media operations
            if (!_tenant.IsResolved)
            {
                DeletedMedia = new List<MediaAsset>();
                return;
            }

            DeletedMedia = await _db.MediaAssets
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    x.IsDeleted)
                .OrderByDescending(x => x.DeletedAt)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { success = false, error = "Tenant not resolved." }) { StatusCode = 400 };

            if (id <= 0)
                return new JsonResult(new { success = false, error = "Invalid media id." }) { StatusCode = 400 };

            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.TenantId == _tenant.TenantId && x.IsDeleted);

            if (asset == null)
                return new JsonResult(new { success = false, error = "Media not found." }) { StatusCode = 404 };

            if (!asset.IsDeleted)
                return new JsonResult(new { success = true }); // idempotent

            // Restore
            asset.IsDeleted = false;
            asset.DeletedAt = null;
            asset.DeletedBy = null;

            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedBy = GetUserIdOrNull();

            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }

        private Guid? GetUserIdOrNull()
        {
            // Works even if Identity is not fully implemented, but claim exists.
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(raw, out var userId))
                return userId;

            return null;
        }
    }
}
