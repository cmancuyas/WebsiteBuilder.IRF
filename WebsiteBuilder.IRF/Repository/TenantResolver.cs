using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.Repository.IRepository;

namespace WebsiteBuilder.IRF.Repository
{
    public sealed class TenantResolver : ITenantResolver
    {
        private readonly DataContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<TenantResolver> _logger;

        public TenantResolver(
            DataContext db,
            IConfiguration config,
            ILogger<TenantResolver> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        public async Task<TenantResolutionResult?> ResolveAsync(string host, string? slug, CancellationToken cancellationToken)
        {
            var normalizedHost = NormalizeHost(host);
            if (string.IsNullOrWhiteSpace(normalizedHost))
                return null;

            // 1) Try resolve by custom domain first (DomainMapping.Host)
            var mapping = await FindDomainMappingAsync(normalizedHost, cancellationToken);

            if (mapping is not null)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.Id == mapping.TenantId &&
                        t.IsActive == true,
                        cancellationToken);

                if (tenant is not null)
                {
                    return new TenantResolutionResult
                    {
                        TenantId = tenant.Id,
                        Slug = tenant.Slug,
                        MatchedByCustomDomain = true,
                        MatchedHost = normalizedHost
                    };
                }

                // Domain exists but tenant not active or missing
                _logger.LogWarning("DomainMapping found but tenant not active/missing. host={Host}", normalizedHost);
                return null;
            }

            // 2) If not matched by domain mapping, try subdomain slug (Tenant.Slug)
            if (!string.IsNullOrWhiteSpace(slug))
            {
                var normalizedSlug = NormalizeSlug(slug);

                if (!string.IsNullOrWhiteSpace(normalizedSlug))
                {
                    var tenant = await _db.Tenants
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t =>
                            t.Slug.ToLower() == normalizedSlug &&
                            t.IsActive == true,
                            cancellationToken);

                    if (tenant is not null)
                    {
                        return new TenantResolutionResult
                        {
                            TenantId = tenant.Id,
                            Slug = tenant.Slug,
                            MatchedByCustomDomain = false,
                            MatchedHost = normalizedHost
                        };
                    }
                }
            }

            return null;
        }

        private async Task<Models.DomainMapping?> FindDomainMappingAsync(string normalizedHost, CancellationToken ct)
        {
            // Optional: normalize www
            var normalizeWww = _config.GetValue("SaaS:NormalizeWww", true);

            // try exact
            var mapping = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.IsActive == true && d.Host == normalizedHost)
                .OrderByDescending(d => d.IsPrimary) // primary wins if duplicates exist (shouldn't if Host unique)
                .FirstOrDefaultAsync(ct);

            if (mapping is not null)
                return mapping;

            // try without www.
            if (normalizeWww && normalizedHost.StartsWith("www."))
            {
                var noWww = normalizedHost.Substring(4);

                mapping = await _db.DomainMappings
                    .AsNoTracking()
                    .Where(d => d.IsActive == true && d.Host == noWww)
                    .OrderByDescending(d => d.IsPrimary)
                    .FirstOrDefaultAsync(ct);

                if (mapping is not null)
                    return mapping;
            }

            return null;
        }

        private static string NormalizeHost(string host)
        {
            // Host should be a hostname only (Request.Host.Host gives that)
            // Normalize: trim, lowercase, remove trailing dot
            var h = (host ?? string.Empty).Trim().ToLowerInvariant();
            if (h.EndsWith(".")) h = h.TrimEnd('.');
            return h;
        }

        private static string NormalizeSlug(string slug)
        {
            // For safety: lowercase, trim, take first label only
            var s = (slug ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            // If someone passes "a.b", keep only "a"
            var first = s.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return first;
        }
    }
}
