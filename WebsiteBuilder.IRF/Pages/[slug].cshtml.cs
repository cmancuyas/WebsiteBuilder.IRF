using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages
{
    public class _slug_Model : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _cfg;
        private readonly ITenantUrlResolver _url;

        public _slug_Model(
            DataContext db,
            ITenantContext tenant,
            IWebHostEnvironment env,
            IConfiguration cfg,
            ITenantUrlResolver url)
        {
            _db = db;
            _tenant = tenant;
            _env = env;
            _cfg = cfg;
            _url = url;
        }

        public WebsiteBuilder.Models.Page? PageEntity { get; private set; }
        public bool IsPreview { get; private set; }
        public List<RenderSectionDto> RenderSections { get; private set; } = new();

        public sealed class RenderSectionDto
        {
            public int SectionTypeId { get; init; }
            public string? SectionTypeName { get; init; }
            public int SortOrder { get; init; }
            public string? SettingsJson { get; init; }
        }

        // Route param: /{slug?}
        public async Task<IActionResult> OnGetAsync(string? slug)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            var previewRequested = IsPreviewRequested();
            IsPreview = previewRequested && UserCanPreview();

            // Never allow unauthorized preview (hide existence)
            if (previewRequested && !IsPreview)
                return NotFound();

            if (IsPreview)
            {
                Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
                ApplyNoCacheHeaders();

                // Layout flags
                ViewData["RobotsNoIndex"] = true;
                ViewData["CanonicalUrl"] = null;
            }

            var normalizedSlug = NormalizeSlug(slug);
            if (string.IsNullOrWhiteSpace(normalizedSlug))
                normalizedSlug = "home";

            // Canonicalize home: redirect /home -> /
            if (normalizedSlug == "home" &&
                HttpContext.Request.Path.Equals("/home", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect("/");
            }

            // Find page
            var pageQuery = _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.Slug == normalizedSlug);

            if (!IsPreview)
                pageQuery = pageQuery.Where(p => p.PageStatusId == PageStatusIds.Published);
            else
                pageQuery = pageQuery.Where(p => p.PageStatusId != PageStatusIds.Archived);

            PageEntity = await pageQuery.FirstOrDefaultAsync(HttpContext.RequestAborted);

            if (PageEntity is null)
                return NotFound();

            // ============================
            // PREVIEW MODE
            // ============================
            if (IsPreview)
            {
                if (PageEntity.DraftRevisionId == null)
                    return NotFound();

                RenderSections = await LoadSectionsAsync(PageEntity.DraftRevisionId.Value);
                return Page();
            }

            // ============================
            // PUBLIC / PUBLISHED MODE
            // ============================
            if (PageEntity.PublishedRevisionId == null)
                return NotFound();

            // Load published revision (source of canonical slug)
            var revision = await _db.PageRevisions
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == PageEntity.PublishedRevisionId.Value &&
                    r.TenantId == _tenant.TenantId,
                    HttpContext.RequestAborted);

            if (revision == null)
                return NotFound();

            RenderSections = await LoadSectionsAsync(PageEntity.PublishedRevisionId.Value);

            // ✅ Canonical URL (published snapshot only)
            var (scheme, host) = await _url.GetCanonicalAsync(HttpContext.RequestAborted);

            // Published snapshot slug is the canonical source of truth
            var publishedSlug = NormalizeSlug(revision.Slug);

            // Map "home" to "/"
            var canonicalPath = publishedSlug == "home"
                ? "/"
                : "/" + publishedSlug;

            ViewData["CanonicalUrl"] = $"{scheme}://{host}{canonicalPath}";
            ViewData["RobotsNoIndex"] = false;



            return Page();
        }

        private async Task<List<RenderSectionDto>> LoadSectionsAsync(int pageRevisionId)
        {
            var sections = await _db.PageRevisionSections
                .AsNoTracking()
                .Include(s => s.SectionType)
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == pageRevisionId &&
                    s.IsActive &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync(HttpContext.RequestAborted);

            return sections.Select(s => new RenderSectionDto
            {
                SectionTypeId = s.SectionTypeId,
                SectionTypeName = s.SectionType?.Name,
                SortOrder = s.SortOrder,
                SettingsJson = s.SettingsJson
            }).ToList();
        }

        private bool IsPreviewRequested()
        {
            var val = Request.Query["preview"].ToString();
            if (string.IsNullOrWhiteSpace(val)) return false;

            return val.Equals("1", StringComparison.OrdinalIgnoreCase)
                || val.Equals("true", StringComparison.OrdinalIgnoreCase)
                || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private bool UserCanPreview()
        {
            if (_env.IsDevelopment() && _cfg.GetValue<bool>("Preview:AllowAnonymousInDev"))
                return true;

            if (User.Identity?.IsAuthenticated != true)
                return false;

            if (User.IsInRole("Admin"))
                return true;

            if (User.HasClaim("Permission", "Pages.Preview"))
                return true;

            return false;
        }

        private void ApplyNoCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }

        private static string NormalizeSlug(string? slug)
        {
            slug ??= string.Empty;

            var s = slug.Trim().Trim('/').ToLowerInvariant();
            s = Regex.Replace(s, @"[\s_]+", "-");
            s = Regex.Replace(s, @"[^a-z0-9\-]+", string.Empty);
            s = Regex.Replace(s, @"-+", "-");

            return s.Trim('-');
        }
    }
}
