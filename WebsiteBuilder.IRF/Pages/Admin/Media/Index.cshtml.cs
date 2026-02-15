using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    [AllowAnonymous]
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
        public sealed class DeleteMediaRequest
        {
            public int Id { get; set; }
        }

        public sealed class RestoreMediaRequest
        {
            public int Id { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteMediaRequest req, CancellationToken ct)
        {
            if (req.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid id." });

            var entity = await _db.MediaAssets
                .FirstOrDefaultAsync(x => x.Id == req.Id, ct);

            if (entity == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            if (!entity.IsDeleted)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                Guid.TryParse(userId, out var userGuid);

                entity.IsDeleted = true;
                entity.DeletedAt = DateTime.UtcNow;
                entity.DeletedBy = userGuid == Guid.Empty ? (Guid?)null : userGuid;

                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = userGuid == Guid.Empty ? (Guid?)null : userGuid;

                await _db.SaveChangesAsync(ct);
            }

            return new JsonResult(new { ok = true, id = entity.Id });
        }

        public async Task<IActionResult> OnPostRestoreAsync([FromBody] RestoreMediaRequest req, CancellationToken ct)
        {
            if (req.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid id." });

            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            var entity = await _db.MediaAssets
                .FirstOrDefaultAsync(x =>
                    x.Id == req.Id &&
                    x.TenantId == _tenant.TenantId,
                    ct);


            if (entity == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            if (entity.IsDeleted)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                Guid.TryParse(userId, out var userGuid);

                entity.IsDeleted = false;
                entity.DeletedAt = null;
                entity.DeletedBy = null;

                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = userGuid == Guid.Empty ? (Guid?)null : userGuid;

                await _db.SaveChangesAsync(ct);
            }

            return new JsonResult(new { ok = true, id = entity.Id });
        }

    }
}
