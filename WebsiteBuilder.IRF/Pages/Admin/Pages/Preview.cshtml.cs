using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
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

        public async Task<IActionResult> OnGetAsync(int id, string? rev = "draft", CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // For now we only support draft preview in iframe
            // (rev param can be extended later to published snapshot)
            Ctx = await _pipeline.BuildDraftForPageIdAsync(id, ct);

            if (Ctx is null)
                return NotFound("Draft preview not available.");

            // Ensure no indexing/caching
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return Page();
        }
    }
}
