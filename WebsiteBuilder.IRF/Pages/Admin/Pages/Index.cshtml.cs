using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
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

        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? StatusId { get; set; }

        public List<PageListItemVm> Items { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_tenant.IsResolved) return NotFound();

            var query = _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.IsActive && !p.IsDeleted);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                query = query.Where(p => p.Title.Contains(q) || p.Slug.Contains(q));
            }

            if (StatusId.HasValue)
            {
                query = query.Where(p => p.PageStatusId == StatusId.Value);
            }

            Items = await query
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                .Select(p => new PageListItemVm
                {
                    Id = p.Id,
                    Title = p.Title,
                    Slug = p.Slug,
                    PageStatusId = p.PageStatusId,
                    UpdatedAt = p.UpdatedAt ?? p.CreatedAt
                })
                .ToListAsync();

            return Page();
        }

        public sealed class PageListItemVm
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Slug { get; set; } = string.Empty;
            public int PageStatusId { get; set; }
            public DateTime UpdatedAt { get; set; }
        }
    }
}
