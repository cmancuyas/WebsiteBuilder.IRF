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

            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId && !t.IsDeleted && t.IsActive)
                .Select(t => new { t.Id, t.Slug })
                .FirstOrDefaultAsync(ct);

            if (tenant is null) return null;

            var primaryHost = await GetPrimaryHostAsync(tenant.Id, ct);

            return new ResolvedTenant(
                TenantId: tenant.Id,
                Slug: tenant.Slug ?? string.Empty,
                RequestHost: primaryHost,   // when resolving by ID, request host is unknown; set to primary
                PrimaryHost: primaryHost,
                IsAlias: false
            );
        }

        public async Task<ResolvedTenant?> ResolveAsync(string host, string? slug, CancellationToken ct = default)
        {
            var requestHost = HostNormalizer.Normalize(host);
            var slugNorm = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(requestHost))
                return null;

            // 1) Resolve by explicit domain mapping first (custom domain OR platform domains you store)
            // NOTE: Ideally use a HostNormalized column. For now, we assume d.Host is already stored normalized.
            var mapping = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => !d.IsDeleted && d.IsActive)
                .Where(d => d.Host == requestHost) // no ToLower() on column = index friendly
                .Select(d => new { d.TenantId, d.IsPrimary })
                .FirstOrDefaultAsync(ct);

            if (mapping is not null)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == mapping.TenantId && !t.IsDeleted && t.IsActive)
                    .Select(t => new { t.Id, t.Slug })
                    .FirstOrDefaultAsync(ct);

                if (tenant is null) return null;

                var primaryHost = await GetPrimaryHostAsync(tenant.Id, ct);

                // If no primary host exists, treat requestHost as primary to avoid breaking routing
                if (string.IsNullOrWhiteSpace(primaryHost))
                    primaryHost = requestHost;

                return new ResolvedTenant(
                    TenantId: tenant.Id,
                    Slug: tenant.Slug ?? string.Empty,
                    RequestHost: requestHost,
                    PrimaryHost: primaryHost,
                    IsAlias: requestHost != primaryHost
                );
            }

            // 2) Optional: fallback by slug (platform subdomain routing)
            if (slugNorm is not null)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => !t.IsDeleted && t.IsActive)
                    .Where(t => t.Slug == slugNorm) // assuming tenant slugs are stored normalized
                    .Select(t => new { t.Id, t.Slug })
                    .FirstOrDefaultAsync(ct);

                if (tenant is null) return null;

                var primaryHost = await GetPrimaryHostAsync(tenant.Id, ct);
                if (string.IsNullOrWhiteSpace(primaryHost))
                    primaryHost = requestHost;

                return new ResolvedTenant(
                    TenantId: tenant.Id,
                    Slug: tenant.Slug ?? string.Empty,
                    RequestHost: requestHost,
                    PrimaryHost: primaryHost,
                    IsAlias: requestHost != primaryHost
                );
            }

            return null;
        }

        private async Task<string> GetPrimaryHostAsync(Guid tenantId, CancellationToken ct)
        {
            var host = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && !d.IsDeleted && d.IsActive)
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Id)
                .Select(d => d.Host)
                .FirstOrDefaultAsync(ct);

            return HostNormalizer.Normalize(host);
        }
    }
}