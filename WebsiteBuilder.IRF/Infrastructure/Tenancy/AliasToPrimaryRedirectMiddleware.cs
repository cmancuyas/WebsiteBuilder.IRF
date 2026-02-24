using Microsoft.AspNetCore.Http;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class AliasToPrimaryRedirectMiddleware
    {
        private readonly RequestDelegate _next;

        public AliasToPrimaryRedirectMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext ctx, ITenantContext tenant)
        {
            // Only redirect safe idempotent requests
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
            {
                await _next(ctx);
                return;
            }

            // Skip preview always
            if (ctx.Request.Query.ContainsKey("preview"))
            {
                await _next(ctx);
                return;
            }

            // Defensive exclusions (avoid interfering with admin/api)
            var path = ctx.Request.Path.Value ?? "";
            if (path.StartsWith("/Admin", System.StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            if (!tenant.IsResolved)
            {
                await _next(ctx);
                return;
            }

            var reqHost = HostNormalizer.Normalize(ctx.Request.Host.Host);
            var primary = HostNormalizer.Normalize(tenant.PrimaryHost);

            if (string.IsNullOrWhiteSpace(primary) ||
                string.Equals(reqHost, primary, System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            // 301 to primary, preserve path + query
            var scheme = ctx.Request.Scheme;

            // If you canonicalize to https (recommended), force it here too
            // (prevents redirecting to http://primary in some proxy setups)
            scheme = "https";

            var fullPath = ctx.Request.PathBase.Add(ctx.Request.Path).ToString();
            var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty;

            var target = $"{scheme}://{primary}{fullPath}{query}";

            // prevent accidental caching of redirects by intermediaries
            ctx.Response.Headers.CacheControl = "no-store";

            ctx.Response.Redirect(target, permanent: true);
        }
    }
}