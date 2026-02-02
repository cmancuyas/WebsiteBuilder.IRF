using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ITenantNavigationService _nav;

        public IndexModel(
            DataContext db,
            ITenantContext tenant,
            ITenantNavigationService nav)
        {
            _db = db;
            _tenant = tenant;
            _nav = nav;
        }

        // =========================
        // Query params
        // =========================
        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? StatusId { get; set; }

        // =========================
        // Page list
        // =========================
        public List<PageListItemVm> Items { get; private set; } = new();

        // =========================
        // Home page picker
        // =========================
        [BindProperty]
        public int? SelectedHomePageId { get; set; }

        public int? CurrentHomePageId { get; private set; }

        public List<SelectListItem> PublishedPagesSelect { get; private set; } = new();

        // =========================
        // Helpers
        // =========================
        private Guid GetUserIdOrEmpty()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userId, out var g) ? g : Guid.Empty;
        }

        private bool CanSeeAllTenantPages()
        {
            return User.IsInRole(AppRoles.SuperAdmin) ||
                   User.IsInRole(AppRoles.Admin);
        }

        private IQueryable<WebsiteBuilder.Models.Page> PagesForCurrentUser()
        {
            var q = _db.Pages
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted);

            if (CanSeeAllTenantPages())
                return q;

            var userId = GetUserIdOrEmpty();
            return q.Where(p => p.OwnerUserId == userId);
        }

        // =========================
        // Load Home Page Picker
        // =========================
        private async Task LoadHomePagePickerAsync(CancellationToken ct)
        {
            PublishedPagesSelect.Clear();

            if (!CanSeeAllTenantPages())
                return;

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.Id == _tenant.TenantId &&
                    t.IsActive &&
                    !t.IsDeleted, ct);

            CurrentHomePageId = tenant?.HomePageId;
            SelectedHomePageId ??= tenant?.HomePageId;

            var publishedPages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = $"{p.Title} ({p.Slug})"
                })
                .ToListAsync(ct);

            PublishedPagesSelect = publishedPages;
        }

        // =========================
        // GET
        // =========================
        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            await LoadHomePagePickerAsync(ct);

            var query = PagesForCurrentUser().AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                query = query.Where(p =>
                    p.Title.Contains(q) ||
                    p.Slug.Contains(q));
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
                .ToListAsync(ct);

            return Page();
        }

        // =========================
        // POST: Set Home Page
        // =========================
        public async Task<IActionResult> OnPostSetHomePageAsync(CancellationToken ct)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            if (!SelectedHomePageId.HasValue)
            {
                ModelState.AddModelError(string.Empty, "Please select a published page.");
                await LoadHomePagePickerAsync(ct);
                return await OnGetAsync(ct);
            }

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == SelectedHomePageId.Value &&
                    p.TenantId == _tenant.TenantId &&
                    p.PageStatusId == PageStatusIds.Published &&
                    p.IsActive &&
                    !p.IsDeleted, ct);

            if (page == null)
            {
                ModelState.AddModelError(string.Empty, "Selected page is not published.");
                await LoadHomePagePickerAsync(ct);
                return await OnGetAsync(ct);
            }

            var tenant = await _db.Tenants
                .FirstOrDefaultAsync(t =>
                    t.Id == _tenant.TenantId &&
                    t.IsActive &&
                    !t.IsDeleted, ct);

            if (tenant == null)
                return NotFound();

            tenant.HomePageId = page.Id;
            tenant.UpdatedAt = DateTime.UtcNow;
            tenant.UpdatedBy = GetUserIdOrEmpty();

            await _db.SaveChangesAsync(ct);

            _nav.Invalidate();

            TempData["Success"] = $"'{page.Title}' is now the Home page.";
            return RedirectToPage();
        }

        // =========================
        // VM
        // =========================
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
