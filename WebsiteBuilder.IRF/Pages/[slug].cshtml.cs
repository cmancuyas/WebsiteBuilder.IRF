using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages
{
    public class _slug_Model : PageModel
    {
        private readonly ITenantContext _tenant;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _cfg;
        private readonly IPageRenderPipeline _pipeline;

        public _slug_Model(
            ITenantContext tenant,
            IWebHostEnvironment env,
            IConfiguration cfg,
            IPageRenderPipeline pipeline)
        {
            _tenant = tenant;
            _env = env;
            _cfg = cfg;
            _pipeline = pipeline;
        }

        public WebsiteBuilder.Models.Page? PageEntity { get; private set; }
        public bool IsPreview { get; private set; }

        public IReadOnlyList<PageRenderContext.RenderSectionDto> RenderSections { get; private set; }
            = Array.Empty<PageRenderContext.RenderSectionDto>();

        public async Task<IActionResult> OnGetAsync(string? slug, CancellationToken ct)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            // --------------------------------------------------
            // Preview gate (single source of truth)
            // --------------------------------------------------
            var previewRequested = IsPreviewRequested();
            IsPreview = previewRequested && UserCanPreview();

            // If preview requested but not allowed, do not reveal existence
            if (previewRequested && !IsPreview)
                return NotFound();

            // --------------------------------------------------
            // Preview response hardening
            // --------------------------------------------------
            if (IsPreview)
            {
                Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
                ApplyNoCacheHeaders();

                ViewData["RobotsNoIndex"] = true;
                ViewData["CanonicalUrl"] = null;
            }

            // --------------------------------------------------
            // Build render context (published-only unless preview enabled)
            // --------------------------------------------------
            var ctx = await _pipeline.BuildForSlugAsync(slug, previewRequested: IsPreview, ct);

            if (ctx is null || ctx.PageEntity is null)
                return NotFound();

            // --------------------------------------------------
            // OutputCache per-page eviction tag support
            // --------------------------------------------------
            HttpContext.Items["ResolvedPageId"] = ctx.PageEntity.Id;

            // Optional diagnostics
            HttpContext.Items["TenantId"] = ctx.PageEntity.TenantId;
            HttpContext.Items["PageId"] = ctx.PageEntity.Id;

            // --------------------------------------------------
            // Canonical redirect (slug history / normalization / home alias)
            // Redirect responses won't be cached (policy stores 200 only)
            // --------------------------------------------------
            if (!IsPreview && !string.IsNullOrWhiteSpace(ctx.RedirectToUrl))
            {
                var qs = Request.QueryString.HasValue ? Request.QueryString.Value : "";
                return RedirectPermanent(ctx.RedirectToUrl + qs);
            }

            // --------------------------------------------------
            // SEO/PERF: ETag support (PUBLIC ONLY)
            // --------------------------------------------------
            if (!IsPreview && ctx.PageEntity.PublishedRevisionId != null)
            {
                var etag = BuildPublicEtag(ctx.PageEntity);
                Response.Headers.ETag = etag;

                if (ctx.PageEntity.PublishedAt.HasValue)
                    Response.Headers.LastModified =
                        ctx.PageEntity.PublishedAt.Value.ToUniversalTime().ToString("R");

                var inm = Request.Headers.IfNoneMatch.ToString();

                if (!string.IsNullOrWhiteSpace(inm) &&
                    inm.Split(',')
                       .Select(x => x.Trim())
                       .Any(x => string.Equals(x, etag, StringComparison.Ordinal)))
                {
                    Response.Headers.ETag = etag;
                    return StatusCode(StatusCodes.Status304NotModified);
                }
            }

            // --------------------------------------------------
            // RENDER CONTEXT -> VIEW
            // --------------------------------------------------
            PageEntity = ctx.PageEntity;
            RenderSections = ctx.RenderSections;

            ViewData["CanonicalUrl"] = IsPreview ? null : ctx.CanonicalUrl;
            ViewData["RobotsNoIndex"] = ctx.RobotsNoIndex;

            ViewData["MetaTitle"] = ctx.MetaTitle;
            ViewData["MetaDescription"] = ctx.MetaDescription;
            ViewData["OgImageUrl"] = ctx.OgImageUrl;

            return Page();
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
            if (_env.IsDevelopment() &&
                _cfg.GetValue<bool>("Preview:AllowAnonymousInDev"))
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

        private string BuildPublicEtag(WebsiteBuilder.Models.Page page)
        {
            // PublishedRevisionId changes on every publish => perfect cache validator.
            // Weak ETag is appropriate for HTML.
            var tenant = page.TenantId.ToString("N");
            var rev = page.PublishedRevisionId?.ToString() ?? "0";
            return $"W/\"t{tenant}-r{rev}\"";
        }
    }
}