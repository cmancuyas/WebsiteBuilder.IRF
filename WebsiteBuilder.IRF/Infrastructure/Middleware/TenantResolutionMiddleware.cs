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
            var host = context.Request.Host.Host?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(host))
            {
                await _next(context);
                return;
            }

            // Determine platform slug if this is a platform subdomain
            string? slug = null;

            var platformDomain = _config["SaaS:PlatformDomain"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(platformDomain))
            {
                if (host.Equals(platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    // Root marketing/app domain – no tenant
                    await _next(context);
                    return;
                }

                if (host.EndsWith("." + platformDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var sub = host[..^(platformDomain.Length + 1)];
                    slug = sub.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                    // Optional: reserved subdomains
                    var reserved = new[] { "www", "app", "admin", "api" };
                    if (slug != null && reserved.Contains(slug, StringComparer.OrdinalIgnoreCase))
                        slug = null;
                }
            }

            var resolved = await tenantResolver.ResolveAsync(host, slug, context.RequestAborted);

            if (resolved is null)
            {
                _logger.LogInformation("Tenant not found for host={Host} slug={Slug} path={Path}",
                    host, slug, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found.");
                return;
            }

            tenantContext.TenantId = resolved.TenantId;
            tenantContext.Slug = resolved.Slug;
            tenantContext.Host = host;

            // Optional convenience
            context.Items["TenantId"] = resolved.TenantId;
            context.Items["TenantSlug"] = resolved.Slug;

            await _next(context);
        }
    }
}
