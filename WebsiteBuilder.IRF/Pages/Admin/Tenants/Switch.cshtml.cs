using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Tenants
{
    [Authorize(Policy = "AdminArea")]
    public sealed class SwitchModel : PageModel
    {
        private const string TenantCookieName = "wb.admin.tenant";
        private readonly DataContext _db;

        public SwitchModel(DataContext db)
        {
            _db = db;
        }

        public sealed class TenantRow
        {
            public Guid Id { get; set; }
            public string DisplayName { get; set; } = "";
            public string Slug { get; set; } = "";
            public string? PrimaryDomain { get; set; }
            public int PagesCount { get; set; }
            public bool IsOwnedByMe { get; set; }
        }

        public List<TenantRow> Tenants { get; private set; } = new();

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public sealed class InputModel
        {
            [Required]
            public Guid TenantId { get; set; }

            // Optional: where to go after selecting tenant
            public string? ReturnUrl { get; set; }
        }

        private Guid GetUserIdOrThrow()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var guid))
                throw new InvalidOperationException("UserId claim is missing/invalid.");
            return guid;
        }

        private bool IsSuperAdminOrAdmin()
        {
            return User.IsInRole(AppRoles.SuperAdmin) || User.IsInRole(AppRoles.Admin);
        }

        public async Task<IActionResult> OnGetAsync(string? returnUrl = null, CancellationToken ct = default)
        {
            Input.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/Admin" : returnUrl;

            await LoadTenantsAsync(ct);

            // Preselect from cookie if present
            if (Request.Cookies.TryGetValue(TenantCookieName, out var cookieVal) &&
                Guid.TryParse(cookieVal, out var cookieTenantId))
            {
                Input.TenantId = cookieTenantId;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                await LoadTenantsAsync(ct);
                return Page();
            }

            var tenantId = Input.TenantId;

            // Validate selection exists and user can access
            var q = _db.Tenants.AsNoTracking()
                .Where(t => !t.IsDeleted && t.IsActive && t.Id == tenantId);

            if (!IsSuperAdminOrAdmin())
            {
                var userId = GetUserIdOrThrow();
                q = q.Where(t => t.OwnerUserId == userId);
            }

            var exists = await q.AnyAsync(ct);
            if (!exists)
            {
                ModelState.AddModelError("", "Invalid tenant selection.");
                await LoadTenantsAsync(ct);
                return Page();
            }

            // Persist selection (cookie)
            Response.Cookies.Append(
                TenantCookieName,
                tenantId.ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,          // ✅ don't force Secure=true on local HTTP
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                }
            );

            var returnUrl = string.IsNullOrWhiteSpace(Input.ReturnUrl) ? "/Admin" : Input.ReturnUrl;
            if (!Url.IsLocalUrl(returnUrl)) returnUrl = "/Admin";

            return LocalRedirect(returnUrl);
        }

        public IActionResult OnPostClear(string? returnUrl = null)
        {
            Response.Cookies.Delete(TenantCookieName);

            var r = string.IsNullOrWhiteSpace(returnUrl) ? "/Admin" : returnUrl;
            if (!Url.IsLocalUrl(r)) r = "/Admin";

            return LocalRedirect(r);
        }

        private async Task LoadTenantsAsync(CancellationToken ct)
        {
            var userId = GetUserIdOrThrow();
            var isAdmin = IsSuperAdminOrAdmin();

            var tenantsQ = _db.Tenants
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.IsActive);

            if (!isAdmin)
                tenantsQ = tenantsQ.Where(t => t.OwnerUserId == userId);

            Tenants = await tenantsQ
                .OrderBy(t => t.DisplayName)
                .Select(t => new TenantRow
                {
                    Id = t.Id,
                    DisplayName = t.DisplayName,
                    Slug = t.Slug,

                    PrimaryDomain = _db.DomainMappings
                        .AsNoTracking()
                        .Where(d => d.TenantId == t.Id && !d.IsDeleted && d.IsActive)
                        .OrderByDescending(d => d.IsPrimary)
                        .ThenBy(d => d.Id)
                        .Select(d => d.Host)
                        .FirstOrDefault(),

                    PagesCount = _db.Pages
                        .AsNoTracking()
                        .Count(p => p.TenantId == t.Id && !p.IsDeleted && p.IsActive),

                    IsOwnedByMe = t.OwnerUserId == userId
                })
                .ToListAsync(ct);
        }

    }
}
