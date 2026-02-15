using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy;

public sealed class AdminTenantResolutionMiddleware
{
    private const string TenantCookieName = "wb.admin.tenant";

    private readonly RequestDelegate _next;

    public AdminTenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenant,
        DataContext db)
    {
        var ct = context.RequestAborted; // ✅ correct cancellation token for middleware

        // Only apply to /Admin routes (but NOT login/logout pages)
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Admin/Account", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // If already resolved (e.g., you set it elsewhere), continue
        if (tenant.IsResolved)
        {
            await _next(context);
            return;
        }

        // Try resolve from cookie
        if (!context.Request.Cookies.TryGetValue(TenantCookieName, out var cookieVal) ||
            !Guid.TryParse(cookieVal, out var tenantId) ||
            tenantId == Guid.Empty)
        {
            // No tenant selected yet: allow Switch page, otherwise redirect
            if (!path.StartsWith("/Admin/Tenants/Switch", StringComparison.OrdinalIgnoreCase))
            {
                var returnUrl = context.Request.Path + context.Request.QueryString;
                context.Response.Redirect($"/Admin/Tenants/Switch?returnUrl={Uri.EscapeDataString(returnUrl)}");
                return;
            }

            await _next(context);
            return;
        }

        // Verify tenant exists and is active
        var t = await db.Tenants
            .AsNoTracking()
            .Where(x => x.Id == tenantId && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.Slug })
            .FirstOrDefaultAsync(ct);

        if (t is null)
        {
            // Bad cookie: delete it and redirect to Switch
            context.Response.Cookies.Delete(TenantCookieName);
            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Response.Redirect($"/Admin/Tenants/Switch?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return;
        }

        // Optional: fetch primary domain (best-effort)
        var primaryHost = await db.DomainMappings
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.IsActive && !d.IsDeleted)
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.Id)
            .Select(d => d.Host)
            .FirstOrDefaultAsync(ct);

        // ✅ Populate your existing ITenantContext implementation
        tenant.TenantId = t.Id;
        tenant.Slug = t.Slug;
        tenant.Host = primaryHost ?? t.Slug;

        await _next(context);
    }
}
