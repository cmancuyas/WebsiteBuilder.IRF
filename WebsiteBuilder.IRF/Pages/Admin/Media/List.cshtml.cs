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

        // New: track current view for UI
        public bool IsTrashView { get; private set; }

        public async Task OnGetAsync(string? view = null)
        {
            if (!_tenant.IsResolved)
                return;

            IsTrashView = string.Equals(view, "deleted", StringComparison.OrdinalIgnoreCase);

            Media = await _db.MediaAssets
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    x.IsDeleted == IsTrashView)
                .OrderByDescending(x => IsTrashView ? x.DeletedAt : x.CreatedAt)
                .ToListAsync();

            DeletedCount = await _db.MediaAssets
                .AsNoTracking()
                .CountAsync(x => x.TenantId == _tenant.TenantId && x.IsDeleted);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            if (!_tenant.IsResolved)
                return BadRequestJson("Tenant not resolved.");

            if (id <= 0)
                return BadRequestJson("Invalid media id.");

            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);

            if (asset == null)
                return NotFoundJson("Media not found.");

            if (asset.IsDeleted)
                return new JsonResult(new { success = true }); // idempotent

            asset.IsDeleted = true;
            asset.DeletedAt = DateTime.UtcNow;
            asset.DeletedBy = GetUserIdOrNull();

            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedBy = GetUserIdOrNull();

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        // New: single restore
        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            if (!_tenant.IsResolved)
                return BadRequestJson("Tenant not resolved.");

            if (id <= 0)
                return BadRequestJson("Invalid media id.");

            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);

            if (asset == null)
                return NotFoundJson("Media not found.");

            if (!asset.IsDeleted)
                return new JsonResult(new { success = true }); // idempotent

            asset.IsDeleted = false;
            asset.DeletedAt = null;
            asset.DeletedBy = null;

            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedBy = GetUserIdOrNull();

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        // New: bulk delete
        public async Task<IActionResult> OnPostDeleteManyAsync([FromForm] int[] ids)
        {
            if (!_tenant.IsResolved)
                return BadRequestJson("Tenant not resolved.");

            if (ids == null || ids.Length == 0)
                return BadRequestJson("No media selected.");

            var now = DateTime.UtcNow;
            var userId = GetUserIdOrNull();

            var assets = await _db.MediaAssets
                .Where(x => x.TenantId == _tenant.TenantId && ids.Contains(x.Id))
                .ToListAsync();

            if (assets.Count == 0)
                return NotFoundJson("No matching media found.");

            foreach (var a in assets)
            {
                if (a.IsDeleted) continue;

                a.IsDeleted = true;
                a.DeletedAt = now;
                a.DeletedBy = userId;

                a.UpdatedAt = now;
                a.UpdatedBy = userId;
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true, count = assets.Count });
        }

        // New: bulk restore
        public async Task<IActionResult> OnPostRestoreManyAsync([FromForm] int[] ids)
        {
            if (!_tenant.IsResolved)
                return BadRequestJson("Tenant not resolved.");

            if (ids == null || ids.Length == 0)
                return BadRequestJson("No media selected.");

            var now = DateTime.UtcNow;
            var userId = GetUserIdOrNull();

            var assets = await _db.MediaAssets
                .Where(x => x.TenantId == _tenant.TenantId && ids.Contains(x.Id))
                .ToListAsync();

            if (assets.Count == 0)
                return NotFoundJson("No matching media found.");

            foreach (var a in assets)
            {
                if (!a.IsDeleted) continue;

                a.IsDeleted = false;
                a.DeletedAt = null;
                a.DeletedBy = null;

                a.UpdatedAt = now;
                a.UpdatedBy = userId;
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true, count = assets.Count });
        }

        private Guid? GetUserIdOrNull()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(raw, out var userId))
                return userId;

            return null;
        }

        private IActionResult BadRequestJson(string error)
            => new JsonResult(new { success = false, error }) { StatusCode = 400 };

        private IActionResult NotFoundJson(string error)
            => new JsonResult(new { success = false, error }) { StatusCode = 404 };
    }
}
