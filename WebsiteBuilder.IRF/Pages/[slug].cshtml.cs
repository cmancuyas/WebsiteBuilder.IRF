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

            var previewRequested = IsPreviewRequested();
            IsPreview = previewRequested && UserCanPreview();

            if (previewRequested && !IsPreview)
                return NotFound();

            if (IsPreview)
            {
                Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
                ApplyNoCacheHeaders();

                ViewData["RobotsNoIndex"] = true;
                ViewData["CanonicalUrl"] = null;
            }

            var ctx = await _pipeline.BuildForSlugAsync(slug, previewRequested: IsPreview, ct);
            if (ctx is null)
                return NotFound();

            // --------------------------------------------------
            // PUBLIC-ONLY REDIRECT LOGIC (SEO CONSOLIDATION)
            // --------------------------------------------------
            if (!IsPreview)
            {
                // 1️⃣ Pipeline-driven redirect (e.g., slug normalization / history)
                if (!string.IsNullOrWhiteSpace(ctx.RedirectToUrl))
                {
                    return RedirectPermanent(ctx.RedirectToUrl);
                }

                // 2️⃣ Canonical host + path enforcement
                if (!string.IsNullOrWhiteSpace(ctx.CanonicalUrl))
                {
                    var canonical = new Uri(ctx.CanonicalUrl, UriKind.Absolute);

                    // Current absolute URL (without querystring)
                    var currentRaw = $"{Request.Scheme}://{Request.Host.Host}{Request.PathBase}{Request.Path}";

                    if (Uri.TryCreate(currentRaw, UriKind.Absolute, out var currentUri))
                    {
                        var currentPath = currentUri.AbsolutePath.TrimEnd('/');
                        var canonicalPath = canonical.AbsolutePath.TrimEnd('/');

                        if (string.IsNullOrEmpty(currentPath)) currentPath = "/";
                        if (string.IsNullOrEmpty(canonicalPath)) canonicalPath = "/";

                        var sameHost = string.Equals(currentUri.Host, canonical.Host, StringComparison.OrdinalIgnoreCase);
                        var samePath = string.Equals(currentPath, canonicalPath, StringComparison.OrdinalIgnoreCase);

                        if (!sameHost || !samePath)
                        {
                            return RedirectPermanent(ctx.CanonicalUrl);
                        }
                    }
                }
            }

            // --------------------------------------------------
            // RENDER CONTEXT
            // --------------------------------------------------
            PageEntity = ctx.PageEntity;
            RenderSections = ctx.RenderSections;

            ViewData["CanonicalUrl"] = IsPreview ? null : ctx.CanonicalUrl;
            ViewData["RobotsNoIndex"] = ctx.RobotsNoIndex;

            ViewData["MetaTitle"] = ctx.MetaTitle;
            ViewData["MetaDescription"] = ctx.MetaDescription;
            ViewData["OgImageUrl"] = ctx.OgImageUrl;

            if (!IsPreview && PageEntity != null)
            {
                HttpContext.Response.Headers.Append("Cache-Tag", $"tenant:{PageEntity.TenantId}");
                HttpContext.Response.Headers.Append("Cache-Tag", $"page:{PageEntity.Id}");
                HttpContext.Items["PageId"] = ctx.PageEntity.Id;
                HttpContext.Items["TenantId"] = ctx.PageEntity.TenantId;
            }


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
    }
}
