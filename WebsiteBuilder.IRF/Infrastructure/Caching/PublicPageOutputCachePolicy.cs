using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Caching
{
    public sealed class PublicPageOutputCachePolicy : IOutputCachePolicy
    {
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        // ------------------------------------------------------------
        // 1️⃣ Decide if request should be cached
        // ------------------------------------------------------------
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;
            var req = http.Request;

            // Only cache GET / HEAD
            if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method))
            {
                context.EnableOutputCaching = false;
                return ValueTask.CompletedTask;
            }

            // Never cache preview mode
            if (req.Query.ContainsKey("preview"))
            {
                context.EnableOutputCaching = false;
                return ValueTask.CompletedTask;
            }

            // Tenant must be resolved
            var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenant.IsResolved)
            {
                context.EnableOutputCaching = false;
                return ValueTask.CompletedTask;
            }

            context.EnableOutputCaching = true;
            context.AllowCacheLookup = true;
            context.AllowCacheStorage = true;

            // Expiration
            context.ResponseExpirationTimeSpan = DefaultTtl;

            // Multi-tenant safety
            context.CacheVaryByRules.VaryByHost = true;

            // Broad tag bucket for mass eviction
            context.Tags.Add("public-pages");

            return ValueTask.CompletedTask;
        }

        // ------------------------------------------------------------
        // 2️⃣ Guard when serving from cache
        // ------------------------------------------------------------
        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;
            var req = http.Request;

            // Never serve cached preview
            if (req.Query.ContainsKey("preview"))
            {
                context.AllowCacheLookup = false;
                context.AllowCacheStorage = false;
                context.EnableOutputCaching = false;
                return ValueTask.CompletedTask;
            }

            // Ensure tenant is still resolved
            var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenant.IsResolved)
            {
                context.AllowCacheLookup = false;
                context.AllowCacheStorage = false;
                context.EnableOutputCaching = false;
                return ValueTask.CompletedTask;
            }

            return ValueTask.CompletedTask;
        }

        // ------------------------------------------------------------
        // 3️⃣ After response executed (tag per page + tenant)
        // ------------------------------------------------------------
        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;

            // These are set inside OnGetAsync
            if (http.Items.TryGetValue("PageId", out var pageIdObj) && pageIdObj is int pageId)
            {
                context.Tags.Add($"page:{pageId}");
            }

            if (http.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is Guid tenantId)
            {
                context.Tags.Add($"tenant:{tenantId}");
            }

            return ValueTask.CompletedTask;
        }
    }
}
