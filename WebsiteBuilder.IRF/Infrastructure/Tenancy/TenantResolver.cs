using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantResolver : ITenantResolver
    {
        private readonly DataContext _db;

        public TenantResolver(DataContext db)
        {
            _db = db;
        }

        public async Task<ResolvedTenant?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (tenantId == Guid.Empty) return null;

            // Adjust property names if your Tenant model differs
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId && !t.IsDeleted && t.IsActive)
                .Select(t => new { t.Id, t.Slug })
                .FirstOrDefaultAsync(ct);

            if (tenant is null) return null;

            var primaryHost = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.TenantId == tenant.Id && !d.IsDeleted && d.IsActive)
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Id)
                .Select(d => d.Host)
                .FirstOrDefaultAsync(ct);

            return new ResolvedTenant(
                tenant.TenantIdOrIdFix(tenant.Id), // see note below
                tenant.Slug ?? string.Empty,
                (primaryHost ?? string.Empty).Trim().ToLowerInvariant()
            );
        }

        public async Task<ResolvedTenant?> ResolveAsync(string host, string? slug, CancellationToken ct = default)
        {
            host = (host ?? string.Empty).Trim().ToLowerInvariant();
            slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

            // 1) Resolve by explicit host mapping first
            var mapped = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.Host.ToLower() == host)
                .Where(d => !d.IsDeleted && d.IsActive)
                .OrderByDescending(d => d.IsPrimary)
                .Select(d => new { d.TenantId })
                .FirstOrDefaultAsync(ct);

            if (mapped is not null)
            {
                var byId = await ResolveByIdAsync(mapped.TenantId, ct);
                if (byId is not null)
                    return byId with { Host = host }; // keep current request host if you want
            }

            // 2) If using platform subdomain slug resolution, fallback by slug
            if (slug is not null)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Slug.ToLower() == slug)
                    .Where(t => !t.IsDeleted && t.IsActive)
                    .Select(t => new { t.Id, t.Slug })
                    .FirstOrDefaultAsync(ct);

                if (tenant is null) return null;

                var primaryHost = await _db.DomainMappings
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenant.Id && !d.IsDeleted && d.IsActive)
                    .OrderByDescending(d => d.IsPrimary)
                    .ThenBy(d => d.Id)
                    .Select(d => d.Host)
                    .FirstOrDefaultAsync(ct);

                // Prefer mapped domain if it exists; else keep request host
                var finalHost = !string.IsNullOrWhiteSpace(primaryHost) ? primaryHost : host;

                return new ResolvedTenant(
                    tenant.TenantIdOrIdFix(tenant.Id), // see note below
                    tenant.Slug ?? string.Empty,
                    finalHost.Trim().ToLowerInvariant()
                );
            }

            return null;
        }
    }

    internal static class TenantResolverExtensions
    {
        // If your tenant PK is Id (Guid), just return id.
        // This helper exists only because I don’t know if your Tenant model uses TenantId or Id.
        public static Guid TenantIdOrIdFix(this object _, Guid id) => id;
    }
}
