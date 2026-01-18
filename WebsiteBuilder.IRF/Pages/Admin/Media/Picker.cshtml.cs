using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    [AutoValidateAntiforgeryToken]
    public class PickerModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public PickerModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public sealed class MediaItemVm
        {
            public int Id { get; set; }
            public string FileName { get; set; } = "";
            public string ContentType { get; set; } = "";
            public string AltText { get; set; } = "";
            public string Url { get; set; } = "";
            public string? ThumbUrl { get; set; } // optional if you have thumbnails
        }

        public sealed class SearchRequest
        {
            public string? Term { get; set; }
            public int Skip { get; set; }
            public int Take { get; set; } = 24;

            // Trash toggle
            public bool IncludeDeleted { get; set; } = false;
        }

        public sealed class DeleteRequest { public int Id { get; set; } }
        public sealed class RestoreRequest { public int Id { get; set; } }

        public sealed class BulkDeleteRequest
        {
            public int[] Ids { get; set; } = Array.Empty<int>();
        }

        public sealed class BulkRestoreRequest
        {
            public int[] Ids { get; set; } = Array.Empty<int>();
        }

        public sealed class UpdateAltRequest
        {
            public int Id { get; set; }
            public string? AltText { get; set; }
        }

        public sealed class LookupRequest
        {
            public int[] Ids { get; set; } = Array.Empty<int>();
        }

        public sealed class LookupItemVm
        {
            public int Id { get; set; }
            public string FileName { get; set; } = "";
            public string ContentType { get; set; } = "";
            public string AltText { get; set; } = "";
            public string Url { get; set; } = "";
            public string? ThumbUrl { get; set; }
            public bool IsDeleted { get; set; }
        }

        private Guid GetUserIdOrThrow()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var guid))
                throw new InvalidOperationException("UserId claim is missing/invalid.");
            return guid;
        }

        private bool CanSeeAllTenantMedia()
        {
            return User.IsInRole(AppRoles.SuperAdmin) || User.IsInRole(AppRoles.Admin);
        }

        public async Task<IActionResult> OnPostLookupAsync([FromBody] LookupRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            var ids = (request?.Ids ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new JsonResult(new { ok = true, items = Array.Empty<LookupItemVm>() });

            var q = _db.MediaAssets
                .AsNoTracking()
                .Where(m => m.TenantId == _tenant.TenantId)
                .Where(m => ids.Contains(m.Id));

            if (!CanSeeAllTenantMedia())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == userId);
            }

            var items = await q
                .Select(m => new LookupItemVm
                {
                    Id = m.Id,
                    FileName = m.FileName,
                    ContentType = m.ContentType,
                    AltText = m.AltText ?? "",
                    Url = m.StorageKey,
                    ThumbUrl = m.ThumbStorageKey,
                    IsDeleted = m.IsDeleted
                })
                .ToListAsync(ct);

            return new JsonResult(new { ok = true, items });
        }

        public IActionResult OnGet()
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            return Page();
        }

        // POST /Admin/Media/Picker?handler=Search
        public async Task<IActionResult> OnPostSearchAsync([FromBody] SearchRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            request ??= new SearchRequest();

            var term = (request.Term ?? "").Trim();
            var take = request.Take <= 0 ? 24 : Math.Min(request.Take, 60);
            var skip = Math.Max(0, request.Skip);
            var includeDeleted = request.IncludeDeleted;

            var q = _db.MediaAssets
                .AsNoTracking()
                .Where(m => m.TenantId == _tenant.TenantId)
                .Where(m => m.IsActive)
                .Where(m => m.ContentType.StartsWith("image/"));

            if (!CanSeeAllTenantMedia())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == userId);
            }

            q = includeDeleted ? q.Where(m => m.IsDeleted) : q.Where(m => !m.IsDeleted);

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(m => m.FileName.Contains(term) || (m.AltText ?? "").Contains(term));
            }

            var total = await q.CountAsync(ct);

            var items = await q
                .OrderByDescending(m => m.Id)
                .Skip(skip)
                .Take(take)
                .Select(m => new MediaItemVm
                {
                    Id = m.Id,
                    FileName = m.FileName,
                    ContentType = m.ContentType,
                    AltText = m.AltText ?? "",
                    Url = m.StorageKey,
                    ThumbUrl = m.ThumbStorageKey
                })
                .ToListAsync(ct);

            return new JsonResult(new
            {
                ok = true,
                items,
                total,
                hasMore = skip + items.Count < total
            });
        }

        // POST /Admin/Media/Picker?handler=Delete
        public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            var q = _db.MediaAssets.Where(m =>
                m.TenantId == _tenant.TenantId &&
                m.Id == request.Id &&
                !m.IsDeleted);

            Guid? actorUserId = null;
            if (!CanSeeAllTenantMedia())
            {
                actorUserId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == actorUserId.Value);
            }
            else
            {
                actorUserId = GetUserIdOrThrow();
            }

            var asset = await q.FirstOrDefaultAsync(ct);

            if (asset == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            var now = DateTime.UtcNow;

            asset.IsDeleted = true;
            asset.DeletedAt = now;
            asset.DeletedBy = actorUserId;
            asset.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        // POST /Admin/Media/Picker?handler=Restore
        public async Task<IActionResult> OnPostRestoreAsync([FromBody] RestoreRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            var q = _db.MediaAssets.Where(m =>
                m.TenantId == _tenant.TenantId &&
                m.Id == request.Id &&
                m.IsDeleted);

            if (!CanSeeAllTenantMedia())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == userId);
            }

            var asset = await q.FirstOrDefaultAsync(ct);

            if (asset == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            var now = DateTime.UtcNow;

            asset.IsDeleted = false;
            asset.DeletedAt = null;
            asset.DeletedBy = null;
            asset.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        // POST /Admin/Media/Picker?handler=BulkDelete
        public async Task<IActionResult> OnPostBulkDeleteAsync([FromBody] BulkDeleteRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            var ids = (request?.Ids ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new JsonResult(new { ok = false, message = "No items selected." });

            var q = _db.MediaAssets.Where(m =>
                m.TenantId == _tenant.TenantId &&
                ids.Contains(m.Id) &&
                !m.IsDeleted);

            Guid? actorUserId = null;
            if (!CanSeeAllTenantMedia())
            {
                actorUserId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == actorUserId.Value);
            }
            else
            {
                actorUserId = GetUserIdOrThrow();
            }

            var assets = await q.ToListAsync(ct);

            if (assets.Count == 0)
                return new JsonResult(new { ok = false, message = "No matching media found." });

            var now = DateTime.UtcNow;

            foreach (var a in assets)
            {
                a.IsDeleted = true;
                a.DeletedAt = now;
                a.DeletedBy = actorUserId;
                a.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true, deletedIds = assets.Select(x => x.Id).ToArray() });
        }

        // POST /Admin/Media/Picker?handler=BulkRestore
        public async Task<IActionResult> OnPostBulkRestoreAsync([FromBody] BulkRestoreRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            var ids = (request?.Ids ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new JsonResult(new { ok = false, message = "No items selected." });

            var q = _db.MediaAssets.Where(m =>
                m.TenantId == _tenant.TenantId &&
                ids.Contains(m.Id) &&
                m.IsDeleted);

            if (!CanSeeAllTenantMedia())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == userId);
            }

            var assets = await q.ToListAsync(ct);

            if (assets.Count == 0)
                return new JsonResult(new { ok = false, message = "No matching media found." });

            var now = DateTime.UtcNow;

            foreach (var a in assets)
            {
                a.IsDeleted = false;
                a.DeletedAt = null;
                a.DeletedBy = null;
                a.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true, restoredIds = assets.Select(x => x.Id).ToArray() });
        }

        // POST /Admin/Media/Picker?handler=UpdateAlt
        public async Task<IActionResult> OnPostUpdateAltAsync([FromBody] UpdateAltRequest request, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            var q = _db.MediaAssets.Where(m =>
                m.TenantId == _tenant.TenantId &&
                m.Id == request.Id &&
                !m.IsDeleted);

            if (!CanSeeAllTenantMedia())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(m => m.OwnerUserId == userId);
            }

            var asset = await q.FirstOrDefaultAsync(ct);

            if (asset == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            asset.AltText = (request.AltText ?? "").Trim();
            asset.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }
    }
}
