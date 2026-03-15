using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Rendering
{

    public sealed class TenantUrlResolver : ITenantUrlResolver
    {
        private readonly IHttpContextAccessor _http;
        private readonly ITenantContext _tenant;
        private readonly IConfiguration _cfg;

        public TenantUrlResolver(IHttpContextAccessor http, ITenantContext tenant, IConfiguration cfg)
        {
            _http = http;
            _tenant = tenant;
            _cfg = cfg;
        }

        public Task<(string Scheme, string Host)> GetCanonicalAsync(CancellationToken ct = default)
        {
            var ctx = _http.HttpContext;

            // ---- Scheme (proxy-safe) ----
            var scheme = ctx?.Request.Scheme ?? "https";

            // If you're behind a proxy/CDN and ForwardedHeaders is enabled, Request.Scheme is already corrected.
            // If not, optionally honor X-Forwarded-Proto (controlled via config).
            if (_cfg.GetValue("SaaS:HonorForwardedProto", true) && ctx is not null)
            {
                var fproto = ctx.Request.Headers["X-Forwarded-Proto"].ToString();
                if (!string.IsNullOrWhiteSpace(fproto))
                {
                    // could be "https, http" in some setups—take first
                    scheme = fproto.Split(',')[0].Trim();
                }
            }

            // Optional: force https canonical in production
            if (_cfg.GetValue("SaaS:ForceHttpsCanonical", true))
                scheme = "https";

            // ---- Host (tenant-safe) ----
            // Never trust forwarded host for canonicalization.
            // Canonical host MUST be tenant primary host (fallback to request host if primary missing).
            var host = !string.IsNullOrWhiteSpace(_tenant.PrimaryHost)
                ? _tenant.PrimaryHost
                : _tenant.Host;

            host = HostNormalizer.Normalize(host);

            return Task.FromResult((scheme, host));
        }
    }
}