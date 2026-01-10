using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.Repository.IRepository;

namespace WebsiteBuilder.IRF.Infrastructure.Middleware
{
    public sealed class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantResolutionMiddleware> _logger;
        private readonly IConfiguration _config;

        public TenantResolutionMiddleware(
            RequestDelegate next,
            ILogger<TenantResolutionMiddleware> logger,
            IConfiguration config)
        {
            _next = next;
            _logger = logger;
            _config = config;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantResolver tenantResolver,
            ITenantContext tenantContext)
        {
            // 1) Bypass tenant resolution for platform/system routes
            if (ShouldBypassTenantResolution(context))
            {
                await _next(context);
                return;
            }

            var host = context.Request.Host.Host?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(host))
            {
                await _next(context);
                return;
            }

            // DEV/LOCAL bypass (so you can access /Identity, /app, etc.)
            if (host == "localhost" || host == "127.0.0.1")
            {
                await _next(context);
                return;
            }

            // 2) Determine platform slug if this is a platform subdomain
            string? slug = null;

            var platformDomain = _config["SaaS:PlatformDomain"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(platformDomain))
            {
                if (host.Equals(platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    // Root marketing/app domain – no tenant for public site rendering
                    await _next(context);
                    return;
                }

                if (host.EndsWith("." + platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var sub = host[..^(platformDomain.Length + 1)];
                    slug = sub.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                    // Reserved subdomains (platform-owned)
                    var reserved = new[] { "www", "app", "admin", "api", "identity" };
                    if (slug != null && reserved.Contains(slug, StringComparer.OrdinalIgnoreCase))
                        slug = null;
                }
            }

            var resolved = await tenantResolver.ResolveAsync(host, slug, context.RequestAborted);

            if (resolved is null)
            {
                _logger.LogInformation("Tenant not found for host={Host} slug={Slug} path={Path}",
                    host, slug, context.Request.Path);

                // Behavior choice:
                // - If this is the platform domain or a known platform route, allow through.
                // - Otherwise, 404 for unknown custom domains.
                var allowUnknownHosts = _config.GetValue("SaaS:AllowUnknownHosts", false);
                if (allowUnknownHosts)
                {
                    await _next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            // 3) Set tenant context
            tenantContext.TenantId = resolved.TenantId;
            tenantContext.Slug = resolved.Slug;
            tenantContext.Host = resolved.MatchedHost ?? host; // prefer result's matched host if you populate it

            // Optional convenience
            context.Items["TenantId"] = resolved.TenantId;
            context.Items["TenantSlug"] = resolved.Slug;

            await _next(context);
        }

        private static bool ShouldBypassTenantResolution(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/_TenantDebug", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }


            // Static + known files
            if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Platform / Identity / Admin routes (no tenant required)
            if (path.StartsWith("/Identity", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/app", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
