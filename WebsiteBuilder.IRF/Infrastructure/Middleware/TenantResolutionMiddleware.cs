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
            // ✅ Admin handled elsewhere
            if (context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            // ✅ Canonical host normalization (no port / no trailing dot / lowercase)
            var requestHost = HostNormalizer.Normalize(context.Request.Host.Host);

            // =========================
            // 1) Static/system bypass
            // =========================
            if (ShouldBypassTenantResolution(context))
            {
                await _next(context);
                return;
            }

            // =========================
            // 2) Local/dev bypass (keep)
            // =========================
            if (string.IsNullOrWhiteSpace(requestHost) || requestHost is "localhost" or "127.0.0.1")
            {
                await _next(context);
                return;
            }

            // =========================
            // 3) Platform admin host bypass
            // =========================
            if (IsPlatformAdminHost(requestHost, _config))
            {
                await _next(context);
                return;
            }

            // =========================
            // 4) Resolve tenant (domain mapping first, then platform subdomain slug)
            // =========================
            string? slug = null;

            var platformDomain = HostNormalizer.Normalize(_config["SaaS:PlatformDomain"]);
            if (!string.IsNullOrWhiteSpace(platformDomain))
            {
                // Root platform domain (marketing/root) → no tenant
                if (requestHost.Equals(platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }

                // Platform subdomain -> tenant slug fallback
                if (requestHost.EndsWith("." + platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var sub = requestHost[..^(platformDomain.Length + 1)];
                    slug = sub.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
            }

            var resolved = await tenantResolver.ResolveAsync(requestHost, slug, context.RequestAborted);

            if (resolved is null)
            {
                _logger.LogInformation(
                    "Tenant not found | host={Host} slug={Slug} path={Path}",
                    requestHost, slug, path);

                if (_config.GetValue("SaaS:AllowUnknownHosts", false))
                {
                    await _next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            // ✅ Set tenant context (request host + primary host)
            SetTenantContext(context, tenantContext, resolved);

            // =========================
            // 5) SEO-safe alias -> primary redirect
            //    - skip preview
            //    - skip non-GET/HEAD (avoid breaking POST callbacks)
            // =========================
            if (ShouldRedirectAliasToPrimary(context, tenantContext, resolved))
            {
                var target = BuildPrimaryRedirectUrl(context, tenantContext.PrimaryHost);

                // Prevent intermediary/proxy caching of redirects
                context.Response.Headers.CacheControl = "no-store";

                context.Response.Redirect(target, permanent: true);
                return;
            }

            await _next(context);
        }

        private static void SetTenantContext(
            HttpContext context,
            ITenantContext tenantContext,
            ResolvedTenant resolved)
        {
            tenantContext.TenantId = resolved.TenantId;
            tenantContext.Slug = resolved.Slug ?? string.Empty;

            // Request host (what user typed / DNS pointed)
            tenantContext.Host = HostNormalizer.Normalize(resolved.RequestHost);

            // Primary host (canonical)
            tenantContext.PrimaryHost = HostNormalizer.Normalize(resolved.PrimaryHost);

            context.Items["TenantId"] = resolved.TenantId;
            context.Items["TenantSlug"] = resolved.Slug ?? string.Empty;
            context.Items["TenantHost"] = tenantContext.Host;
            context.Items["TenantPrimaryHost"] = tenantContext.PrimaryHost;
        }

        private static bool ShouldRedirectAliasToPrimary(
            HttpContext context,
            ITenantContext tenantContext,
            ResolvedTenant resolved)
        {
            // No tenant => no redirect
            if (!tenantContext.IsResolved) return false;

            // No primary host => nothing to redirect to
            if (string.IsNullOrWhiteSpace(tenantContext.PrimaryHost)) return false;

            // Preview should never redirect (draft must be viewable on any host in dev/testing)
            if (context.Request.Query.ContainsKey("preview")) return false;

            // Only redirect GET/HEAD (avoid breaking form posts / webhooks)
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                return false;

            // Bypass routes (you already bypass earlier, but keep extra safety)
            if (ShouldBypassTenantResolution(context)) return false;

            // Redirect only when request host != primary host
            var reqHost = HostNormalizer.Normalize(context.Request.Host.Host);
            var primary = HostNormalizer.Normalize(tenantContext.PrimaryHost);

            return !string.IsNullOrWhiteSpace(reqHost)
                   && !string.IsNullOrWhiteSpace(primary)
                   && !reqHost.Equals(primary, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPrimaryRedirectUrl(HttpContext context, string primaryHost)
        {
            var scheme = context.Request.Scheme; // keep https/http (edge should enforce https)
            var path = context.Request.PathBase.Add(context.Request.Path).ToString();
            var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;

            return $"{scheme}://{primaryHost}{path}{query}";
        }

        private static bool IsPlatformAdminHost(string host, IConfiguration config)
        {
            host = HostNormalizer.Normalize(host);

            var platformDomain = HostNormalizer.Normalize(config["SaaS:PlatformDomain"]);
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