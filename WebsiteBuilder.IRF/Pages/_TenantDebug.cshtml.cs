using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages
{
    public class _TenantDebugModel : PageModel
    {
        private readonly ITenantContext _tenantContext;

        public _TenantDebugModel(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public bool IsResolved { get; private set; }

        public string RequestHost { get; private set; } = string.Empty;
        public string RequestPath { get; private set; } = string.Empty;

        public string TenantId { get; private set; } = string.Empty;
        public string TenantSlug { get; private set; } = string.Empty;
        public string TenantHost { get; private set; } = string.Empty;

        public string ItemsTenantId { get; private set; } = string.Empty;
        public string ItemsTenantSlug { get; private set; } = string.Empty;

        public void OnGet()
        {
            RequestHost = HttpContext?.Request?.Host.Value ?? string.Empty;
            RequestPath = HttpContext?.Request?.Path.Value ?? string.Empty;

            IsResolved = _tenantContext.IsResolved;

            TenantId = _tenantContext.TenantId == Guid.Empty
                ? "(empty)"
                : _tenantContext.TenantId.ToString();

            TenantSlug = string.IsNullOrWhiteSpace(_tenantContext.Slug)
                ? "(empty)"
                : _tenantContext.Slug;

            TenantHost = string.IsNullOrWhiteSpace(_tenantContext.Host)
                ? "(empty)"
                : _tenantContext.Host;

            // If you stored these in middleware (optional)
            ItemsTenantId = HttpContext!.Items.TryGetValue("TenantId", out var idVal) && idVal != null
                ? idVal.ToString()!
                : "(not set)";

            ItemsTenantSlug = HttpContext.Items.TryGetValue("TenantSlug", out var slugVal) && slugVal != null
                ? slugVal.ToString()!
                : "(not set)";
        }
    }
}
