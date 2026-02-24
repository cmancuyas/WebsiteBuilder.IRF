using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Caching;

public sealed class PublicPageOutputCachePolicy : IOutputCachePolicy
{
    private readonly ITenantContext _tenant;

    public PublicPageOutputCachePolicy(ITenantContext tenant)
    {
        _tenant = tenant;
    }

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        // preview => NO lookup + NO store
        if (IsPreview(http))
        {
            Disable(context);
            return ValueTask.CompletedTask;
        }

        // authenticated => safest default: no cache
        if (http.User?.Identity?.IsAuthenticated == true)
        {
            Disable(context);
            return ValueTask.CompletedTask;
        }

        // must be tenant-resolved (avoid cross-tenant pollution)
        if (!_tenant.IsResolved)
        {
            Disable(context);
            return ValueTask.CompletedTask;
        }

        // Enable caching for eligible anonymous public requests
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;

        // Vary by host (tenant isolation)
        var host = (http.Request.Host.HasValue ? http.Request.Host.Value : "").ToLowerInvariant();
        context.CacheVaryByRules.VaryByValues["host"] = host;

        // Tags
        context.Tags.Add("public-pages");
        context.Tags.Add($"tenant:{_tenant.TenantId}");

        if (http.Items.TryGetValue("ResolvedPageId", out var pageIdObj) &&
            pageIdObj is int pageId && pageId > 0)
        {
            context.Tags.Add($"page:{pageId}");
        }

        // Central TTL
        context.ResponseExpirationTimeSpan = TimeSpan.FromMinutes(5);

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        // Defense-in-depth: preview never stored
        if (IsPreview(http))
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }

        // Store 200 only (no redirects/404/500)
        if (http.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }

        // Don't store if response sets cookies
        if (http.Response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
            return ValueTask.CompletedTask;
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsPreview(HttpContext http)
        => http.Request.Query.ContainsKey("preview");

    private static void Disable(OutputCacheContext context)
    {
        context.EnableOutputCaching = false;
        context.AllowCacheLookup = false;
        context.AllowCacheStorage = false;
    }
}
