using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Auth;

public static class OwnershipQueryExtensions
{
    public static Guid GetUserIdOrThrow(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var guid))
            throw new InvalidOperationException("UserId claim is missing/invalid.");
        return guid;
    }

    public static bool IsSuperAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.SuperAdmin);
    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.Admin);

    public static IQueryable<Page> ApplyPageVisibility(this IQueryable<Page> q, ClaimsPrincipal user, ITenantContext tenant)
    {
        // Always tenant-scope first (unless you explicitly want SuperAdmin cross-tenant views)
        q = q.Where(x => x.TenantId == tenant.TenantId);

        if (user.IsSuperAdmin() || user.IsAdmin())
            return q;

        var userId = user.GetUserIdOrThrow();
        return q.Where(x => x.OwnerUserId == userId);
    }

    public static IQueryable<MediaAsset> ApplyMediaVisibility(this IQueryable<MediaAsset> q, ClaimsPrincipal user, ITenantContext tenant)
    {
        q = q.Where(x => x.TenantId == tenant.TenantId);

        if (user.IsSuperAdmin() || user.IsAdmin())
            return q;

        var userId = user.GetUserIdOrThrow();
        return q.Where(x => x.OwnerUserId == userId);
    }
}
