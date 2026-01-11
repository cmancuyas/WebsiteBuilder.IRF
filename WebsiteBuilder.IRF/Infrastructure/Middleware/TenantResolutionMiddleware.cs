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
            // 1) Bypass tenant resolution for system/platform routes
            if (ShouldBypassTenantResolution(context))
            {
                await _next(context);
                return;
            }

            // ✅ Always use host WITHOUT port
            var host = context.Request.Host.Host?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(host))
            {
                await _next(context);
                return;
            }

            // Local/dev bypass
            if (host is "localhost" or "127.0.0.1")
            {
                await _next(context);
                return;
            }

            string? slug = null;

            var platformDomain = _config["SaaS:PlatformDomain"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(platformDomain))
            {
                // Root platform domain → no tenant
                if (host.Equals(platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }

                // Platform subdomain (tenant slug)
                if (host.EndsWith("." + platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var sub = host[..^(platformDomain.Length + 1)];
                    slug = sub.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                    var reserved = new[] { "www", "app", "admin", "api", "identity" };
                    if (slug != null && reserved.Contains(slug, StringComparer.OrdinalIgnoreCase))
                        slug = null;
                }
            }

            var resolved = await tenantResolver.ResolveAsync(host, slug, context.RequestAborted);

            if (resolved is null)
            {
                _logger.LogInformation(
                    "Tenant not found | host={Host} slug={Slug} path={Path}",
                    host, slug, context.Request.Path);

                if (_config.GetValue("SaaS:AllowUnknownHosts", false))
                {
                    await _next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            // ✅ Canonical tenant context assignment
            tenantContext.TenantId = resolved.TenantId;
            tenantContext.Slug = resolved.Slug ?? string.Empty;
            tenantContext.Host = host;

            // Optional convenience
            context.Items["TenantId"] = resolved.TenantId;
            context.Items["TenantSlug"] = resolved.Slug;

            await _next(context);
        }

        private static bool ShouldBypassTenantResolution(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/_TenantDebug", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("/Identity", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/app", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
