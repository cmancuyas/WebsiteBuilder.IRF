using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
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
        }

        public sealed class SearchRequest
        {
            public string? Term { get; set; }
            public int Skip { get; set; }
            public int Take { get; set; } = 24;
        }

        public sealed class DeleteRequest
        {
            public int Id { get; set; }
        }

        public IActionResult OnGet()
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            return Page();
        }

        // POST /Admin/Media/Picker?handler=Search
        public async Task<IActionResult> OnPostSearchAsync(
            [FromBody] SearchRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            request ??= new SearchRequest();
            var term = (request.Term ?? "").Trim();
            var take = request.Take <= 0 ? 24 : Math.Min(request.Take, 60);
            var skip = Math.Max(0, request.Skip);

            var q = _db.MediaAssets
                .AsNoTracking()
                .Where(m => !m.IsDeleted && m.IsActive)
                .Where(m => m.ContentType.StartsWith("image/"));

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(m =>
                    m.FileName.Contains(term) ||
                    m.AltText!.Contains(term));
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
                    AltText = m.AltText!,
                    Url = m.StorageKey
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
        public async Task<IActionResult> OnPostDeleteAsync(
            [FromBody] DeleteRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.Id <= 0)
                return new JsonResult(new { ok = false, message = "Invalid request." });

            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(m => m.Id == request.Id && !m.IsDeleted, ct);

            if (asset == null)
                return new JsonResult(new { ok = false, message = "Media not found." });

            asset.IsDeleted = true;
            asset.DeletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }
    }
}
