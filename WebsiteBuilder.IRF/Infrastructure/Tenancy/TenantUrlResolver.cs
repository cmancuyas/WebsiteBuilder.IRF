using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantUrlResolver : ITenantUrlResolver
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IHttpContextAccessor _http;

        public TenantUrlResolver(DataContext db, ITenantContext tenant, IHttpContextAccessor http)
        {
            _db = db;
            _tenant = tenant;
            _http = http;
        }

        public async Task<(string Scheme, string Host)> GetCanonicalAsync(CancellationToken ct = default)
        {
            var req = _http.HttpContext?.Request;

            // If tenant isn't resolved, fall back to request host/scheme safely.
            if (_tenant.TenantId == Guid.Empty || req is null)
            {
                var fallbackHost = (req?.Host.Host ?? "localhost").Trim().ToLowerInvariant();
                var fallbackScheme = (req?.Scheme ?? "https").Trim().ToLowerInvariant();
                return (fallbackScheme, fallbackHost);
            }

            var mappedHost = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.TenantId == _tenant.TenantId)
                .Where(d => !d.IsDeleted && d.IsActive)
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Id)
                .Select(d => d.Host)
                .FirstOrDefaultAsync(ct);

            // Prefer mapped canonical host; otherwise fall back to current request host (without port).
            var host = !string.IsNullOrWhiteSpace(mappedHost)
                ? mappedHost.Trim()
                : (req.Host.Host?.Trim() ?? "localhost");

            // If we have a mapped host, force https canonical (common SaaS assumption).
            // Otherwise, respect request scheme (dev may be http).
            var scheme = !string.IsNullOrWhiteSpace(mappedHost)
                ? "https"
                : (req.Scheme ?? "https");

            return (scheme.Trim().ToLowerInvariant(), host.Trim().ToLowerInvariant());
        }
    }
}
