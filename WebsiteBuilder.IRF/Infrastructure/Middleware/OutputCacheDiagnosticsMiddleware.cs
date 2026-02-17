using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Middleware;

public sealed class OutputCacheDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;

    public OutputCacheDiagnosticsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenant, IWebHostEnvironment env)
    {
        // Reduce noise: only decorate "public page" responses.
        // (Your OutputCache policy already blocks caching on these paths; this mirrors that intent.)
        var path = ctx.Request.Path.Value ?? "/";
        if (IsExcludedPath(path))
        {
            await _next(ctx);
            return;
        }

        ctx.Response.OnStarting(() =>
        {
            // If policy already set HIT/MISS, keep it. Otherwise default to MISS.
            if (!ctx.Response.Headers.ContainsKey("X-OutputCache"))
                ctx.Response.Headers.TryAdd("X-OutputCache", "MISS");

            if (tenant.IsResolved)
                ctx.Response.Headers.TryAdd("X-TenantId", tenant.TenantId.ToString());

            if (ctx.Items.TryGetValue("PageId", out var pageIdObj) && pageIdObj is int pageId)
                ctx.Response.Headers.TryAdd("X-PageId", pageId.ToString());

            // Optional: extra debug-only headers (avoid leaking internals in prod)
            if (env.IsDevelopment())
            {
                // Example: show normalized path (if you store it somewhere), etc.
                // ctx.Response.Headers.TryAdd("X-Debug-Path", path);
            }

            return Task.CompletedTask;
        });

        await _next(ctx);
    }

    private static bool IsExcludedPath(string p)
    {
        return p.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/sitemap", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/sitemaps", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);
    }
}
