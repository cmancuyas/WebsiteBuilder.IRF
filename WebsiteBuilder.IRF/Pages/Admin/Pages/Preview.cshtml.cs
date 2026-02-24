using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public enum PreviewMode
    {
        Draft = 1,
        Published = 2
    }

    [IgnoreAntiforgeryToken] // GET only
    public class PreviewModel : PageModel
    {
        private readonly ITenantContext _tenant;
        private readonly IPageRenderPipeline _pipeline;

        public PreviewModel(ITenantContext tenant, IPageRenderPipeline pipeline)
        {
            _tenant = tenant;
            _pipeline = pipeline;
        }

        public PageRenderContext? Ctx { get; private set; }
        public string? Message { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id, string? rev = "draft", CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            // Only draft preview supported
            Ctx = await _pipeline.BuildDraftForPageIdAsync(id, ct);

            // 🚨 IMPORTANT FIX:
            // Do NOT return NotFound() here.
            // When draft is cleared after publish, just show friendly message.
            if (Ctx is null)
            {
                Message = "Draft preview not available.";
                return Page();
            }

            return Page();
        }
    }

}
