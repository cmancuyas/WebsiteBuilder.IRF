using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Middleware
{
    public sealed class TenantResolutionMiddleware
    {
        private const string AdminTenantCookie = "wb.admin.tenant";

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
            var path = context.Request.Path.Value ?? string.Empty;
            var isAdminRoute = context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase);

            // ✅ Always use host WITHOUT port
            var host = context.Request.Host.Host?.Trim().ToLowerInvariant() ?? string.Empty;

            // =========================
            // 0) Admin: resolve tenant via cookie (NOT by host)
            // =========================
            if (isAdminRoute)
            {
                // Platform admin host: do NOT resolve tenant at all
                if (IsPlatformAdminHost(host, _config))
                {
                    await _next(context);
                    return;
                }

                // Resolve tenant from cookie if present
                if (context.Request.Cookies.TryGetValue(AdminTenantCookie, out var v) &&
                    Guid.TryParse(v, out var tenantId) &&
                    tenantId != Guid.Empty)
                {
                    var resolved = await tenantResolver.ResolveByIdAsync(tenantId, context.RequestAborted);

                    if (resolved is not null)
                    {
                        SetTenantContext(context, tenantContext, resolved, hostOverride: resolved.Host);
                        await _next(context);
                        return; // ✅ critical: do not fall through to public resolution
                    }

                    // Cookie exists but tenant not found -> treat as "not selected"
                    _logger.LogWarning(
                        "Admin tenant cookie points to missing tenant | tenantId={TenantId} path={Path}",
                        tenantId, path);
                }

                // No cookie (or invalid / missing tenant): proceed unresolved;
                // Admin guard (separate middleware/filter) should redirect to /Admin/Tenants/Switch.
                await _next(context);
                return; // ✅ critical: do not fall through to public resolution
            }

            // =========================
            // 1) Static/system bypass (public only)
            // =========================
            if (ShouldBypassTenantResolution(context))
            {
                await _next(context);
                return;
            }

            // =========================
            // 2) Local/dev bypass (keep)
            // =========================
            if (string.IsNullOrWhiteSpace(host) || host is "localhost" or "127.0.0.1")
            {
                await _next(context);
                return;
            }

            // =========================
            // 3) HYBRID: detect “platform admin host”
            //    If it’s the platform host but NOT /Admin (public marketing pages),
            //    do not resolve tenant.
            // =========================
            if (IsPlatformAdminHost(host, _config))
            {
                await _next(context);
                return;
            }

            // =========================
            // 4) Resolve tenant normally (public tenant hosts)
            // =========================
            string? slug = null;

            var platformDomain = _config["SaaS:PlatformDomain"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(platformDomain))
            {
                // Root platform domain (public pages) → no tenant
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
                }
            }

            var resolvedTenant = await tenantResolver.ResolveAsync(host, slug, context.RequestAborted);

            if (resolvedTenant is null)
            {
                _logger.LogInformation("Tenant not found | host={Host} slug={Slug} path={Path}", host, slug, path);

                if (_config.GetValue("SaaS:AllowUnknownHosts", false))
                {
                    await _next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            SetTenantContext(context, tenantContext, resolvedTenant, hostOverride: host);
            await _next(context);
        }

        private static void SetTenantContext(
            HttpContext context,
            ITenantContext tenantContext,
            ResolvedTenant resolved,
            string hostOverride)
        {
            tenantContext.TenantId = resolved.TenantId;
            tenantContext.Slug = resolved.Slug ?? string.Empty;
            tenantContext.Host = (hostOverride ?? string.Empty).Trim().ToLowerInvariant();

            context.Items["TenantId"] = resolved.TenantId;
            context.Items["TenantSlug"] = resolved.Slug ?? string.Empty;
        }

        private static bool IsPlatformAdminHost(string host, IConfiguration config)
        {
            host = (host ?? string.Empty).Trim().ToLowerInvariant();

            var platformDomain = config["SaaS:PlatformDomain"]?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(platformDomain)) return false;

            // Root platform domain
            if (host.Equals(platformDomain, StringComparison.OrdinalIgnoreCase))
                return true;

            // Reserved subdomains on platform domain
            if (host.EndsWith("." + platformDomain, StringComparison.OrdinalIgnoreCase))
            {
                var sub = host[..^(platformDomain.Length + 1)];
                var first = sub.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                return first.Equals("admin", StringComparison.OrdinalIgnoreCase)
                    || first.Equals("app", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool ShouldBypassTenantResolution(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return true;

            // NOTE: DO NOT include "/Admin" here anymore;
            // Admin is handled explicitly at the top of InvokeAsync.

            if (path.StartsWith("/Preview", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/sitemap", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/sitemaps", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("/_TenantDebug", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase))
                return true;

            if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
