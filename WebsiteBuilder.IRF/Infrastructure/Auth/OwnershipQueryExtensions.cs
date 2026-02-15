using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Auth;

public static class OwnershipQueryExtensions
{
    public static Guid? TryGetUserGuid(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Guid.TryParse(id, out var guid) ? guid : null;
    }

    public static bool IsSuperAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.SuperAdmin);
    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AppRoles.Admin);

    public static IQueryable<Page> ApplyPageVisibility(this IQueryable<Page> q, ClaimsPrincipal user, ITenantContext tenant)
    {
        q = q.Where(x => x.TenantId == tenant.TenantId);

        if (user.IsSuperAdmin() || user.IsAdmin())
            return q;

        var userId = user.TryGetUserGuid();

        // If user id isn't a Guid, don't throw — just return nothing (or decide another policy)
        if (!userId.HasValue)
            return q.Where(x => false);

        return q.Where(x => x.OwnerUserId == userId.Value);
    }

    public static IQueryable<MediaAsset> ApplyMediaVisibility(this IQueryable<MediaAsset> q, ClaimsPrincipal user, ITenantContext tenant)
    {
        q = q.Where(x => x.TenantId == tenant.TenantId);

        if (user.IsSuperAdmin() || user.IsAdmin())
            return q;

        var userId = user.TryGetUserGuid();
        if (!userId.HasValue)
            return q.Where(x => false);

        return q.Where(x => x.OwnerUserId == userId.Value);
    }
}
