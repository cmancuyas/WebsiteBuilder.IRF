using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantResolver : ITenantResolver
    {
        private readonly DataContext _db;
        private readonly IConfiguration _cfg;

        public TenantResolver(DataContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
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

            // If no verified+activated domain exists, prefer platform host
            if (string.IsNullOrWhiteSpace(primaryHost))
            {
                var platformHost = BuildPlatformHost(tenant.Slug ?? "");
                primaryHost = platformHost; // may still be empty
            }

            return new ResolvedTenant(
                TenantId: tenant.Id,
                Slug: tenant.Slug ?? string.Empty,
                RequestHost: string.Empty, // unknown
                PrimaryHost: HostNormalizer.Normalize(primaryHost),
                IsAlias: false
            );
        }

        public async Task<ResolvedTenant?> ResolveAsync(string host, string? slug, CancellationToken ct = default)
        {
            var requestHost = HostNormalizer.Normalize(host);
            var slugNorm = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(requestHost))
                return null;

            // 1) Resolve by explicit verified+activated domain mapping
            var mapping = await _db.DomainMappings
                .AsNoTracking()
                .Where(dm =>
                    dm.NormalizedHost == requestHost &&
                    !dm.IsDeleted &&
                    dm.IsActive &&
                    dm.VerificationStatusId == DomainVerificationStatus.Verified &&
                    dm.ActivatedAt != null)
                .OrderByDescending(dm => dm.IsPrimary)
                .ThenBy(dm => dm.Id)
                .Select(dm => new { dm.TenantId, dm.IsPrimary })
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

                if (string.IsNullOrWhiteSpace(primaryHost))
                {
                    // fallback: platform host if possible, else request host
                    var platformHost = BuildPlatformHost(tenant.Slug ?? "");
                    primaryHost = !string.IsNullOrWhiteSpace(platformHost) ? platformHost : requestHost;
                }

                primaryHost = HostNormalizer.Normalize(primaryHost);

                return new ResolvedTenant(
                    TenantId: tenant.Id,
                    Slug: tenant.Slug ?? string.Empty,
                    RequestHost: requestHost,
                    PrimaryHost: primaryHost,
                    IsAlias: !requestHost.Equals(primaryHost, StringComparison.OrdinalIgnoreCase)
                );
            }

            // 2) Fallback by slug (platform subdomain routing)
            if (slugNorm is not null)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => !t.IsDeleted && t.IsActive && t.Slug == slugNorm)
                    .Select(t => new { t.Id, t.Slug })
                    .FirstOrDefaultAsync(ct);

                if (tenant is null) return null;

                var primaryHost = await GetPrimaryHostAsync(tenant.Id, ct);

                if (string.IsNullOrWhiteSpace(primaryHost))
                {
                    // canonical for slug routing should be the platform host if available
                    var platformHost = BuildPlatformHost(tenant.Slug ?? "");
                    primaryHost = !string.IsNullOrWhiteSpace(platformHost) ? platformHost : requestHost;
                }

                primaryHost = HostNormalizer.Normalize(primaryHost);

                return new ResolvedTenant(
                    TenantId: tenant.Id,
                    Slug: tenant.Slug ?? string.Empty,
                    RequestHost: requestHost,
                    PrimaryHost: primaryHost,
                    IsAlias: !requestHost.Equals(primaryHost, StringComparison.OrdinalIgnoreCase)
                );
            }

            return null;
        }

        private async Task<string> GetPrimaryHostAsync(Guid tenantId, CancellationToken ct)
        {
            var host = await _db.DomainMappings
                .AsNoTracking()
                .Where(d =>
                    d.TenantId == tenantId &&
                    !d.IsDeleted &&
                    d.IsActive &&
                    d.VerificationStatusId == DomainVerificationStatus.Verified &&
                    d.ActivatedAt != null)
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Id)
                .Select(d => d.NormalizedHost)
                .FirstOrDefaultAsync(ct);

            return HostNormalizer.Normalize(host);
        }

        private string BuildPlatformHost(string slug)
        {
            var platformDomain = HostNormalizer.Normalize(_cfg["SaaS:PlatformDomain"]);
            if (string.IsNullOrWhiteSpace(platformDomain) || string.IsNullOrWhiteSpace(slug))
                return string.Empty;

            return HostNormalizer.Normalize($"{slug}.{platformDomain}");
        }
    }
}