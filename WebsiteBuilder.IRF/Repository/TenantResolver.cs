using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.Repository.IRepository;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Repository
{
    public class TenantResolver : ITenantResolver
    {
        private readonly DataContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<TenantResolver> _logger;

        private readonly string _platformDomain;
        private readonly bool _normalizeWww;

        public TenantResolver(
            DataContext db,
            IConfiguration config,
            ILogger<TenantResolver> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;

            _platformDomain = _config["SaaS:PlatformDomain"] ?? string.Empty;
            _normalizeWww = bool.TryParse(_config["SaaS:NormalizeWww"], out var b) && b;
        }

        public async Task<TenantResolutionResult?> ResolveAsync(
            string host,
            string? slug,
            CancellationToken cancellationToken = default)
        {
            var normalizedHost = NormalizeHost(host);

            // 1) Try Custom Domain mapping first
            var mapping = await FindDomainMappingAsync(normalizedHost, cancellationToken);
            if (mapping is not null)
            {
                var tenant = await _db.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.Id == mapping.TenantId &&
                        t.IsActive == true &&
                        t.IsDeleted == false,
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

                _logger.LogWarning(
                    "DomainMapping found but tenant not active/missing. host={Host}, tenantId={TenantId}",
                    normalizedHost, mapping.TenantId);

                return null;
            }

            // 2) If not matched by custom domain, try subdomain slug (Tenant.Slug)
            // Only attempt slug-based resolution if PlatformDomain is configured.
            if (!string.IsNullOrWhiteSpace(_platformDomain))
            {
                // If slug not provided by middleware, infer it from host: {slug}.{platformDomain}
                if (string.IsNullOrWhiteSpace(slug))
                {
                    slug = ExtractSlugFromHost(normalizedHost);
                }

                if (!string.IsNullOrWhiteSpace(slug))
                {
                    var normalizedSlug = NormalizeSlug(slug);

                    var tenant = await _db.Tenants
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t =>
                            t.Slug.ToLower() == normalizedSlug &&
                            t.IsActive == true &&
                            t.IsDeleted == false,
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

        private async Task<DomainMapping?> FindDomainMappingAsync(string normalizedHost, CancellationToken ct)
        {
            // IMPORTANT: IgnoreQueryFilters() prevents tenant filters from hiding mappings pre-resolution.
            return await _db.DomainMappings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Host == normalizedHost &&
                    d.IsActive == true &&
                    d.IsDeleted == false,
                    ct);
        }

        private string NormalizeHost(string host)
        {
            host = (host ?? string.Empty).Trim().ToLowerInvariant().TrimEnd('.');

            if (_normalizeWww && host.StartsWith("www."))
                host = host.Substring(4);

            return host;
        }

        private string NormalizeSlug(string slug)
            => (slug ?? string.Empty).Trim().ToLowerInvariant();

        private string? ExtractSlugFromHost(string normalizedHost)
        {
            var platform = NormalizeHost(_platformDomain);

            // Example: john.yourplatform.com => john
            if (string.IsNullOrWhiteSpace(platform))
                return null;

            if (!normalizedHost.EndsWith("." + platform))
                return null;

            var prefix = normalizedHost.Substring(0, normalizedHost.Length - platform.Length - 1);
            if (string.IsNullOrWhiteSpace(prefix))
                return null;

            // If nested subdomains exist, only take the left-most token as slug
            var parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : null;
        }
    }
}
